using System.Diagnostics;
using System.Web;
using System.Text;
using System.ComponentModel;
using Continuance.Models;
using Continuance.Roblox.Services;
using Continuance.CLI;
using Spectre.Console;

namespace Continuance.Roblox.Automation
{
    public class GameLauncher(AuthenticationService authService, BadgeService badgeService)
    {
        private readonly AuthenticationService _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        private readonly BadgeService _badgeService = badgeService ?? throw new ArgumentNullException(nameof(badgeService));
        private static Mutex? singletonMutex;

        private static void EnsureMultiInstanceMutex()
        {
            try
            {
                singletonMutex ??= new Mutex(true, "ROBLOX_singletonEvent");
            }
            catch { }
        }

        private static string? FindRobloxPlayerBeta()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                string[] potentialBasePaths = [
                    Path.Combine(localAppData, "Fishstrap", "Versions"),
                    Path.Combine(localAppData, "Bloxstrap", "Versions"),
                    Path.Combine(localAppData, "Roblox"   , "Versions")
                ];

                foreach (var basePath in potentialBasePaths)
                {
                    if (!Directory.Exists(basePath)) continue;

                    var latestVersionDir = new DirectoryInfo(basePath)
                        .GetDirectories()
                        .Where(d => File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
                        .OrderByDescending(d => d.LastWriteTime)
                        .FirstOrDefault();

                    if (latestVersionDir != null)
                    {
                        string fullPath = Path.Combine(latestVersionDir.FullName, "RobloxPlayerBeta.exe");
                        string sourceName = new DirectoryInfo(basePath).Parent?.Name ?? "Roblox";
                        Logger.LogInfo($"Found Roblox Player ({sourceName}): {Markup.Escape(fullPath)}");
                        return fullPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error finding RobloxPlayerBeta.exe: {Markup.Escape(ex.Message)}");
            }

            Logger.LogError("Could not find a valid RobloxPlayerBeta.exe in Bloxstrap, Fishstrap, or standard locations.");
            return null;
        }

        public static async Task<bool> LaunchGameAsync(Account account, string gameId)
        {
            EnsureMultiInstanceMutex();

            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Logger.LogError($"Cannot Launch Game for {account.Username}: Missing Cookie.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Logger.LogWarning($"Skipping game launch for {account.Username}: No Game ID provided.");
                return false;
            }

            Logger.LogInfo($"Refreshing XCSRF for {account.Username} before getting auth ticket...");
            bool tokenRefreshed = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(account);
            if (!tokenRefreshed || string.IsNullOrEmpty(account.XcsrfToken))
            {
                Logger.LogError($"Failed to refresh XCSRF token for {account.Username}. Cannot proceed with game launch.");
                return false;
            }

            if (!Environment.UserInteractive)
            {
                Logger.LogWarning("Skipping Launch Game action in non-interactive environment.");
                return false;
            }

            string? authTicket = await AuthenticationService.GetAuthenticationTicketAsync(account, gameId);

            if (string.IsNullOrEmpty(authTicket))
            {
                Logger.LogError("Auth ticket is missing or invalid. Cannot launch game.");
                return false;
            }

            long browserTrackerId = Random.Shared.NextInt64(10_000_000_000L, 100_000_000_000L);
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={gameId}&isPlayTogetherGame=false&joinAttemptId={Guid.NewGuid()}&joinAttemptOrigin=PlayButton";
            string encodedPlaceLauncherUrl = HttpUtility.UrlEncode(placeLauncherUrl);

            var launchUrlBuilder = new StringBuilder("roblox-player:1");

            launchUrlBuilder.Append("+launchmode:play");
            launchUrlBuilder.Append("+gameinfo:").Append(authTicket);
            launchUrlBuilder.Append("+launchtime:").Append(launchTime);
            launchUrlBuilder.Append("+placelauncherurl:").Append(encodedPlaceLauncherUrl);
            launchUrlBuilder.Append("+browsertrackerid:").Append(browserTrackerId);
            launchUrlBuilder.Append("+robloxLocale:en_us");
            launchUrlBuilder.Append("+gameLocale:en_us");

            string launchUrl = launchUrlBuilder.ToString();

            try
            {
                Logger.LogInfo("Dispatching launch command for Roblox Player...");
                Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
                Logger.LogSuccess($"Launch command sent. The Roblox Player should start shortly for {account.Username}.");
                await Task.Delay(1000);
                return true;
            }
            catch (Win32Exception ex)
            {
                Logger.LogError("Failed to launch Roblox Player. Is Roblox installed and the protocol handler registered?");
                Logger.LogError($"Error: {ex.Message} (Code: {ex.NativeErrorCode})");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"An unexpected error occurred launching Roblox Player for {account.Username}.");
                Logger.LogError($"Error: {ex.Message}");
                return false;
            }
        }

        public static async Task<int?> LaunchClientWithRenameAsync(Account account, string gameId, int instanceNumber, bool renameWindow, bool minimizeForTiling)
        {
            EnsureMultiInstanceMutex();
            Logger.LogDefault($"   Starting multi-launch sequence for [{Theme.Current.Info}]{Markup.Escape(account.Username)}[/]...");
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Logger.LogError("      FAIL: Account has no cookie.");
                return null;
            }
            if (!long.TryParse(gameId, out long placeId) || placeId <= 0)
            {
                Logger.LogError($"      FAIL: Invalid Game ID '{Markup.Escape(gameId)}'.");
                return null;
            }

            Logger.LogDefault("      Requesting authentication ticket...");
            bool tokenRefreshed = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(account);
            if (!tokenRefreshed || string.IsNullOrEmpty(account.XcsrfToken))
            {
                Logger.LogError("      FAIL: Could not refresh XCSRF token before requesting auth ticket.");
                return null;
            }
            string? authTicket = await AuthenticationService.GetAuthenticationTicketAsync(account, gameId);
            if (string.IsNullOrEmpty(authTicket))
            {
                Logger.LogError($"      FAIL: Authentication ticket was missing or invalid for {Markup.Escape(account.Username)}.");
                return null;
            }
            Logger.LogSuccess("      Authentication ticket received.");

            Logger.LogDefault("      Locating Roblox executable...");
            string? robloxExePath = FindRobloxPlayerBeta();
            if (string.IsNullOrEmpty(robloxExePath)) return null;

            string? workingDirectory = Path.GetDirectoryName(robloxExePath);
            if (string.IsNullOrEmpty(workingDirectory))
            {
                Logger.LogError($"      FAIL: Could not determine working directory from executable path.");
                return null;
            }

            Logger.LogDefault("      Constructing launch arguments...");
            long browserTrackerId = Random.Shared.NextInt64(10_000_000_000L, 100_000_000_000L);
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string joinAttemptId = Guid.NewGuid().ToString();

            string placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={gameId}&isPlayTogetherGame=false&joinAttemptId={joinAttemptId}&joinAttemptOrigin=PlayButton";

            var argumentBuilder = new StringBuilder();
            argumentBuilder.Append("--app ");
            argumentBuilder.Append($"-t {authTicket} ");
            argumentBuilder.Append($"-j \"{placeLauncherUrl}\" ");
            argumentBuilder.Append($"--launchtime={launchTime} ");
            argumentBuilder.Append($"--browsertrackerid={browserTrackerId} ");
            argumentBuilder.Append("--rloc en_us ");
            argumentBuilder.Append("--gloc en_us");

            string arguments = argumentBuilder.ToString();

            Logger.LogSuccess("      Launch arguments constructed.");

            try
            {
                Logger.LogDefault("      Starting process...");
                var processStartInfo = new ProcessStartInfo(robloxExePath, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                Process? robloxProcess = Process.Start(processStartInfo);

                if (robloxProcess != null)
                {
                    Logger.LogSuccess($"      Process started successfully (PID: {robloxProcess.Id}).");
                    Logger.LogDefault($"      Waiting for window to initialize...");

                    IntPtr windowHandle = await WindowManager.WaitForWindowHandleAsync(robloxProcess.Id);

                    if (windowHandle != IntPtr.Zero)
                    {
                        if (AppConfig.HeadlessMode)
                        {
                            WindowManager.ApplyHeadlessToHandle(windowHandle);
                            Logger.LogSuccess("      [Headless] Window hidden.");
                        }
                        else
                        {
                            if (renameWindow)
                            {
                                string newTitle = $"[{account.UserId}] :: [{account.Username}] -- [#{instanceNumber + 1}]";
                                _ = WindowManager.MonitorAndRenameWindowAsync(robloxProcess.Id, newTitle);
                            }

                            if (minimizeForTiling)
                            {
                                WindowManager.MinimizeWindow(windowHandle);
                                Logger.LogSuccess("      Window minimized (queued for tiling).");
                            }
                            else
                            {
                                Logger.LogSuccess("      Window confirmed visible.");
                            }
                        }
                    }
                    else
                    {
                        Logger.LogWarning("      Process started, but window did not appear within timeout.");
                    }

                    return robloxProcess.Id;
                }
                else
                {
                    Logger.LogError($"      FAIL: Process.Start returned null.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"      FAIL: An unexpected error occurred during process launch.");
                Logger.LogError($"      Details: {Markup.Escape(ex.Message)}");
                return null;
            }
        }

        public static async Task<bool> LaunchFollowUserAsync(Account account, long targetUserId)
        {
            EnsureMultiInstanceMutex();

            if (string.IsNullOrWhiteSpace(account.Cookie)) return false;

            await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(account);
            string? authTicket = await AuthenticationService.GetAuthenticationTicketAsync(account, AppConfig.DefaultBadgeGameId);

            if (string.IsNullOrEmpty(authTicket)) return false;

            long browserTrackerId = Random.Shared.NextInt64(10_000_000_000L, 100_000_000_000L);
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&browserTrackerId={browserTrackerId}&userId={targetUserId}&isPlayTogetherGame=false&joinAttemptId={Guid.NewGuid()}&joinAttemptOrigin=Profile";
            string encodedPlaceLauncherUrl = HttpUtility.UrlEncode(placeLauncherUrl);

            var launchUrlBuilder = new StringBuilder("roblox-player:1");
            launchUrlBuilder.Append("+launchmode:play");
            launchUrlBuilder.Append("+gameinfo:").Append(authTicket);
            launchUrlBuilder.Append("+launchtime:").Append(launchTime);
            launchUrlBuilder.Append("+placelauncherurl:").Append(encodedPlaceLauncherUrl);
            launchUrlBuilder.Append("+browsertrackerid:").Append(browserTrackerId);
            launchUrlBuilder.Append("+robloxLocale:en_us");
            launchUrlBuilder.Append("+gameLocale:en_us");

            try
            {
                Process.Start(new ProcessStartInfo(launchUrlBuilder.ToString()) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        public static async Task<bool> LaunchGameForBadgesAsync(Account account, string gameId, int badgeGoal)
        {
            EnsureMultiInstanceMutex();
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Logger.LogError($"Cannot GetBadges for {account.Username}: Missing Cookie.");
                return false;
            }
            if (badgeGoal <= 0)
            {
                Logger.LogInfo($"Skipping game launch for {account.Username}: Badge goal is zero or negative.");
                return true;
            }
            if (string.IsNullOrWhiteSpace(gameId))
            {
                Logger.LogWarning($"Skipping game launch for {account.Username}: No Game ID provided.");
                return false;
            }

            bool launchSuccess = await LaunchGameAsync(account, gameId);

            if (launchSuccess)
            {
                Logger.LogWarning($"Please complete any actions required in the game to earn badges (aiming for {badgeGoal}).");
                await BadgeService.MonitorBadgeAcquisitionAsync(account, badgeGoal);
                await TerminateRobloxProcessesAsync(account);
                Logger.LogInfo($"GetBadges action sequence finished monitoring/termination for {account.Username}.");
            }

            return launchSuccess;
        }

        private static async Task TerminateRobloxProcessesAsync(Account account)
        {
            if (!Environment.UserInteractive)
            {
                Logger.LogInfo("Skipping automatic termination of Roblox processes in non-interactive mode.");
                return;
            }

            Logger.LogInfo("Attempting automatic termination of Roblox Player instances...");
            int closedCount = 0;

            try
            {
                string[] processNames = ["RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxPlayer"];
                List<Process> robloxProcesses = [];

                foreach (var name in processNames)
                {
                    try { robloxProcesses.AddRange(Process.GetProcessesByName(name)); }
                    catch { }
                }

                robloxProcesses = [.. robloxProcesses.Where(p =>
                {
                    try { return !p.HasExited; }
                    catch { return false; }
                })];

                if (robloxProcesses.Count == 0) { Logger.LogDefault("No active Roblox Player processes found to terminate."); }

                else
                {
                    Logger.LogAccent($"Found {robloxProcesses.Count} potential Roblox process(es). Attempting to close...");
                    foreach (var process in robloxProcesses)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                Logger.LogDefault($"   Killing {process.ProcessName} (PID: {process.Id})...");
                                process.Kill(entireProcessTree: true);

                                if (await Task.Run(() => process.WaitForExit(2000)))
                                {
                                    Logger.LogSuccess("      Terminated.");
                                    closedCount++;
                                }
                                else
                                {
                                    try
                                    {
                                        if (process.HasExited) { Logger.LogSuccess("      Terminated (late)."); closedCount++; }
                                        else { Logger.LogWarning("      Still running?"); }
                                    }
                                    catch { Logger.LogWarning("      Status Unknown."); }
                                }
                            }
                        }
                        catch (Exception ex) { Logger.LogError($"      Error interacting with process: {ex.Message}"); }

                        finally
                        {
                            process.Dispose();
                        }
                    }
                    Logger.LogInfo($"Attempted termination for {closedCount} process(es).");
                }
            }
            catch (Exception ex) { Logger.LogError($"Error finding/killing Roblox processes: {ex.Message}"); }
        }
    }
}