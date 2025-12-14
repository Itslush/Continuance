using Continuance.CLI;
using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;
using System.Diagnostics;

namespace Continuance.Actions
{
    public class VerifyStatusAction(AccountManager accountManager, FriendService friendService, UserService userService, AvatarService avatarService, BadgeService badgeService, int requiredFriends, int requiredBadges, string? expectedDisplayName, long expectedAvatarSourceId)
    {
        private static AvatarDetails? _targetAvatarDetailsCache;
        private static long _targetAvatarCacheSourceId = -1;
        private static readonly object _avatarCacheLock = new();
        private static async Task<AvatarDetails?> GetOrFetchTargetAvatarDetailsAsync(long sourceUserId, CancellationToken cancellationToken)
        {
            lock (_avatarCacheLock)
            {
                if (_targetAvatarDetailsCache != null && _targetAvatarCacheSourceId == sourceUserId)
                {
                    return _targetAvatarDetailsCache;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogDefault($"   [[*]] [{Theme.Current.Info}]Fetching target avatar details from User ID [{Theme.Current.Warning}]{sourceUserId}[/] for comparison/cache...[/]");
            var fetchedDetails = await AvatarService.FetchAvatarDetailsAsync(sourceUserId);
            if (fetchedDetails != null)
            {
                lock (_avatarCacheLock)
                {
                    _targetAvatarDetailsCache = fetchedDetails;
                    _targetAvatarCacheSourceId = sourceUserId;
                    Logger.LogDefault($"   [[+]] [{Theme.Current.Success}]Target avatar details cached successfully.[/]");
                }
            }
            else
            {
                Logger.LogError("Failed to fetch/cache target avatar details.");
            }
            return fetchedDetails;
        }

        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            accountManager.ClearVerificationResults();
            SessionData.LastVerificationFailedAccounts.Clear();

            var selectedAccountsRaw = accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccountsRaw.Where(acc => acc != null && acc.IsValid).ToList();

            int totalSelected = selectedAccountsRaw.Count;
            int validCount = accountsToProcess.Count;
            int skippedInvalidCount = totalSelected - validCount;

            AnsiConsole.Write(new Rule($"[bold]Verifying Account Status for {validCount} account(s)[/]") { Justification = Justify.Left });
            var requirements = new List<string>();
            if (requiredFriends > 0) requirements.Add($"Friends >= [{Theme.Current.Warning}]{requiredFriends}[/]");
            if (requiredBadges > 0) requirements.Add($"Badges >= [{Theme.Current.Warning}]{requiredBadges}[/]");
            if (!string.IsNullOrEmpty(expectedDisplayName)) requirements.Add($"Name == '[{Theme.Current.Warning}]{Markup.Escape(expectedDisplayName)}[/]'");
            if (expectedAvatarSourceId > 0) requirements.Add($"Avatar Source == [{Theme.Current.Warning}]{expectedAvatarSourceId}[/]");

            if (requirements.Count > 0)
                Logger.LogDefault($"   [bold]Requirements:[/] {string.Join(", ", requirements)}");
            else
                Logger.LogDefault($"   [bold]Requirements:[/] [{Theme.Current.Warning}]No checks selected.[/]");

            if (skippedInvalidCount > 0) Logger.LogWarning($"({skippedInvalidCount} selected accounts were invalid and will be skipped)");
            if (validCount == 0) { Logger.LogWarning("No valid accounts selected for verification."); return false; }

            AvatarDetails? targetAvatarDetails = null;
            if (expectedAvatarSourceId > 0)
            {
                targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(expectedAvatarSourceId, cancellationToken);
                if (targetAvatarDetails == null)
                {
                    Logger.LogWarning("Could not fetch target avatar details. Avatar check will be skipped for all accounts.");
                }
            }

            int passedCount = 0, failedReqCount = 0, failedErrCount = 0;
            var stopwatch = Stopwatch.StartNew();
            int checkDelay = AppConfig.CurrentApiDelayMs / 4;

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Account acc = accountsToProcess[i];

                var resultsGrid = new Grid().AddColumn().AddColumn(new GridColumn().RightAligned());
                resultsGrid.AddRow($"Verifying: [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] (ID: {acc.UserId})", $"[[[bold {Theme.Current.Accent1}]{i + 1}/{validCount}[/]]]");
                AnsiConsole.Write(new Panel(resultsGrid) { Border = BoxBorder.Rounded, Padding = new Padding(1, 0, 1, 0) });

                int friendCount = -1, badgeCount = -1;
                string? currentDisplayName = null;
                bool? avatarMatches = null;
                bool errorOccurred = false;
                var failureReasons = new List<string>();

                try
                {
                    if (requiredFriends > 0)
                    {
                        friendCount = await FriendService.GetFriendCountAsync(acc);
                        if (friendCount == -1) { errorOccurred = true; failureReasons.Add("Friend check API failed"); }
                        else if (friendCount < requiredFriends) { failureReasons.Add($"Friends {friendCount} < {requiredFriends}"); }
                        await Task.Delay(checkDelay, cancellationToken);
                    }

                    if (requiredBadges > 0 && !errorOccurred)
                    {
                        int apiLimitForBadges = requiredBadges <= 10 ? 10 : requiredBadges <= 25 ? 25 : requiredBadges <= 50 ? 50 : 100;
                        badgeCount = await BadgeService.GetBadgeCountAsync(acc, limit: apiLimitForBadges);
                        if (badgeCount == -1) { errorOccurred = true; failureReasons.Add("Badge check API failed"); }
                        else if (badgeCount < requiredBadges) { failureReasons.Add($"Badges {badgeCount} < {requiredBadges}"); }
                        await Task.Delay(checkDelay, cancellationToken);
                    }

                    if (!string.IsNullOrEmpty(expectedDisplayName) && !errorOccurred)
                    {
                        (currentDisplayName, _) = await UserService.GetUsernamesAsync(acc);
                        if (currentDisplayName == null) { errorOccurred = true; failureReasons.Add("Name check API failed"); }
                        else if (!string.Equals(currentDisplayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase)) { failureReasons.Add($"Name '{currentDisplayName}' != '{expectedDisplayName}'"); }
                        await Task.Delay(checkDelay, cancellationToken);
                    }

                    if (targetAvatarDetails != null && !errorOccurred)
                    {
                        var currentAvatarDetails = await AvatarService.FetchAvatarDetailsAsync(acc.UserId);
                        if (currentAvatarDetails == null) { errorOccurred = true; failureReasons.Add("Avatar fetch API failed"); }
                        else { avatarMatches = AvatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails); if (avatarMatches == false) failureReasons.Add("Avatar mismatch"); }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    errorOccurred = true;
                    failureReasons.Add($"Runtime Error: {ex.GetType().Name}");
                    Logger.LogError($"Exception during verification for {Markup.Escape(acc.Username)}: {Markup.Escape(ex.Message)}");
                }

                VerificationStatus finalStatus;
                string resultMessage;
                if (errorOccurred)
                {
                    finalStatus = VerificationStatus.Error;
                    failedErrCount++;
                    SessionData.LastVerificationFailedAccounts.Add(acc);
                    resultMessage = $"[[!]] [{Theme.Current.Error}]ERROR[/] - {string.Join(", ", failureReasons)}";
                }
                else if (failureReasons.Count > 0)
                {
                    finalStatus = VerificationStatus.Failed;
                    failedReqCount++;
                    SessionData.LastVerificationFailedAccounts.Add(acc);
                    resultMessage = $"[[!]] [{Theme.Current.Error}]FAIL[/] - {string.Join(", ", failureReasons)}";
                }
                else
                {
                    finalStatus = VerificationStatus.Passed;
                    passedCount++;
                    resultMessage = $"[[+]] [{Theme.Current.Success}]PASS[/]";
                }

                Logger.LogDefault($"   -> Result: {resultMessage}");
                accountManager.SetVerificationStatus(acc.UserId, finalStatus, string.Join("; ", failureReasons));
                await Task.Delay(AppConfig.CurrentApiDelayMs / 2, cancellationToken);
            }

            stopwatch.Stop();
            var summaryTable = new Table().Centered().Title("[bold]Verification Summary[/]");
            summaryTable.AddColumn("Status").AddColumn("Count");
            summaryTable.AddRow($"[{Theme.Current.Success}]Passed[/]", passedCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Error}]Failed (Requirements)[/]", failedReqCount.ToString());
            summaryTable.AddRow($"[bold {Theme.Current.Error}]Failed (API/Runtime Errors)[/]", failedErrCount.ToString());
            if (skippedInvalidCount > 0) summaryTable.AddRow($"[{Theme.Current.Warning}]Skipped (Invalid)[/]", skippedInvalidCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Header}]Total Verified[/]", validCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Prompt}]Total Time[/]", $"{stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
            AnsiConsole.Write(summaryTable);

            if (SessionData.LastVerificationFailedAccounts.Count != 0)
            {
                Logger.LogInfo("Verification found failures. Use 'Export Session Data' in the Main Menu to save the list of failed accounts.");
            }

            return failedReqCount > 0 || failedErrCount > 0;
        }
    }
}