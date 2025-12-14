using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;
using Continuance.Models;
using Continuance.Roblox.Http;
using Spectre.Console;
using Continuance.CLI;

namespace Continuance.Roblox.Services
{
    public class UserService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public static async Task<bool> SetDisplayNameAsync(Account account, string newDisplayName)
        {
            if (account == null) { Logger.LogError("Cannot SetDisplayName: Account is null."); return false; }

            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Logger.LogWarning($"Cannot SetDisplayName for {Markup.Escape(account.Username)}: Missing XCSRF token.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(newDisplayName) || newDisplayName.Length < 3 || newDisplayName.Length > 20)
            {
                Logger.LogError($"Cannot SetDisplayName for {Markup.Escape(account.Username)}: Invalid name '{Markup.Escape(newDisplayName)}'. Must be 3-20 characters, non-empty.");
                return false;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}/display-names";
            var payload = new JObject { ["newDisplayName"] = newDisplayName };
            var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

            bool success = await RobloxHttpClient.SendRequestAsync(
                HttpMethod.Patch,
                url,
                account,
                content,
                $"Set Display Name to '{Markup.Escape(newDisplayName)}'",
                allowRetryOnXcsrf: true
                );

            return success;
        }

        public static async Task<(string? DisplayName, string? Username)> GetUsernamesAsync(Account account)
        {
            if (account == null) { Logger.LogError("Cannot GetUsernames: Account is null."); return (null, null); }
            if (account.UserId <= 0) { Logger.LogError($"Cannot GetUsernames: Invalid User ID ({account.UserId})."); return (null, null); }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}";

            var (statusCode, success, content) = await RobloxHttpClient.SendRequest(
                HttpMethod.Get,
                url,
                account,
                null,
                $"Get User Info for {account.UserId}",
                allowRetryOnXcsrf: false,
                suppressOutput: true
            );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    string? displayName = json["displayName"]?.Value<string>();
                    string? username = json["name"]?.Value<string>();

                    if (!string.IsNullOrWhiteSpace(username) && account.Username != username && account.Username != "N/A")
                    {
                        Logger.LogInfo($"Updated username cache for ID {account.UserId} from '{Markup.Escape(account.Username)}' to '{Markup.Escape(username)}'.");
                        account.Username = username;
                    }
                    else if (!string.IsNullOrWhiteSpace(username) && account.Username == "N/A")
                    {
                        account.Username = username;
                    }

                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(username))
                    {
                        Logger.LogWarning($"Fetched user info for {account.UserId} has missing/empty name or displayName. Display: '{Markup.Escape(displayName ?? "null")}', User: '{Markup.Escape(username ?? "null")}'");
                    }

                    return (displayName, username);
                }
                catch (JsonReaderException jex)
                {
                    Logger.LogError($"Error parsing user info JSON for {account.UserId}: {Markup.Escape(jex.Message)}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error processing user info for {account.UserId}: {Markup.Escape(ex.Message)}");
                }
            }
            return (null, null);
        }

        public static async Task<string?> GetCurrentDisplayNameAsync(Account account)
        {
            var (displayName, _) = await GetUsernamesAsync(account);
            return displayName;
        }
    }
}