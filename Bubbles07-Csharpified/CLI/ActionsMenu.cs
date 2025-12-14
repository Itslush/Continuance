using Continuance.Actions;
using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Continuance.Roblox.Services;
using Spectre.Console;
using System.Text;

namespace Continuance.CLI
{
    public class ActionsMenu(
        AccountManager accountManager,
        AccountSelector accountSelector,
        AccountActionExecutor actionExecutor,
        UserService userService,
        AvatarService avatarService,
        GameLauncher gameLauncher,
        BadgeService badgeService,
        FriendService friendService,
        WebDriverManager webDriverManager,
        AuthenticationService authService)
    {
        private readonly AccountManager _accountManager = accountManager;
        private readonly AccountSelector _accountSelector = accountSelector;
        private readonly AccountActionExecutor _actionExecutor = actionExecutor;
        private readonly UserService _userService = userService;
        private readonly AvatarService _avatarService = avatarService;
        private readonly GameLauncher _gameLauncher = gameLauncher;
        private readonly BadgeService _badgeService = badgeService;
        private readonly FriendService _friendService = friendService;
        private readonly WebDriverManager _webDriverManager = webDriverManager;
        private readonly AuthenticationService _authService = authService;

        private static async Task ExecuteCancellableAction(Func<CancellationToken, Task> actionToRun)
        {
            using var cts = new CancellationTokenSource();
            Logger.LogMuted($"(Press [{Theme.Current.Warning}]Q[/] to abort the action at the next step)");

            var keyListener = Task.Run(async () =>
            {
                if (Console.IsInputRedirected) return;
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q)
                        {
                            if (!cts.IsCancellationRequested) cts.Cancel();
                            return;
                        }
                        await Task.Delay(250, cts.Token);
                    }
                }
                catch { }
            }, cts.Token);

            try
            {
                await actionToRun(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.NewLine();
                Logger.LogError("Action cancelled by user.");
            }
            catch (Exception ex)
            {
                Logger.NewLine();
                Logger.LogError($"An unexpected error occurred during action execution: {Markup.Escape(ex.Message)}");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
            finally
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
                await Task.WhenAny(keyListener, Task.Delay(50));
            }
        }

        public async Task Show()
        {
            bool back = false;
            while (!back)
            {
                var selectedAccounts = _accountManager.GetSelectedAccounts();
                int totalSelectedCount = selectedAccounts.Count;
                int validSelectedCount = selectedAccounts.Count(a => a != null && a.IsValid);
                int invalidSelectedCount = totalSelectedCount - validSelectedCount;

                Console.Clear();

                string statusText;
                if (invalidSelectedCount > 0)
                {
                    statusText = $"[bold]Targeting:[/] [{Theme.Current.Accent1}]{totalSelectedCount} Selected Accounts[/] ([{Theme.Current.Success}]{validSelectedCount} Valid[/], [{Theme.Current.Error}]{invalidSelectedCount} Invalid[/])\n[{Theme.Current.Muted}](Invalid accounts will be skipped for most actions)[/]";
                }
                else
                {
                    statusText = $"[bold]Targeting:[/] [{Theme.Current.Success}]{validSelectedCount} valid accounts selected[/]";
                }

                var statusPanel = new Panel(statusText) { Border = BoxBorder.Rounded, Padding = new Padding(2, 1), Header = new PanelHeader($"[bold {Theme.Current.Info}]Actions Menu[/]") };
                AnsiConsole.Write(statusPanel);

                var menuText = new StringBuilder();
                menuText.AppendLine($"[underline {Theme.Current.Header}]Automated Actions[/]");
                menuText.AppendLine($"[bold]1.[/] Set Display Name [grey](from defaults)[/]");
                menuText.AppendLine($"[bold]2.[/] Set Avatar [grey](copy from user ID)[/]");
                menuText.AppendLine($"[bold]5.[/] Friend Accounts [grey](UNRELIABLE / UNSTABLE)[/]");
                menuText.AppendLine();
                menuText.AppendLine($"[underline {Theme.Current.Header}]Interactive Actions[/]");
                menuText.AppendLine($"[bold]3.[/] Join Group [grey](Interactive)[/]");
                menuText.AppendLine($"[bold]4.[/] Get Badges [grey](Launches Game)[/]");
                menuText.AppendLine($"[bold]6.[/] Open In Browser [grey](Interactive)[/]");
                menuText.AppendLine($"[bold]8.[/] Launch Game [grey](Single Instance)[/]");
                menuText.AppendLine($"[bold {Theme.Current.Accent1}]11.[/] Launch Multiple Instances [grey](Multi-Client)[/]");
                menuText.AppendLine($"[bold {Theme.Current.Accent1}]12.[/] Follow User In-Game");
                menuText.AppendLine();
                menuText.AppendLine($"[underline {Theme.Current.Header}]Utilities[/]");
                menuText.AppendLine($"[bold]7.[/] Verify Account Status");
                menuText.AppendLine($"[bold]10.[/] Refresh XCSRF Tokens");
                string headlessStatus = AppConfig.HeadlessMode ? $"[{Theme.Current.Success}]ON[/]" : $"[{Theme.Current.Error}]OFF[/]";
                menuText.AppendLine($"[bold]13.[/] Toggle Headless Mode (Current: {headlessStatus})");
                menuText.AppendLine();
                menuText.AppendLine($"[underline {Theme.Current.Header}]Sequences[/]");
                menuText.AppendLine($"[bold {Theme.Current.Warning}]9.[/] Execute All Auto [grey](1->2->5->4)[/]");

                var menuPanel = new Panel(menuText.ToString()) { Border = BoxBorder.None, Padding = new Padding(1, 1, 1, 0) };
                AnsiConsole.Write(menuPanel);
                Logger.NewLine();
                AnsiConsole.MarkupLine($"[bold {Theme.Current.Error}]0.[/] Back to Main Menu");

                string? choice = AnsiConsole.Prompt(new TextPrompt<string>($"\n[bold {Theme.Current.Prompt}]Choose action:[/] ").PromptStyle(Theme.Current.Prompt));

                if (totalSelectedCount == 0 && choice != "0")
                {
                    Logger.LogError("No accounts selected. Please select accounts first (Main Menu Option 5).");
                    await Task.Delay(1500);
                    continue;
                }

                switch (choice)
                {
                    case "1":
                        {
                            string nameToUse = GetStringInput($"Enter new name (or blank for default '{AppConfig.RuntimeDefaultDisplayName}'):", AppConfig.RuntimeDefaultDisplayName);
                            if (string.IsNullOrWhiteSpace(nameToUse) || nameToUse.Length < 3 || nameToUse.Length > 20)
                            {
                                Logger.LogError("Invalid name. Must be 3-20 characters.");
                                break;
                            }
                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new SetDisplayNameAction(_userService, nameToUse);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Set Display Name to '{Markup.Escape(nameToUse)}'", token);
                            });
                        }
                        break;
                    case "2":
                        {
                            long sourceIdToUse = GetLongInput($"Enter source User ID (or blank for default {AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy}):", AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy, 1);
                            if (sourceIdToUse <= 0) break;
                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new SetAvatarAction(_avatarService, sourceIdToUse);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Set Avatar from UserID {sourceIdToUse}", token);
                            });
                        }
                        break;
                    case "3":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("This action requires an interactive environment."); break; }
                            long groupIdToUse = GetLongInput($"Enter Group ID (or blank for default {AppConfig.RuntimeDefaultGroupId}):", AppConfig.RuntimeDefaultGroupId, 1);
                            if (groupIdToUse <= 0) break;
                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new JoinGroupInteractiveAction(_webDriverManager, groupIdToUse);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Join Group ID {groupIdToUse} (Interactive)", token, requireInteraction: true, requireValidToken: false);
                            });
                        }
                        break;
                    case "4":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("This action requires an interactive environment."); break; }
                            string gameIdToUse = GetStringInput($"Enter Game ID (or blank for default '{AppConfig.RuntimeDefaultBadgeGameId}'): ", AppConfig.RuntimeDefaultBadgeGameId);
                            int badgeGoalToUse = GetIntInput($"Enter target badge count (or blank for default {AppConfig.RuntimeDefaultBadgeGoal}): ", AppConfig.RuntimeDefaultBadgeGoal, 0);
                            if (badgeGoalToUse <= 0) { Logger.LogWarning("Badge goal is zero or negative, skipping."); break; }

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new GetBadgesAction(_gameLauncher, _badgeService, badgeGoalToUse, gameIdToUse);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Get Badges (Goal: {badgeGoalToUse}, Game: {gameIdToUse})", token, requireInteraction: true);
                            });
                        }
                        break;
                    case "5":
                        {
                            int friendGoalToUse = GetIntInput($"Enter target friend count (or blank for default {AppConfig.RuntimeDefaultFriendGoal}): ", AppConfig.RuntimeDefaultFriendGoal, 0);
                            if (friendGoalToUse < 0) break;

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new HandleFriendRequestsAction(_accountManager, _friendService, friendGoalToUse);
                                await action.ExecuteAsync(token);
                            });
                        }
                        break;
                    case "6":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("This action requires an interactive environment."); break; }
                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new OpenInBrowserAction(_webDriverManager);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, "Open in Browser", token, requireInteraction: true, requireValidToken: false);
                            });
                        }
                        break;
                    case "7":
                        {
                            var checks = AnsiConsole.Prompt(new MultiSelectionPrompt<string>().Title($"Which requirements do you want to [{Theme.Current.Success}]verify[/]?").PageSize(5)
                                .InstructionsText($"[{Theme.Current.Muted}](Press [{Theme.Current.Accent1}]<space>[/] to toggle, [{Theme.Current.Success}]<enter>[/] to accept)[/]")
                                .AddChoices(["Friend Count", "Badge Count", "Display Name", "Avatar"]).Required(false));

                            int friends = checks.Contains("Friend Count") ? AppConfig.RuntimeDefaultFriendGoal : 0;
                            int badges = checks.Contains("Badge Count") ? AppConfig.RuntimeDefaultBadgeGoal : 0;
                            string? name = checks.Contains("Display Name") ? AppConfig.RuntimeDefaultDisplayName : null;
                            long avatar = checks.Contains("Avatar") ? AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy : 0;

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new VerifyStatusAction(_accountManager, _friendService, _userService, _avatarService, _badgeService, friends, badges, name, avatar);
                                bool hadFailures = await action.ExecuteAsync(token);
                                if (hadFailures && AnsiConsole.Confirm($"\n[[?]] Verification found failures. Select failed accounts?"))
                                {
                                    _accountSelector.SelectFailedVerification();
                                }
                                else if (!hadFailures)
                                {
                                    Logger.NewLine();
                                    Logger.LogSuccess("Verification complete. No failures detected based on selected requirements.");
                                }
                            });
                        }
                        break;
                    case "8":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("This action requires an interactive environment."); break; }
                            string gameIdToUse = GetStringInput($"Enter Game ID (or blank for default '{AppConfig.RuntimeDefaultBadgeGameId}'): ", AppConfig.RuntimeDefaultBadgeGameId);
                            if (string.IsNullOrWhiteSpace(gameIdToUse)) break;

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new LaunchGameAction(_gameLauncher, gameIdToUse);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Launch Game (Game: {gameIdToUse})", token, requireInteraction: true);
                            });
                        }
                        break;
                    case "9":
                        {
                            await ExecuteCancellableAction(async (token) =>
                            {
                                AnsiConsole.Write(new Rule("Starting: Set Display Name"));
                                var setNameAction = new SetDisplayNameAction(_userService, AppConfig.RuntimeDefaultDisplayName);
                                await _actionExecutor.ExecuteOnSelectedAsync(setNameAction, "Set Display Name", token);
                                token.ThrowIfCancellationRequested();

                                AnsiConsole.Write(new Rule("Starting: Set Avatar"));
                                var setAvatarAction = new SetAvatarAction(_avatarService, AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy);
                                await _actionExecutor.ExecuteOnSelectedAsync(setAvatarAction, "Set Avatar", token);
                                token.ThrowIfCancellationRequested();

                                AnsiConsole.Write(new Rule("Starting: Limited Friend Actions"));
                                var friendAction = new HandleFriendRequestsAction(_accountManager, _friendService, AppConfig.RuntimeDefaultFriendGoal);
                                await friendAction.ExecuteAsync(token);
                                token.ThrowIfCancellationRequested();

                                AnsiConsole.Write(new Rule("Starting: Get Badges"));
                                if (!Environment.UserInteractive) Logger.LogWarning("Skipping 'Get Badges' in non-interactive mode.");
                                else
                                {
                                    var getBadgesAction = new GetBadgesAction(_gameLauncher, _badgeService, AppConfig.RuntimeDefaultBadgeGoal, AppConfig.RuntimeDefaultBadgeGameId);
                                    await _actionExecutor.ExecuteOnSelectedAsync(getBadgesAction, "Get Badges", token, requireInteraction: true);
                                }
                                Logger.NewLine();
                                Logger.LogHeader("Multi-Action Sequence 'Execute All Auto' Complete.");
                            });
                        }
                        break;
                    case "10":
                        {
                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new RefreshXcsrfAction(_authService);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, "Refresh XCSRF Tokens", token, requireInteraction: false, requireValidToken: false);
                            });
                        }
                        break;
                    case "11":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("This action requires an interactive environment."); break; }
                            string gameIdToUse = GetStringInput($"Enter Game ID (or blank for default '{AppConfig.RuntimeDefaultBadgeGameId}'): ", AppConfig.RuntimeDefaultBadgeGameId);
                            if (string.IsNullOrWhiteSpace(gameIdToUse) || !long.TryParse(gameIdToUse, out _))
                            {
                                Logger.LogError("Invalid Game ID provided. Must be a number.");
                                break;
                            }

                            bool renameWindows = AnsiConsole.Confirm($"   [[?]] Rename game windows with account name and instance number?\n   (Recommended: [{Theme.Current.Success}]Yes[/], disable if you suspect anti-cheat interference)", true);

                            bool tileWindows = false;
                            if (!AppConfig.HeadlessMode)
                            {
                                tileWindows = AnsiConsole.Confirm($"   [[?]] Auto-arrange windows in a grid after launching? (Windows will be minimized during launch)", true);
                            }

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var validAccounts = _accountManager.GetSelectedAccounts().Where(a => a.IsValid).ToList();
                                if (validAccounts.Count == 0)
                                {
                                    Logger.LogWarning("No valid accounts selected to launch.");
                                    return;
                                }

                                AnsiConsole.Write(new Rule($"[bold]Multi-Launching Game: {gameIdToUse} for {validAccounts.Count} account(s)[/]").LeftJustified());

                                int successCount = 0;
                                int failCount = 0;
                                var failedRetries = new List<(Account Account, int Index)>();
                                var launchedProcessIds = new List<int>();

                                for (int i = 0; i < validAccounts.Count; i++)
                                {
                                    token.ThrowIfCancellationRequested();
                                    var acc = validAccounts[i];

                                    Logger.NewLine();
                                    Logger.LogDefault($"[[[bold {Theme.Current.Accent1}]{i + 1}/{validAccounts.Count}[/]]] Launching game for: [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] (ID: {acc.UserId})...");

                                    int? pid = await GameLauncher.LaunchClientWithRenameAsync(acc, gameIdToUse, i, renameWindows, tileWindows);
                                    if (pid != null)
                                    {
                                        successCount++;
                                        launchedProcessIds.Add(pid.Value);
                                    }
                                    else
                                    {
                                        failCount++;
                                        failedRetries.Add((acc, i));
                                    }

                                    if (i < validAccounts.Count - 1)
                                    {
                                        int delay = AppConfig.CurrentApiDelayMs;
                                        Logger.LogMuted($"Waiting {delay}ms before launching next account...");
                                        await Task.Delay(delay, token);
                                    }
                                }

                                if (failedRetries.Count > 0)
                                {
                                    Logger.NewLine();
                                    AnsiConsole.Write(new Rule($"[bold {Theme.Current.Warning}]Retrying {failedRetries.Count} Failed Launch(es)[/]").LeftJustified());
                                    Logger.LogInfo("Retrying launch for accounts that failed due to network errors...");

                                    foreach (var (acc, idx) in failedRetries)
                                    {
                                        token.ThrowIfCancellationRequested();

                                        if (!acc.IsValid)
                                        {
                                            Logger.LogWarning($"Skipping retry for {acc.Username}: Account marked invalid during process.");
                                            continue;
                                        }

                                        Logger.NewLine();
                                        Logger.LogDefault($"[[Retry]] Launching game for: [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] (Instance {idx + 1})...");

                                        await Task.Delay(2500, token);

                                        int? retryPid = await GameLauncher.LaunchClientWithRenameAsync(acc, gameIdToUse, idx, renameWindows, tileWindows);

                                        if (retryPid != null)
                                        {
                                            Logger.LogSuccess("Retry successful.");
                                            successCount++;
                                            failCount--;
                                            launchedProcessIds.Add(retryPid.Value);
                                        }
                                        else
                                        {
                                            Logger.LogError($"Retry failed for {acc.Username}.");
                                        }
                                    }
                                }

                                Logger.NewLine();
                                Logger.LogHeader("Multi-Launch Sequence Complete.");
                                Logger.LogInfo($"Successfully launched: [{Theme.Current.Success}]{successCount}[/], Failed: [{Theme.Current.Error}]{failCount}[/].");

                                if (tileWindows && launchedProcessIds.Count > 0)
                                {
                                    Logger.LogInfo("Arranging windows in grid layout...");
                                    WindowManager.TileWindows(launchedProcessIds);
                                    Logger.LogSuccess("Windows arranged.");
                                }
                                else if (renameWindows && !AppConfig.HeadlessMode)
                                {
                                    Logger.LogMuted("It may take some time for all game windows to appear and be renamed.");
                                }
                            });
                        }
                        break;
                    case "12":
                        {
                            if (!Environment.UserInteractive) { Logger.LogError("Interactive environment required."); break; }
                            long targetId = GetLongInput($"Enter Target User ID to follow:", 0, 1);
                            if (targetId <= 0) break;

                            await ExecuteCancellableAction(async (token) =>
                            {
                                var action = new FollowUserAction(_gameLauncher, targetId);
                                await _actionExecutor.ExecuteOnSelectedAsync(action, $"Follow User {targetId}", token, requireInteraction: true);
                            });
                        }
                        break;
                    case "13":
                        {
                            AppConfig.HeadlessMode = !AppConfig.HeadlessMode;
                            Logger.LogInfo($"Headless Mode set to: {(AppConfig.HeadlessMode ? "ON (Windows will be hidden)" : "OFF (Windows visible)")}");
                            await Task.Delay(1000);
                        }
                        break;
                    case "0": back = true; break;
                    default: Logger.LogError("Invalid choice."); break;
                }

                if (!back)
                {
                    AnsiConsole.Prompt(new TextPrompt<string>($"\n[{Theme.Current.Muted}]Action complete. Press Enter to return to Actions Menu...[/]").AllowEmpty());
                }
            }
            Console.Clear();
        }

        private static int GetIntInput(string prompt, int defaultValue, int? minValue = null, int? maxValue = null)
        {
            var textPrompt = new TextPrompt<int>(prompt)
                .DefaultValue(defaultValue)
                .PromptStyle(Theme.Current.Prompt)
                .ValidationErrorMessage($"[{Theme.Current.Error}]Invalid integer input.[/]");

            if (minValue.HasValue) textPrompt.Validate(i => i >= minValue.Value, $"[{Theme.Current.Error}]Input must be at least {minValue.Value}.[/]");
            if (maxValue.HasValue) textPrompt.Validate(i => i <= maxValue.Value, $"[{Theme.Current.Error}]Input must be at most {maxValue.Value}.[/]");

            return AnsiConsole.Prompt(textPrompt);
        }

        private static long GetLongInput(string prompt, long defaultValue, long? minValue = null, long? maxValue = null)
        {
            var textPrompt = new TextPrompt<long>(prompt)
               .DefaultValue(defaultValue)
               .PromptStyle(Theme.Current.Prompt)
               .ValidationErrorMessage($"[{Theme.Current.Error}]Invalid integer input.[/]");

            textPrompt.Validate(value =>
            {
                if (minValue.HasValue && value < minValue.Value) return ValidationResult.Error($"[{Theme.Current.Error}]Input must be at least {minValue.Value}.[/]");
                if (maxValue.HasValue && value > maxValue.Value) return ValidationResult.Error($"[{Theme.Current.Error}]Input must be at most {maxValue.Value}.[/]");
                return ValidationResult.Success();
            });

            return AnsiConsole.Prompt(textPrompt);
        }

        private static string GetStringInput(string prompt, string defaultValue)
        {
            return AnsiConsole.Prompt(new TextPrompt<string>(prompt).DefaultValue(defaultValue).PromptStyle(Theme.Current.Prompt));
        }

        public static void AdjustRateLimitsUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule($"[bold]Adjust Rate Limits, Timeout & Retries[/]").LeftJustified());
            Logger.LogWarning("Setting delays too low increases risk of rate limiting or account flags.");
            Logger.LogMuted($"Min API/Friend Delay: {AppConfig.MinAllowedDelayMs}ms | Min Retry Delay: {AppConfig.MinRetryDelayMs}ms");
            Logger.LogMuted("Changes apply until restart OR saved via Main Menu.\n");

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]1. General API Delay (Between accounts/steps)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentApiDelayMs}ms[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultApiDelayMs}ms[/]");
            AppConfig.CurrentApiDelayMs = GetIntInput($"[[?]] New delay (ms, >= {AppConfig.MinAllowedDelayMs}) or blank: ", AppConfig.CurrentApiDelayMs, AppConfig.MinAllowedDelayMs);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]2. Friend Action Delay (Send/Accept)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentFriendActionDelayMs}ms[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultFriendActionDelayMs}ms[/]");
            AppConfig.CurrentFriendActionDelayMs = GetIntInput($"[[?]] New delay (ms, >= {AppConfig.MinAllowedDelayMs}) or blank: ", AppConfig.CurrentFriendActionDelayMs, AppConfig.MinAllowedDelayMs);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]3. Import Validation Delay (Between cookies)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentImportDelayMs}ms[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultImportDelayMs}ms[/]");
            AppConfig.CurrentImportDelayMs = GetIntInput($"[[?]] New delay (ms, >= 100) or blank: ", AppConfig.CurrentImportDelayMs, 100);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]4. Request Timeout (Max wait for API response)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.DefaultRequestTimeoutSec}s[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultRequestTimeoutSec}s[/]");
            AppConfig.DefaultRequestTimeoutSec = GetIntInput($"[[?]] New timeout (seconds, 5-120) or blank: ", AppConfig.DefaultRequestTimeoutSec, 5, 120);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]5. Max Action Retries (Attempts after initial failure)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentMaxApiRetries}[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultMaxApiRetries}[/]");
            AppConfig.CurrentMaxApiRetries = GetIntInput($"[[?]] New max retries (0+) or blank: ", AppConfig.CurrentMaxApiRetries, 0);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]6. Action Retry Delay (Wait between retries)[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentApiRetryDelayMs}ms[/] / Default: [{Theme.Current.Muted}]{AppConfig.DefaultApiRetryDelayMs}ms[/]");
            AppConfig.CurrentApiRetryDelayMs = GetIntInput($"[[?]] New retry delay (ms, >= {AppConfig.MinRetryDelayMs}) or blank: ", AppConfig.CurrentApiRetryDelayMs, AppConfig.MinRetryDelayMs);

            AnsiConsole.Write(new Rule($"[bold {Theme.Current.Info}]7. Action Confirmation Threshold[/]").LeftJustified());
            Logger.LogDefault($"   Current: [{Theme.Current.Warning}]{AppConfig.CurrentActionConfirmationThreshold}[/] / Default: [{Theme.Current.Muted}]15[/]");
            AppConfig.CurrentActionConfirmationThreshold = GetIntInput($"[[?]] New threshold (0 = always ask, high number = never ask) or blank: ", AppConfig.CurrentActionConfirmationThreshold, 0);

            Logger.NewLine();
            Logger.LogSuccess("Settings updated for this session.");
        }
    }
}