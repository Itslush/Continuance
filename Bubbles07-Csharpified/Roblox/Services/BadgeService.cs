using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Continuance.Models;
using Continuance.Roblox.Http;
using Continuance.CLI;

namespace Continuance.Roblox.Services
{
    public class BadgeService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        private static readonly int[] sourceArray = [10, 25, 50, 100];

        public static async Task<int> GetBadgeCountAsync(Account account, int limit = 10)
        {
            if (account == null)
            {
                Logger.LogError("Cannot GetBadgeCount: Account object is null.");
                return -1;
            }
            if (account.UserId <= 0)
            {
                Logger.LogError($"Cannot GetBadgeCount: Invalid User ID ({account.UserId}) in Account object.");
                return -1;
            }

            if (!sourceArray.Contains(limit))
            {
                Logger.LogError($"[BadgeService] Invalid limit '{limit}' passed to GetBadgeCountAsync. Defaulting to 10.");
                limit = 10;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Badges}/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (statusCode, success, content) = await RobloxHttpClient.SendRequest(
                HttpMethod.Get,
                url,
                account,
                null,
                $"Get Badge Count (API Limit: {limit})",
                allowRetryOnXcsrf: false
            );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    if (json["data"] is JArray dataArray)
                    {
                        return dataArray.Count;
                    }
                    else
                    {
                        Logger.LogError($"Could not parse badge count (missing or invalid 'data' array) from response for {account.Username}: {ConsoleUI.Truncate(content)}");
                    }
                }
                catch (JsonReaderException jex)
                {
                    Logger.LogError($"Error parsing badge count JSON for {account.Username}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error processing badge count response for {account.Username}: {ex.Message}");
                }
            }
            else if (success)
            {
                Logger.LogWarning($"Get Badge Count request succeeded but returned empty content for {account.Username}.");
            }

            return -1;
        }

        public static async Task MonitorBadgeAcquisitionAsync(Account account, int badgeGoal)
        {
            if (account == null)
            {
                Logger.LogError("Cannot MonitorBadgeAcquisition: Account object is null.");
                return;
            }
            if (badgeGoal <= 0)
            {
                Logger.LogInfo($"Skipping badge monitoring for {account.Username}: Badge goal is zero or negative.");
                return;
            }
            if (!Environment.UserInteractive)
            {
                Logger.LogWarning("Skipping badge monitoring in non-interactive environment.");
                return;
            }


            int checkCount = 0;
            int initialBadgeCount = -1;
            int apiLimitForMonitoring;

            const int maxChecks = 4;
            const int checkIntervalSeconds = 6;

            if (badgeGoal <= 0) apiLimitForMonitoring = 10;
            else if (badgeGoal <= 10) apiLimitForMonitoring = 10;
            else if (badgeGoal <= 25) apiLimitForMonitoring = 25;
            else if (badgeGoal <= 50) apiLimitForMonitoring = 50;
            else apiLimitForMonitoring = 100;

            Logger.LogInfo($"Monitoring badge acquisition for {account.Username} (Goal: {badgeGoal})...");
            Logger.LogMuted($"Checking every {checkIntervalSeconds}s up to {maxChecks} times (~{maxChecks * checkIntervalSeconds}s total).");
            Logger.LogMuted("Press 'Q' in console to stop monitoring early.");

            Logger.LogAccent("Performing initial badge check...");
            initialBadgeCount = await GetBadgeCountAsync(account, limit: apiLimitForMonitoring);

            if (initialBadgeCount != -1)
            {
                Logger.LogInfo($"Initial recent badges found: {initialBadgeCount} (checked up to {apiLimitForMonitoring})");
                if (initialBadgeCount >= badgeGoal)
                {
                    Logger.LogSuccess($"Goal already met or exceeded ({initialBadgeCount} >= {badgeGoal}). Monitoring finished early.");
                    return;
                }
            }
            else
            {
                Logger.LogWarning("Failed to get initial count. Monitoring will continue.");
            }

            bool stopWaitingFlag = false;
            using CancellationTokenSource cts = new();

            Task keyListener = Task.Run(async () =>
            {
                if (Console.IsInputRedirected)
                {
                    try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) { }
                    return;
                }
                ;

                try
                {
                    while (!cts.IsCancellationRequested && !Console.KeyAvailable)
                    {
                        await Task.Delay(250, cts.Token);
                    }
                    if (!cts.IsCancellationRequested)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            stopWaitingFlag = true;
                            Logger.NewLine();
                            Logger.LogWarning("User pressed 'Q'. Aborting monitor.");
                            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { }
                        }
                    }
                }
                catch (InvalidOperationException) { }
                catch (TaskCanceledException) { }
                catch (Exception ex) { Logger.LogError($"\nError in key listener: {ex.Message}"); }
                finally
                {
                    try { if (!cts.IsCancellationRequested) cts.Cancel(); }
                    catch (ObjectDisposedException) { }
                    catch (Exception) { }
                }

            }, cts.Token);

            while (checkCount < maxChecks && !stopWaitingFlag && !cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (stopWaitingFlag || cts.IsCancellationRequested) break;

                checkCount++;

                try
                {
                    Logger.LogAccent($"Badge Check ({checkCount}/{maxChecks}) for {account.Username}...");
                    int currentBadgeCount = await GetBadgeCountAsync(account, limit: apiLimitForMonitoring);

                    if (currentBadgeCount != -1)
                    {
                        Logger.LogInfo($"Recent Badges: {currentBadgeCount} (checked up to {apiLimitForMonitoring})");
                        if (initialBadgeCount != -1 && currentBadgeCount > initialBadgeCount)
                        {
                            Logger.LogSuccess($"Change detected! ({initialBadgeCount} -> {currentBadgeCount}).");
                            initialBadgeCount = currentBadgeCount;
                        }
                        else if (initialBadgeCount != -1 && currentBadgeCount < initialBadgeCount)
                        {
                            Logger.LogWarning($"Count decreased? ({initialBadgeCount} -> {currentBadgeCount}). API might show badges differently over time.");
                            initialBadgeCount = currentBadgeCount;
                        }
                        if (currentBadgeCount >= badgeGoal)
                        {
                            Logger.LogSuccess($"Badge goal ({badgeGoal}) met or exceeded ({currentBadgeCount}). Stopping monitor.");
                            stopWaitingFlag = true;
                            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { }
                        }
                    }
                    else
                    {
                        Logger.LogError("Check Failed (API error retrieving count).");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.LogError($"Unexpected Check Error: {ex.GetType().Name} - {ConsoleUI.Truncate(ex.Message)}");
                }
            }

            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { } catch (Exception) { }

            await Task.WhenAny(keyListener, Task.Delay(100));

            try { cts.Dispose(); } catch { }

            if (checkCount >= maxChecks) { Logger.LogWarning($"Max badge checks ({maxChecks}) reached for {account.Username}. Monitoring finished."); }

            await Task.Delay(500);

            Logger.LogAccent("Performing final badge count check...");

            int finalCount = await GetBadgeCountAsync(account, limit: apiLimitForMonitoring);

            if (finalCount != -1) Logger.LogInfo($"Final recent badge count: {finalCount} (Goal was {badgeGoal}, checked up to {apiLimitForMonitoring})");
            else Logger.LogError("Final check failed.");
        }
    }
}