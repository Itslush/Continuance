using System.Diagnostics;
using Continuance.CLI;
using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Actions
{
    public class HandleFriendRequestsAction(AccountManager accountManager, FriendService friendService, int friendGoal)
    {
        private readonly AccountManager _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        private readonly FriendService _friendService = friendService ?? throw new ArgumentNullException(nameof(friendService));

        private enum AcceptAttemptResult { Accepted, Failed, Skipped_AlreadyDone, Skipped_InvalidSender, Skipped_InvalidReceiver, Skipped_SendNotSuccessful }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var overallStopwatch = Stopwatch.StartNew();
            int totalAttemptedSends = 0, totalSuccessSends = 0, totalFailedSends = 0, totalPendingSends = 0;
            int totalAttemptedAccepts = 0, totalSuccessAccepts = 0, totalFailedAccepts = 0, totalSkippedAccepts = 0;

            List<Account> selectedAccountsRaw = _accountManager.GetSelectedAccounts();

            AnsiConsole.Write(new Rule($"[bold]Executing Action: Friend Actions (Goal: >= {friendGoal} friends)[/]").LeftJustified());

            if (selectedAccountsRaw.Count < 2)
            {
                Logger.LogError("Need at least 2 selected accounts for this action. Aborting.");
                return;
            }

            var (accountsNeedingFriends, preCheckStats) = await PreCheckAccountsAsync(selectedAccountsRaw, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var summaryPanel = new Panel(
                $"[{Theme.Current.Success}]Ready to Proceed:[/] {accountsNeedingFriends.Count}\n" +
                $"[{Theme.Current.Info}]Refreshed/Initialized Tokens:[/] {preCheckStats.RefreshedTokens}\n" +
                $"[{Theme.Current.Warning}]Already Met Goal:[/] {preCheckStats.AlreadyMetGoal}\n" +
                $"[{Theme.Current.Error}]Skipped/Failed (Invalid/Token):[/] {preCheckStats.FailedPreCheck}\n" +
                $"[{Theme.Current.Error}]Friend Check API Errors:[/] {preCheckStats.FriendCheckErrors}"
            )
            {
                Header = new PanelHeader($"[bold]Pre-check Summary[/] (Took {preCheckStats.ElapsedMs}ms)"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1)
            };
            AnsiConsole.Write(summaryPanel);

            if (accountsNeedingFriends.Count < 2)
            {
                Logger.NewLine();
                Logger.LogError($"Need at least 2 valid accounts below the friend goal. Found {accountsNeedingFriends.Count}. Aborting friend cycle.");
                return;
            }

            if (accountsNeedingFriends.Count >= AppConfig.CurrentActionConfirmationThreshold)
            {
                if (!AnsiConsole.Confirm($"   [[?]] You are about to run friend actions between [bold {Theme.Current.Success}]{accountsNeedingFriends.Count}[/] accounts. Proceed?"))
                {
                    Logger.LogError("Action cancelled by user.");
                    return;
                }
            }

            var (useBatching, batchSize, batchDelaySeconds) = GetBatchingOptions(accountsNeedingFriends.Count);
            int numBatches = useBatching ? (int)Math.Ceiling((double)accountsNeedingFriends.Count / batchSize) : 1;

            for (int batchNum = 0; batchNum < numBatches; batchNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentBatchAccounts = accountsNeedingFriends.Skip(batchNum * batchSize).Take(batchSize).ToList();
                if (currentBatchAccounts.Count < 2)
                {
                    Logger.LogWarning($"Skipping Batch {batchNum + 1}/{numBatches}: Contains fewer than 2 accounts.");
                    continue;
                }

                AnsiConsole.Write(new Rule($"[bold]Processing Batch {batchNum + 1}/{numBatches} ({currentBatchAccounts.Count} accounts)[/]").LeftJustified());

                var (sentPairs, sendStats) = await SendFriendRequestsInBatchAsync(currentBatchAccounts, batchNum, numBatches, cancellationToken);
                totalAttemptedSends += sendStats.Attempted;
                totalSuccessSends += sendStats.Success;
                totalPendingSends += sendStats.Pending;
                totalFailedSends += sendStats.Failed;

                if (sentPairs.Count == 0)
                {
                    Logger.LogWarning($"Batch {batchNum + 1}: No sends were successful or pending. Skipping Phase 2 for this batch.");
                }
                else
                {
                    const int phase2DelaySeconds = 75;
                    Logger.NewLine();
                    Logger.LogInfo($"Phase 1 Sends complete. Waiting {phase2DelaySeconds}s for server processing...");
                    await Task.Delay(TimeSpan.FromSeconds(phase2DelaySeconds), cancellationToken);

                    var (Attempted, Success, Failed, Skipped) = await AcceptFriendRequestsInBatchAsync(currentBatchAccounts, sentPairs, batchNum, numBatches, cancellationToken);
                    totalAttemptedAccepts += Attempted;
                    totalSuccessAccepts += Success;
                    totalFailedAccepts += Failed;
                    totalSkippedAccepts += Skipped;
                }

                if (useBatching && batchNum < numBatches - 1)
                {
                    Logger.NewLine();
                    Logger.LogDefault($"--- Batch {batchNum + 1} finished. Waiting [yellow]{batchDelaySeconds}s[/] before next batch... ---");
                    await Task.Delay(TimeSpan.FromSeconds(batchDelaySeconds), cancellationToken);
                }
            }

            Logger.NewLine();
            overallStopwatch.Stop();
            var summaryTable = new Table().Centered();
            summaryTable.Title("[bold underline]Overall Friend Action Summary[/]");
            summaryTable.AddColumn("Category").AddColumn("Details").AddColumn("Count");

            summaryTable.AddRow(new Markup("[bold]Phase 1: Sending[/]"), new Markup($"[{Theme.Current.Success}]Successful Sends[/]"), new Markup($"[{Theme.Current.Success}]{totalSuccessSends}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Warning}]Pending/Already Friends[/]"), new Markup($"[{Theme.Current.Warning}]{totalPendingSends}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Error}]Failed Sends/Errors[/]"), new Markup($"[{Theme.Current.Error}]{totalFailedSends}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Header}]Total Attempted Sends[/]"), new Markup($"[{Theme.Current.Header}]{totalAttemptedSends}[/]"));
            summaryTable.AddEmptyRow();
            summaryTable.AddRow(new Markup("[bold]Phase 2: Accepting[/]"), new Markup($"[{Theme.Current.Success}]Successful Accepts[/]"), new Markup($"[{Theme.Current.Success}]{totalSuccessAccepts}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Error}]Failed Accepts/Errors[/]"), new Markup($"[{Theme.Current.Error}]{totalFailedAccepts}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Warning}]Skipped Accepts[/]"), new Markup($"[{Theme.Current.Warning}]{totalSkippedAccepts}[/]"));
            summaryTable.AddRow(new Markup(""), new Markup($"[{Theme.Current.Header}]Total Attempted Accepts[/]"), new Markup($"[{Theme.Current.Header}]{totalAttemptedAccepts}[/]"));
            summaryTable.AddEmptyRow();
            summaryTable.AddRow(new Markup("[bold]Total Time[/]"), new Markup($"[{Theme.Current.Prompt}]{overallStopwatch.ElapsedMilliseconds}ms[/]"), new Markup($"[{Theme.Current.Accent1}]({overallStopwatch.Elapsed.TotalSeconds:F1}s)[/]"));

            AnsiConsole.Write(summaryTable);
            Logger.LogMuted("Reminder: Run the 'Verify' action to confirm final friend counts.");
        }

        private async Task<(List<Account>, (int RefreshedTokens, int FailedPreCheck, int FriendCheckErrors, int AlreadyMetGoal, long ElapsedMs))> PreCheckAccountsAsync(List<Account> accounts, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            var accountsNeedingFriends = new List<Account>();
            int refreshed = 0, failed = 0, errors = 0, metGoal = 0;

            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[{Theme.Current.Success}]Pre-checking accounts[/]", new ProgressTaskSettings { MaxValue = accounts.Count });
                    foreach (var acc in accounts)
                    {
                        ct.ThrowIfCancellationRequested();
                        task.Description = $"[{Theme.Current.Warning}]Checking:[/] [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]";

                        if (!acc.IsValid || string.IsNullOrEmpty(acc.Cookie))
                        {
                            failed++;
                            task.Increment(1);
                            continue;
                        }

                        string oldToken = acc.XcsrfToken;
                        bool tokenOk = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(acc);

                        if (!tokenOk || !acc.IsValid || string.IsNullOrEmpty(acc.XcsrfToken))
                        {
                            failed++;
                            task.Increment(1);
                            continue;
                        }

                        if (acc.XcsrfToken != oldToken) refreshed++;

                        int friendCount = await FriendService.GetFriendCountAsync(acc);
                        await Task.Delay(AppConfig.CurrentApiDelayMs / 4, ct);

                        if (friendCount == -1)
                        {
                            errors++;
                        }
                        else
                        {
                            if (friendCount >= friendGoal)
                            {
                                metGoal++;
                            }
                            else
                            {
                                accountsNeedingFriends.Add(acc);
                            }
                        }
                        task.Increment(1);
                    }
                });

            stopwatch.Stop();
            return (
                accountsNeedingFriends.OrderBy(a => a.UserId).ToList(),
                (refreshed, failed, errors, metGoal, stopwatch.ElapsedMilliseconds)
            );
        }

        private static (bool UseBatching, int BatchSize, int BatchDelay) GetBatchingOptions(int totalAccounts)
        {
            const int batchPromptThreshold = 20;
            const int defaultBatchSize = 10;
            const int minBatchSize = 5;
            const int defaultBatchDelay = 120;

            if (totalAccounts < batchPromptThreshold) return (false, totalAccounts, 0);

            Logger.NewLine();
            if (!AnsiConsole.Confirm($"   [[?]] Process {totalAccounts} accounts in smaller batches to reduce rate limit risk?", defaultValue: true))
            {
                Logger.LogWarning("Processing all accounts at once.");
                return (false, totalAccounts, 0);
            }

            int batchSize = AnsiConsole.Prompt(
                new TextPrompt<int>($"   [[?]] Enter batch size (min {minBatchSize}) or blank for default ({defaultBatchSize}):")
                    .DefaultValue(defaultBatchSize)
                    .Validate(size => size >= minBatchSize ? ValidationResult.Success() : ValidationResult.Error($"[{Theme.Current.Error}]Batch size must be at least {minBatchSize}[/]"))
            );

            int batchDelay = AnsiConsole.Prompt(
                new TextPrompt<int>("   [[?]] Enter delay between batches in seconds (e.g., 60):")
                    .DefaultValue(defaultBatchDelay)
                    .Validate(delay => delay >= 10 ? ValidationResult.Success() : ValidationResult.Error($"[{Theme.Current.Error}]Delay must be at least 10 seconds[/]"))
            );

            return (true, batchSize, batchDelay);
        }

        private static async Task<(HashSet<Tuple<long, long>> SentPairs, (int Attempted, int Success, int Pending, int Failed) Stats)> SendFriendRequestsInBatchAsync(List<Account> batchAccounts, int batchNum, int numBatches, CancellationToken ct)
        {
            Logger.NewLine();
            Logger.LogInfo($"Phase 1 (Batch {batchNum + 1}/{numBatches}): Sending Friend Requests...");

            int attempted = 0, success = 0, pending = 0, failed = 0;
            var successfulSendPairs = new HashSet<Tuple<long, long>>();
            var stopwatch = Stopwatch.StartNew();
            int baseDelay = AppConfig.CurrentFriendActionDelayMs;
            int randomness = Math.Min(baseDelay / 2, 2000);

            for (int i = 0; i < batchAccounts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Account receiver = batchAccounts[i];
                var senders = new List<Account>
                {
                    batchAccounts[(i + 1) % batchAccounts.Count]
                };

                if (batchAccounts.Count > 2)
                {
                    senders.Add(batchAccounts[(i + 2) % batchAccounts.Count]);
                }
                Logger.NewLine();
                Logger.LogAccent($"Send Target: {receiver.Username} (ID: {receiver.UserId})");

                foreach (var sender in senders.DistinctBy(s => s.UserId))
                {
                    if (sender.UserId == receiver.UserId) continue;

                    Logger.LogDefault($" >>> Sending from {sender.Username}... ");
                    attempted++;
                    try
                    {
                        if (!sender.IsValid || string.IsNullOrEmpty(sender.XcsrfToken))
                        {
                            Logger.LogError ($"Fail (Sender invalid).");
                            failed++;
                        }
                        else
                        {
                            var (sendOk, isPending, reason) = await FriendService.SendFriendRequestAsync(sender, receiver.UserId, receiver.Username);
                            await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseDelay - randomness), baseDelay + randomness), ct);

                            if (sendOk)
                            {
                                Logger.LogSuccess("OK");
                                success++;
                                successfulSendPairs.Add(Tuple.Create(sender.UserId, receiver.UserId));
                            }
                            else if (isPending)
                            {
                                Logger.LogWarning($"Skipped (Pending/Friends).");
                                pending++;
                                successfulSendPairs.Add(Tuple.Create(sender.UserId, receiver.UserId));
                            }
                            else
                            {
                                Logger.LogError($"[{Theme.Current.Error}]Fail[/] [{Theme.Current.Muted}] {reason} [/]");
                                failed++;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Logger.LogError($"[{Theme.Current.Error}]Fail (Exception: {ex.GetType().Name}).[/]");
                        failed++;
                    }
                }
            }

            stopwatch.Stop();
            Logger.NewLine();
            Logger.LogInfo($"Batch {batchNum + 1} Phase 1 Complete ({stopwatch.ElapsedMilliseconds}ms). " + $"Success: [{Theme.Current.Success}]{success}[/], Pending: [{Theme.Current.Warning}]{pending}[/], Failed: [{Theme.Current.Error}]{failed}[/]");
            return (successfulSendPairs, (attempted, success, pending, failed));
        }

        private static async Task<(int Attempted, int Success, int Failed, int Skipped)> AcceptFriendRequestsInBatchAsync(List<Account> batchAccounts, HashSet<Tuple<long, long>> sentPairs, int batchNum, int numBatches, CancellationToken ct)
        {
            Logger.NewLine();
            Logger.LogInfo($"Phase 2 (Batch {batchNum + 1}/{numBatches}): Attempting to accept requests...");
            int attempted = 0, success = 0, failed = 0, skipped = 0;
            var stopwatch = Stopwatch.StartNew();
            var acceptedPairs = new HashSet<Tuple<long, long>>();
            int baseDelay = AppConfig.CurrentFriendActionDelayMs;
            int randomness = Math.Min(baseDelay / 2, 2000);

            for (int i = 0; i < batchAccounts.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Account receiver = batchAccounts[i];
                Logger.NewLine();
                Logger.LogAccent($"Accept Target: {receiver.Username} (ID: {receiver.UserId})");

                if (!receiver.IsValid || string.IsNullOrEmpty(receiver.XcsrfToken))
                {
                    Logger.LogWarning("Skipping receiver (invalid account).");
                    skipped += batchAccounts.Count > 2 ? 2 : 1;
                    continue;
                }

                var potentialSenders = new List<Account>
                {
                    batchAccounts[(i + 1) % batchAccounts.Count]
                };
                if (batchAccounts.Count > 2)
                {
                    potentialSenders.Add(batchAccounts[(i + 2) % batchAccounts.Count]);
                }

                foreach (var sender in potentialSenders.DistinctBy(s => s.UserId))
                {
                    if (sender.UserId == receiver.UserId) continue;

                    var expectedPair = Tuple.Create(sender.UserId, receiver.UserId);
                    if (sentPairs.Contains(expectedPair))
                    {
                        attempted++;
                        var result = await TryAcceptRequestAsync(receiver, sender, acceptedPairs, baseDelay, randomness, ct);
                        switch (result)
                        {
                            case AcceptAttemptResult.Accepted: success++; break;
                            case AcceptAttemptResult.Failed: failed++; break;
                            default: skipped++; break;
                        }
                    }
                    else
                    {
                        Logger.LogMuted($"Skipping accept from {sender.Username} (send did not succeed)");
                        skipped++;
                    }
                }
                if (i < batchAccounts.Count - 1)
                {
                    await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs), ct);
                }
            }

            stopwatch.Stop();
            Logger.NewLine();
            Logger.LogInfo($"Batch {batchNum + 1} Phase 2 Complete ({stopwatch.ElapsedMilliseconds}ms). " +
                                   $"Success: [{Theme.Current.Success}]{success}[/], Failed: [{Theme.Current.Error}]{failed}[/], Skipped: [{Theme.Current.Warning}]{skipped}[/]");
            return (attempted, success, failed, skipped);
        }

        private static async Task<AcceptAttemptResult> TryAcceptRequestAsync(Account receiver, Account sender, HashSet<Tuple<long, long>> acceptedPairs, int baseDelay, int randomness, CancellationToken ct)
        {
            Logger.LogDefault($" >>> Accepting from {sender.Username}... ");

            var currentPair = Tuple.Create(sender.UserId, receiver.UserId);
            var reversePair = Tuple.Create(receiver.UserId, sender.UserId);

            if (acceptedPairs.Contains(currentPair) || acceptedPairs.Contains(reversePair))
            {
                Logger.LogWarning($"Skipped (already accepted).");
                return AcceptAttemptResult.Skipped_AlreadyDone;
            }

            if (!sender.IsValid)
            {
                Logger.LogWarning($"Skipped (sender invalid).");
                return AcceptAttemptResult.Skipped_InvalidSender;
            }
            if (!receiver.IsValid || string.IsNullOrEmpty(receiver.XcsrfToken))
            {
                Logger.LogError($"Fail (receiver invalid).");
                return AcceptAttemptResult.Skipped_InvalidReceiver;
            }

            try
            {
                bool acceptOk = await FriendService.AcceptFriendRequestAsync(receiver, sender.UserId, sender.Username);
                await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseDelay - randomness), baseDelay + randomness), ct);

                if (acceptOk)
                {
                    Logger.LogSuccess("OK");

                    acceptedPairs.Add(currentPair);
                    acceptedPairs.Add(reversePair);

                    return AcceptAttemptResult.Accepted;
                }
                else
                {
                    Logger.LogError($"Fail (API error).");
                    return AcceptAttemptResult.Failed;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError($"Fail (Exception: {ex.GetType().Name}).");
                return AcceptAttemptResult.Failed;
            }
        }
    }
}