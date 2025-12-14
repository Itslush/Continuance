using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using Continuance.Models;
using Continuance.Roblox.Http;
using Spectre.Console;
using Continuance.CLI;

namespace Continuance.Roblox.Services
{
    public class AvatarService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public static async Task<AvatarDetails?> FetchAvatarDetailsAsync(long userId)
        {
            if (userId <= 0)
            {
                Logger.LogError("Cannot fetch avatar details: Invalid User ID provided.");
                return null;
            }

            string avatarUrl = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/users/{userId}/avatar";
            JObject? avatarData = null;
            HttpResponseMessage? response = null;

            try
            {
                var externalClient = RobloxHttpClient.ExternalHttpClient;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                response = await externalClient.GetAsync(avatarUrl, cts.Token);

                string jsonString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        avatarData = JObject.Parse(jsonString);
                    }
                    catch (JsonReaderException jex)
                    {
                        Logger.LogError($"Error parsing avatar details JSON for {userId}: {jex.Message}. Response: {jsonString}");
                        return null;
                    }
                }
                else
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Logger.LogError($"Failed to fetch avatar details for {userId}: 404 Not Found (User may not exist?).");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        Logger.LogError($"Failed to fetch avatar details for {userId}: 400 Bad Request. Details: {jsonString}");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Logger.LogError($"Failed to fetch avatar details for {userId}: 429 Too Many Requests. Check delays.");
                    }
                    else
                    {
                        Logger.LogError($"Failed to fetch avatar details for {userId}: {(int)response.StatusCode} {response.ReasonPhrase}. Details: {jsonString}");
                    }
                    return null;
                }
            }
            catch (OperationCanceledException) { Logger.LogError($"Timeout fetching avatar details for {userId}."); return null; }
            catch (HttpRequestException hrex) { Logger.LogError($"Network error fetching avatar details for {userId}: {hrex.Message}"); return null; }
            catch (Exception ex) { Logger.LogError($"Exception fetching avatar details for {userId}: {ex.GetType().Name} - {ex.Message}"); return null; }
            finally { response?.Dispose(); }


            if (avatarData == null) return null;

            try
            {
                var details = new AvatarDetails
                {
                    AssetIds = avatarData["assets"]?
                                .Select(a => a?["id"]?.Value<long>() ?? 0)
                                .Where(id => id != 0)
                                .OrderBy(id => id)
                                .Distinct()
                                .ToList() ?? [],

                    BodyColors = avatarData["bodyColors"] as JObject,
                    PlayerAvatarType = avatarData["playerAvatarType"]?.ToString(),
                    Scales = avatarData["scales"] as JObject,
                    FetchTime = DateTime.UtcNow
                };

                if (details.BodyColors == null || details.PlayerAvatarType == null || details.Scales == null || details.AssetIds == null)
                {
                    Logger.LogWarning($"Fetched avatar data for {userId} was incomplete (missing fields: bodyColors, playerAvatarType, scales, or assets). Content: {ConsoleUI.Truncate(avatarData.ToString())}");
                    return null;
                }

                return details;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error processing fetched avatar JSON data for {userId}: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> SetAvatarAsync(Account account, long sourceUserId)
        {
            if (account == null) { Logger.LogError("Cannot SetAvatar: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Logger.LogWarning($"Cannot SetAvatar for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (sourceUserId <= 0) { Logger.LogError("Cannot SetAvatar: Invalid Source User ID."); return false; }

            Logger.LogInfo($"Action: SetAvatar Source: {sourceUserId} Target: {account.Username}");

            AvatarDetails? targetAvatarDetails = await FetchAvatarDetailsAsync(sourceUserId);

            if (targetAvatarDetails == null)
            {
                Logger.LogError($"Failed to get source avatar details from {sourceUserId}. Cannot proceed.");
                return false;
            }

            if (targetAvatarDetails.AssetIds == null || targetAvatarDetails.BodyColors == null || targetAvatarDetails.PlayerAvatarType == null || targetAvatarDetails.Scales == null)
            {
                Logger.LogError($"Source avatar data fetched from {sourceUserId} is incomplete. Cannot apply.");
                return false;
            }

            var wearPayload = new JObject { ["assetIds"] = new JArray(targetAvatarDetails.AssetIds) };
            var bodyColorsPayload = targetAvatarDetails.BodyColors;
            var avatarTypePayload = new JObject { ["playerAvatarType"] = targetAvatarDetails.PlayerAvatarType };
            var scalesPayload = targetAvatarDetails.Scales;

            Logger.LogAccent($"Applying avatar configuration to {account.Username}...");
            bool overallSuccess = true;

            async Task<bool> ExecuteAvatarStep(Func<Task<bool>> stepAction, string description)
            {
                bool stepSuccess = false;
                try
                {
                    stepSuccess = await stepAction();
                    if (!stepSuccess)
                    {
                        Logger.LogError($"Step Failed: {description} for {account.Username}.");
                        overallSuccess = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"UNEXPECTED ERROR applying {description} for {account.Username}: {ex.GetType().Name} - {ex.Message}");
                    stepSuccess = false;
                    overallSuccess = false;
                }

                if (stepSuccess)
                {
                    await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
                }
                else
                {
                    await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
                }

                return stepSuccess;
            }

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-body-colors";
                if (bodyColorsPayload == null) return false;
                var content = new StringContent(bodyColorsPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await RobloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Body Colors");
            }, "Body Colors")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-player-avatar-type";
                var content = new StringContent(avatarTypePayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await RobloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Avatar Type");
            }, "Avatar Type")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-scales";
                if (scalesPayload == null) return false;
                var content = new StringContent(scalesPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await RobloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Scales");
            }, "Scales")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-wearing-assets";
                var content = new StringContent(wearPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await RobloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Wearing Assets");
            }, "Wearing Assets")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/redraw-thumbnail";
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                return await RobloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Redraw Thumbnail (Final Step)");
            }, "Redraw Thumbnail"))
            {
                Logger.LogWarning($"Avatar settings applied, but final thumbnail redraw failed for {account.Username}.");
                overallSuccess = true;
            }

            return overallSuccess;
        }

        public static bool CompareAvatarDetails(AvatarDetails? details1, AvatarDetails? details2)
        {
            if (ReferenceEquals(details1, details2)) return true;
            if (details1 == null || details2 == null) return false;

            if (!string.Equals(details1.PlayerAvatarType, details2.PlayerAvatarType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (details1.BodyColors == null || details2.BodyColors == null) return false;
            if (!JToken.DeepEquals(details1.BodyColors, details2.BodyColors))
            {
                return false;
            }

            if (details1.Scales == null || details2.Scales == null) return false;
            if (!JToken.DeepEquals(details1.Scales, details2.Scales))
            {
                return false;
            }

            if (details1.AssetIds == null || details2.AssetIds == null) return false;
            if (!details1.AssetIds.SequenceEqual(details2.AssetIds))
            {
                return false;
            }

            return true;
        }
    }
}