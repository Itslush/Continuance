using System.Diagnostics;
using Continuance.CLI;
using Continuance.Core;
using Continuance.Models;
using Spectre.Console;

namespace Continuance.Actions
{
    public class AccountActionExecutor(AccountManager accountManager)
    {
        private readonly AccountManager _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));

        public async Task ExecuteOnSelectedAsync(
            IContinuanceAction action,
            string actionName,
            CancellationToken cancellationToken,
            bool requireInteraction = false,
            bool requireValidToken = true)
        {
            var escapedActionName = Markup.Escape(actionName);

            if (requireInteraction && !Environment.UserInteractive)
            {
                Logger.LogWarning($"Skipping interactive action '{escapedActionName}' in non-interactive environment.");
                return;
            }

            var selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccountsRaw
                .Where(acc => acc != null && acc.IsValid && (!requireValidToken || !string.IsNullOrEmpty(acc.XcsrfToken)))
                .ToList();

            int totalSelected = selectedAccountsRaw.Count;
            int validCount = accountsToProcess.Count;
            int skippedInvalidCount = totalSelected - validCount;

            if (validCount >= AppConfig.CurrentActionConfirmationThreshold)
            {
                if (!AnsiConsole.Confirm($"   [[?]] You are about to run action '[{Theme.Current.Warning}]{escapedActionName}[/]' on [bold {Theme.Current.Success}]{validCount}[/] accounts. Proceed?"))
                {
                    Logger.LogError("Action cancelled by user.");
                    return;
                }
                Logger.LogInfo("Confirmation received. Proceeding...");
            }
            cancellationToken.ThrowIfCancellationRequested();

            AnsiConsole.Write(new Rule($"[bold]Executing Action: {escapedActionName} for {validCount} valid account(s)[/]") { Justification = Justify.Left });
            if (skippedInvalidCount > 0)
            {
                string reason = requireValidToken ? "invalid / lacked XCSRF" : "marked invalid";
                Logger.LogWarning($"({skippedInvalidCount} selected accounts were {reason} and will be skipped)");
            }
            if (validCount == 0 && totalSelected > 0)
            {
                Logger.LogError("All selected accounts were skipped.");
                return;
            }
            else if (validCount == 0)
            {
                Logger.LogWarning("No accounts selected for this action.");
                return;
            }

            int successCount = 0, failCount = 0, skippedPreCheckCount = 0;
            var stopwatch = Stopwatch.StartNew();
            int maxRetries = AppConfig.CurrentMaxApiRetries;
            int retryDelayMs = AppConfig.CurrentApiRetryDelayMs;
            int baseDelayMs = AppConfig.CurrentApiDelayMs;

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Account acc = accountsToProcess[i];

                Logger.NewLine();
                Logger.LogDefault($"[[[bold {Theme.Current.Accent1}]{i + 1}/{validCount}[/]]] Starting action '[{Theme.Current.Warning}]{escapedActionName}[/]' for: [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] (ID: {acc.UserId})...");

                bool finalSuccess = false;
                bool finalSkipped = false;
                Exception? lastException = null;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (attempt > 0)
                        {
                            Logger.LogInfo($"Retrying action... (Attempt {attempt + 1}/{maxRetries + 1})");
                        }

                        var (currentSuccess, currentSkipped) = await action.ExecuteAsync(acc, cancellationToken);
                        finalSuccess = currentSuccess;
                        finalSkipped = currentSkipped;
                        lastException = null;

                        if (finalSuccess || finalSkipped) break;

                        if (attempt < maxRetries)
                        {
                            Logger.LogWarning($"Action failed on attempt {attempt + 1}. Retrying after {retryDelayMs}ms...");
                            await Task.Delay(retryDelayMs, cancellationToken);
                        }
                        else
                        {
                            Logger.LogError($"Action failed after {maxRetries + 1} attempts.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        finalSuccess = false;
                        finalSkipped = false;
                        Logger.LogError($"Exception on attempt {attempt + 1}: {ex.GetType().Name}");

                        if (attempt < maxRetries)
                        {
                            if (!acc.IsValid)
                            {
                                Logger.LogError("Account marked invalid during operation. Stopping retries.");
                                break;
                            }
                            Logger.LogWarning($"Retrying after exception ({retryDelayMs}ms)...");
                            await Task.Delay(retryDelayMs, cancellationToken);
                        }
                        else
                        {
                            Logger.LogError($"Action failed due to exception after {maxRetries + 1} attempts.");
                            if (!acc.IsValid) { Logger.LogError("Account was marked invalid."); }
                        }
                    }
                }

                string resultIndicator = finalSkipped ? $"[{Theme.Current.Warning}]Skipped[/]" : finalSuccess ? $"[{Theme.Current.Success}]Success[/]" : $"[{Theme.Current.Error}]Failed[/]";
                if (finalSkipped) skippedPreCheckCount++; else if (finalSuccess) successCount++; else failCount++;

                string finalResultLine = $"Result for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]: {resultIndicator}";

                if (finalSuccess) Logger.LogSuccess(finalResultLine);
                else if (finalSkipped) Logger.LogWarning(finalResultLine);
                else Logger.LogError(finalResultLine);

                if (!finalSuccess && !finalSkipped && lastException != null)
                {
                    string errorType = lastException switch
                    {
                        HttpRequestException hrex => $"Network Error ({(int?)hrex.StatusCode})",
                        _ => $"Runtime Error ({lastException.GetType().Name})"
                    };
                    Logger.LogError($"-> Details: {errorType} - {ConsoleUI.Truncate(lastException.Message)}");
                }

                if (i < accountsToProcess.Count - 1)
                {
                    await Task.Delay(baseDelayMs / 2, cancellationToken);
                }
            }

            Logger.NewLine();
            stopwatch.Stop();
            var summaryTable = new Table().Centered();
            summaryTable.Title($"[bold]Action '{escapedActionName}' Finished[/]");
            summaryTable.AddColumn("Status").AddColumn("Count");
            summaryTable.AddRow($"[{Theme.Current.Success}]Success[/]", successCount.ToString());
            if (skippedPreCheckCount > 0) summaryTable.AddRow($"[{Theme.Current.Warning}]Skipped (Pre-Check Met)[/]", skippedPreCheckCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Error}]Failed[/]", failCount.ToString());
            if (skippedInvalidCount > 0) summaryTable.AddRow($"[{Theme.Current.Warning}]Skipped (Invalid/Token)[/]", skippedInvalidCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Header}]Total Processed[/]", validCount.ToString());
            summaryTable.AddRow($"[{Theme.Current.Prompt}]Total Time[/]", $"{stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
            AnsiConsole.Write(summaryTable);
        }
    }
}