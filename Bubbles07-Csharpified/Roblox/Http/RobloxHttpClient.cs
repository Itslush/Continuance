using Continuance.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using System.Text.RegularExpressions;
using Continuance.CLI;

namespace Continuance.Roblox.Http
{
    public class RobloxHttpClient
    {
        private static readonly HttpClient httpClient = new(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        private const int XcsrfRetryDelayMs = AppConfig.XcsrfRetryDelayMs;

        private static readonly HttpClient externalHttpClient = new(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        public static HttpClient ExternalHttpClient => externalHttpClient;

        private static HttpRequestMessage CreateBaseRequest(HttpMethod method, string url, Account account, HttpContent? content = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                Logger.LogError($"Invalid URL format provided: {Markup.Escape(url)}");
                throw new ArgumentException("Invalid URL format", nameof(url));
            }

            var request = new HttpRequestMessage(method, url);

            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            if (account != null)
            {
                if (!string.IsNullOrEmpty(account.Cookie))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                }
            }

            request.Content = content;
            return request;
        }

        public static async Task<(HttpStatusCode? StatusCode, bool IsSuccess, string Content)> SendRequest(HttpMethod method, string url, Account account, HttpContent? content = null, string actionDescription = "API request", bool allowRetryOnXcsrf = true, Action<HttpRequestMessage>? configureRequest = null, bool suppressOutput = true)
        {
            if (account == null && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete || allowRetryOnXcsrf))
            {
                if (!suppressOutput) Logger.LogError($"Cannot send modifying request '{Markup.Escape(actionDescription)}': Account object is null.");
                return (null, false, "Account object was null for an authenticated request.");
            }

            HttpRequestMessage request;
            var escapedActionDescription = Markup.Escape(actionDescription);

            try
            {
                request = CreateBaseRequest(method, url, account!, content);
                configureRequest?.Invoke(request);
            }
            catch (ArgumentException ex)
            {
                if (!suppressOutput) Logger.LogError($"Request Creation Failed for '{escapedActionDescription}': {Markup.Escape(ex.Message)}");
                return (null, false, ex.Message);
            }
            catch (Exception ex)
            {
                if (!suppressOutput) Logger.LogError($"Unexpected Error Creating Request for '{escapedActionDescription}': {Markup.Escape(ex.Message)}");
                return (null, false, $"Unexpected error during request creation: {ex.Message}");
            }
            bool retried = false;

            retry_request:
            HttpResponseMessage? response = null;

            try
            {
                if (account != null)
                {
                    if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");

                    if (!string.IsNullOrEmpty(account.XcsrfToken))
                    {
                        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", account.XcsrfToken);
                    }
                    else if (allowRetryOnXcsrf && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete))
                    {
                        if (!suppressOutput) Logger.LogWarning($"Attempting modifying request '{escapedActionDescription}' for {Markup.Escape(account.Username)} with missing XCSRF.");
                    }
                }

                using HttpRequestMessage clonedRequest = request.Clone();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                response = await httpClient.SendAsync(clonedRequest, cts.Token);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string username = Markup.Escape(account?.Username ?? "N/A");
                    if (!suppressOutput)
                    {
                        string failedMessage = $"FAILED: {escapedActionDescription} for {username}. Code: {(int)response.StatusCode} ({Markup.Escape(response.ReasonPhrase?.ToString() ?? "")}). URL: {ConsoleUI.Truncate(url, 60)}. Data: {ConsoleUI.Truncate(responseContent)}";
                        Logger.LogError(failedMessage);
                    }

                    if (response.StatusCode == HttpStatusCode.Forbidden &&
                        response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues) &&
                        allowRetryOnXcsrf && !retried && account != null)
                    {
                        string? newToken = csrfHeaderValues?.FirstOrDefault()?.Trim();
                        if (!string.IsNullOrEmpty(newToken) && newToken != account.XcsrfToken)
                        {
                            if (!suppressOutput) Logger.LogWarning($"XCSRF Rotation Detected for {Markup.Escape(account.Username)}. Updating token and retrying...");
                            account.XcsrfToken = newToken;
                            await Task.Delay(XcsrfRetryDelayMs);
                            retried = true;
                            response?.Dispose();
                            goto retry_request;
                        }
                        else if (newToken == account.XcsrfToken)
                        {
                            if (!suppressOutput) Logger.LogWarning($"Received 403 Forbidden for {Markup.Escape(account.Username)} but XCSRF token in response header did not change. Not retrying automatically. ({escapedActionDescription})");
                        }
                        else
                        {
                            if (!suppressOutput) Logger.LogWarning($"Received 403 Forbidden for {Markup.Escape(account.Username)} but X-CSRF-TOKEN header was missing or empty in response. Cannot retry based on this. ({escapedActionDescription})");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        if (!suppressOutput) Logger.LogError($"RATE LIMITED (429) on '{escapedActionDescription}' for {username}. Consider increasing delays. Failing action.");
                        await Task.Delay(AppConfig.RateLimitRetryDelayMs);
                        return (response.StatusCode, false, responseContent);
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized && account != null)
                    {
                        if (!suppressOutput) Logger.LogError($"UNAUTHORIZED (401) on '{escapedActionDescription}' for {Markup.Escape(account.Username)}. Cookie might be invalid. Marking account invalid.");
                        account.IsValid = false;
                        account.XcsrfToken = "";
                        return (response.StatusCode, false, responseContent);
                    }

                    return (response.StatusCode, false, responseContent);
                }
                else
                {
                    return (response.StatusCode, true, responseContent);
                }
            }
            catch (HttpRequestException hrex)
            {
                if (!suppressOutput) Logger.LogError($"NETWORK EXCEPTION: During '{escapedActionDescription}' for {Markup.Escape(account?.Username ?? "N/A")}: {Markup.Escape(hrex.Message)} (StatusCode: {hrex.StatusCode})");
                return (hrex.StatusCode, false, hrex.Message);
            }

            catch (TaskCanceledException)
            {
                if (!suppressOutput) Logger.LogError($"TIMEOUT/CANCELLED: During '{escapedActionDescription}' for {Markup.Escape(account?.Username ?? "N/A")} (Timeout: {AppConfig.DefaultRequestTimeoutSec}s): Request cancelled or timed out.");
                return (HttpStatusCode.RequestTimeout, false, "Request timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                if (!suppressOutput)
                {
                    string generalExceptionMarkup = $"GENERAL EXCEPTION: During '{escapedActionDescription}' for {Markup.Escape(account?.Username ?? "N/A")}: {Markup.Escape(ex.GetType().Name)} - {Markup.Escape(ex.Message)}";
                    Logger.LogError(generalExceptionMarkup);
                }
                return (null, false, ex.Message);
            }
            finally
            {
                response?.Dispose();
            }
        }

        public static async Task<bool> SendRequestAsync(
            HttpMethod method,
            string url,
            Account account,
            HttpContent? content = null,
            string actionDescription = "API request",
            bool allowRetryOnXcsrf = true,
            Action<HttpRequestMessage>? configureRequest = null)
        {
            var (_, isSuccess, _) = await SendRequest(method, url, account, content, actionDescription, allowRetryOnXcsrf, configureRequest);
            return isSuccess;
        }

        public static async Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie, bool suppressOutput = true)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return (false, 0, "N/A");

            string validationUrl = AppConfig.RobloxApiBaseUrl_Users + "/v1/users/authenticated";
            using var request = new HttpRequestMessage(HttpMethod.Get, validationUrl);
            request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            HttpResponseMessage? response = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                response = await httpClient.SendAsync(request, cts.Token);

                string jsonString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    JObject? accountInfo = null;
                    try { accountInfo = JObject.Parse(jsonString); }
                    catch (JsonReaderException jex)
                    {
                        if (!suppressOutput) Logger.LogError($"Validation JSON Parse Error: {Markup.Escape(jex.Message)} Response: {ConsoleUI.Truncate(jsonString)}");
                        return (false, 0, "N/A");
                    }

                    long userId = accountInfo?["id"]?.Value<long>() ?? 0;
                    string? username = accountInfo?["name"]?.Value<string>();

                    if (userId > 0 && !string.IsNullOrWhiteSpace(username))
                    {
                        return (true, userId, username);
                    }
                    else
                    {
                        if (!suppressOutput) Logger.LogError($"Validation Error: Parsed user ID ({userId}) or username ('{Markup.Escape(username ?? "null")}') was invalid from response: {ConsoleUI.Truncate(jsonString)}");
                        return (false, 0, "N/A");
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (!suppressOutput) Logger.LogError($"Validation Failed: API request returned {response.StatusCode} (Unauthorized). Cookie is likely invalid or expired.");
                    return (false, 0, "N/A");
                }
                else
                {
                    if (!suppressOutput) Logger.LogError($"Validation Failed: API request returned status {(int)response.StatusCode} ({Markup.Escape(response.ReasonPhrase ?? "")}). Response: {ConsoleUI.Truncate(jsonString)}");
                    return (false, 0, "N/A");
                }
            }
            catch (OperationCanceledException) { if (!suppressOutput) Logger.LogError($"Validation Timeout ({TimeSpan.FromSeconds(15).TotalSeconds}s)."); }
            catch (HttpRequestException hrex) { if (!suppressOutput) Logger.LogError($"Validation Network Error: {Markup.Escape(hrex.Message)} (StatusCode: {hrex.StatusCode})"); }
            catch (Exception ex) { if (!suppressOutput) Logger.LogError($"Validation Exception: {Markup.Escape(ex.GetType().Name)} - {Markup.Escape(ex.Message)}"); }
            finally { response?.Dispose(); }

            return (false, 0, "N/A");
        }

        public static async Task<string> FetchXCSRFTokenAsync(string cookie, bool suppressOutput = true)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return "";

            if (!suppressOutput) Logger.LogInfo("Attempting XCSRF token acquisition...");

            string logoutUrl = AppConfig.RobloxApiBaseUrl_Auth + "/v2/logout";
            using var logoutReq = new HttpRequestMessage(HttpMethod.Post, logoutUrl);
            logoutReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            logoutReq.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "fetch");
            logoutReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            logoutReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            HttpResponseMessage? response = null;
            try
            {
                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                response = await httpClient.SendAsync(logoutReq, HttpCompletionOption.ResponseHeadersRead, cts1.Token);

                if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues))
                {
                    string? token = csrfHeaderValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        if (!suppressOutput) Logger.LogSuccess("XCSRF acquired via POST /logout.");
                        return token.Trim();
                    }
                }
                if (!suppressOutput) Logger.LogError($"POST /logout failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying next method...");
            }
            catch (OperationCanceledException) { if (!suppressOutput) Logger.LogError("XCSRF fetch (POST /logout) timeout."); }
            catch (HttpRequestException hrex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (POST /logout) network exception: {Markup.Escape(hrex.Message)}"); }
            catch (Exception ex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (POST /logout) exception: {Markup.Escape(ex.GetType().Name)} - {Markup.Escape(ex.Message)}"); }

            finally { response?.Dispose(); }

            string bdayUrl = AppConfig.RobloxApiBaseUrl_AccountInfo + "/v1/birthdate";
            using var bdayReq = new HttpRequestMessage(HttpMethod.Post, bdayUrl);

            bdayReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            bdayReq.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "fetch");
            bdayReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            bdayReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            response = null;

            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                response = await httpClient.SendAsync(bdayReq, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues))
                {
                    string? token = csrfHeaderValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        if (!suppressOutput) Logger.LogSuccess($"XCSRF acquired via POST {bdayUrl}.");
                        return token.Trim();
                    }
                }
                if (!suppressOutput) Logger.LogError($"POST {bdayUrl} failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying scrape...");
            }

            catch (OperationCanceledException) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (POST {bdayUrl}) timeout."); }
            catch (HttpRequestException hrex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (POST {bdayUrl}) network exception: {Markup.Escape(hrex.Message)}"); }
            catch (Exception ex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (POST {bdayUrl}) exception: {Markup.Escape(ex.GetType().Name)} - {Markup.Escape(ex.Message)}"); }

            finally { response?.Dispose(); }

            string scrapeUrl = AppConfig.RobloxWebBaseUrl + "/my/account";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, scrapeUrl);
            getReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            getReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            response = null;
            try
            {
                using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                response = await httpClient.SendAsync(getReq, cts3.Token);

                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();

                    var patterns = new Dictionary<string, Regex>
                     {
                         { "JS setToken", new Regex(@"Roblox\.XsrfToken\.setToken\('(.+?)'\)", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(2)) },
                         { "data-csrf-token", new Regex(@"data-csrf-token=[""'](.+?)[""']", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(2)) },
                         { "meta tag", new Regex(@"<meta\s+name=[""']csrf-token[""']\s+data-token=[""'](.+?)[""']", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)) }
                     };

                    foreach (var kvp in patterns)
                    {
                        try
                        {
                            Match match = kvp.Value.Match(html);

                            if (match.Success && match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                            {
                                if (!suppressOutput) Logger.LogSuccess($"XCSRF acquired via scrape (Method: {kvp.Key}).");
                                return match.Groups[1].Value.Trim();
                            }
                        }
                        catch (RegexMatchTimeoutException) { if (!suppressOutput) Logger.LogWarning($"XCSRF fetch (Scrape) regex timeout for pattern: {kvp.Key}."); }
                    }

                    if (!suppressOutput) Logger.LogWarning($"Scrape successful ({response.StatusCode}) but token not found in HTML content with known patterns.");
                }
                else { if (!suppressOutput) Logger.LogError($"Scrape failed ({response.StatusCode})."); }
            }

            catch (OperationCanceledException) { if (!suppressOutput) Logger.LogError("XCSRF fetch (Scrape) timeout."); }
            catch (HttpRequestException hrex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (Scrape) network exception: {Markup.Escape(hrex.Message)}"); }
            catch (Exception ex) { if (!suppressOutput) Logger.LogError($"XCSRF fetch (Scrape) exception: {Markup.Escape(ex.GetType().Name)} - {Markup.Escape(ex.Message)}"); }

            finally { response?.Dispose(); }

            if (!suppressOutput) Logger.LogError("Failed to acquire XCSRF Token using all methods.");

            return "";
        }
    }
}