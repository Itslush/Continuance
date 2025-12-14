using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using System.Net;
using Continuance.Models;
using Continuance.Roblox.Http;
using Continuance.CLI;

namespace Continuance.Roblox.Services
{
    public class FriendService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public static async Task<(bool Success, bool IsPendingOrFriends, string FailureReason)> SendFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(account);

            if (account == null) return (false, false, "Account is null.");
            if (string.IsNullOrEmpty(account.XcsrfToken)) return (false, false, "Missing XCSRF token.");
            if (friendUserId <= 0) return (false, false, $"Invalid friend User ID ({friendUserId}).");
            if (account.UserId == friendUserId) return (false, false, "Cannot friend yourself.");

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/request-friendship";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var (statusCode, isSuccess, responseContent) = await RobloxHttpClient.SendRequest(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Send Friend Request to {friendUsername} ({friendUserId})",
                allowRetryOnXcsrf: true
            );

            if (isSuccess)
            {
                return (true, false, string.Empty);
            }
            else
            {
                if (statusCode == HttpStatusCode.BadRequest && !string.IsNullOrEmpty(responseContent))
                {
                    try
                    {
                        var errorJson = JObject.Parse(responseContent);
                        if (errorJson["errors"] is JArray errors &&
                            errors.Any(err => err["code"]?.Value<int>() == 5))
                        {
                            Logger.LogWarning($"Send Fail (Code 5: Request likely pending or already friends with {friendUsername}).");
                            return (false, true, "Request pending or already friends (Code 5).");
                        }
                    }
                    catch (JsonException) { }
                }
                string reason = $"API Error: {statusCode?.ToString() ?? "Unknown"} - {responseContent}";
                return (false, false, reason);
            }
        }

        public static async Task<bool> AcceptFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            if (account == null) { Logger.LogError("Cannot AcceptFriendRequest: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Logger.LogWarning($"Cannot AcceptFriendRequest for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (friendUserId <= 0) { Logger.LogError($"Cannot AcceptFriendRequest for {account.Username}: Invalid friend User ID ({friendUserId})."); return false; }
            if (account.UserId == friendUserId) { Logger.LogError($"Cannot AcceptFriendRequest for {account.Username}: Cannot accept request from yourself."); return false; }

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/accept-friend-request";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            bool success = await RobloxHttpClient.SendRequestAsync(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Accept Friend Request from {friendUsername} ({friendUserId})",
                allowRetryOnXcsrf: true
                );

            return success;
        }

        public static async Task<int> GetFriendCountAsync(Account account)
        {
            if (account == null) { Logger.LogError("Cannot GetFriendCount: Account is null."); return -1; }
            if (account.UserId <= 0) { Logger.LogError($"Cannot GetFriendCount: Invalid User ID ({account.UserId}) in Account object."); return -1; }

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{account.UserId}/friends/count";

            var (statusCode, success, content) = await RobloxHttpClient.SendRequest(
                HttpMethod.Get,
                url,
                account,
                null,
                "Get Friend Count",
                allowRetryOnXcsrf: false,
                suppressOutput: true
            );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    if (json.TryGetValue("count", StringComparison.OrdinalIgnoreCase, out JToken? countToken) && countToken.Type == JTokenType.Integer)
                    {
                        return countToken.Value<int>();
                    }
                    else
                    {
                        Logger.LogError($"Could not parse friend count (missing/invalid 'count' property) from response for {account.Username}: {ConsoleUI.Truncate(content)}");
                    }
                }
                catch (JsonReaderException jex)
                {
                    Logger.LogError($"Error parsing friend count JSON for {account.Username}: {jex.Message}");
                }
                catch (Exception ex) { Logger.LogError($"Error processing friend count response for {account.Username}: {ex.Message}"); }
            }
            return -1;
        }
    }
}