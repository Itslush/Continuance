using Spectre.Console;
using System.Text;
using Continuance.Models;
using Continuance.CLI;

namespace Continuance.Core
{
    public static class AccountExporter
    {
        public static async Task<bool> ExportAccountsToFileAsync(string filePath, List<Account> accountsToExport, bool sortByUsername = true)
        {
            if (accountsToExport == null || accountsToExport.Count == 0)
            {
                Logger.LogError("No accounts provided or found matching the filter to export.");
                return false;
            }

            var exportData = new List<(string Cookie, string Username)>();

            foreach (var account in accountsToExport)
            {
                if (account != null && !string.IsNullOrWhiteSpace(account.Cookie) && !string.IsNullOrWhiteSpace(account.Username) && account.Username != "N/A")
                {
                    exportData.Add((account.Cookie, account.Username));
                }
                else
                {
                    Logger.LogWarning($"Skipping account ID {account?.UserId.ToString() ?? "Unknown"} from export due to missing cookie or username, or null account object.");
                }
            }

            if (exportData.Count == 0)
            {
                Logger.LogError("No accounts with both valid cookies and usernames found in the provided list to export.");
                return false;
            }

            if (sortByUsername)
            {
                exportData = [.. exportData.OrderBy(d => d.Username, StringComparer.OrdinalIgnoreCase)];
                Logger.LogInfo("(Usernames will be sorted alphabetically)");
            }
            else
            {
                Logger.LogInfo("(Usernames will be kept in their provided order)");
            }

            try
            {
                var sb = new StringBuilder();
                foreach (var (cookie, _) in exportData)
                {
                    sb.AppendLine(cookie);
                }

                sb.AppendLine();
                sb.AppendLine("--- Usernames ---");
                sb.AppendLine();

                foreach (var (_, username) in exportData)
                {
                    sb.AppendLine(username);
                }

                await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

                string sortStatus = sortByUsername ? "sorted" : "original order";
                Logger.LogSuccess($"Successfully exported {exportData.Count} cookies and {sortStatus} usernames to: {Markup.Escape(filePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"An unexpected error occurred during export: {ex.Message}");
                return false;
            }
        }
    }
}