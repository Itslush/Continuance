using System.Collections.Concurrent;
using System.Diagnostics;
using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Core
{
    public class AccountImporter(AccountManager accountManager, AuthenticationService authService)
    {
        private readonly AccountManager _accountManager = accountManager;
        private readonly AuthenticationService _authService = authService;

        public async Task<bool> AddAccountAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie) || !cookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogError("Invalid Format/Empty Cookie.");
                return false;
            }

            if (_accountManager.GetAllAccounts().Any(a => a.Cookie == cookie))
            {
                Logger.LogWarning("This cookie is already in the account pool.");
                return false;
            }

            try
            {
                Logger.LogInfo("Validating cookie integrity...");
                var (isValid, userId, username) = await AuthenticationService.ValidateCookieAsync(cookie);

                if (isValid && userId > 0)
                {
                    Logger.LogSuccess($"Cookie Valid :: User: {Markup.Escape(username)} (ID: {userId}). Fetching XCSRF token...");
                    string xcsrfRaw = await AuthenticationService.FetchXCSRFTokenAsync(cookie);
                    string xcsrf = xcsrfRaw?.Trim() ?? "";

                    var newAccount = new Account
                    {
                        Cookie = cookie,
                        UserId = userId,
                        Username = username,
                        XcsrfToken = xcsrf,
                        IsValid = !string.IsNullOrEmpty(xcsrf)
                    };

                    _accountManager.AddAccount(newAccount);
                    _accountManager.SortAccountsByUsername();

                    if (newAccount.IsValid)
                    {
                        Logger.LogSuccess($"Account Secured & Added to Account Pool. ({_accountManager.GetAllAccounts().Count} total)");
                        return true;
                    }
                    else
                    {
                        Logger.LogError("XCSRF Fetch Failed. Account added but marked as INVALID.");
                        return true;
                    }
                }
                else
                {
                    Logger.LogError("Cookie Validation Failed. Could not retrieve user info. Account not added.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"An unexpected error occurred while adding the account: {ex.Message}");
                Logger.LogMuted("This can be due to network issues or a temporary Roblox API problem. Please try again.");
                return false;
            }
        }

        public async Task ImportAccountsFromFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.LogError("File path provided is empty.");
                return;
            }

            if (!File.Exists(filePath))
            {
                Logger.LogError($"File not found at path: {Markup.Escape(filePath)}");
                return;
            }

            Logger.LogInfo($"Reading cookies from file: {Markup.Escape(filePath)}...");
            List<string> cookiesFromFile = [];
            int potentialCookiesFound = 0;

            int linesRead;
            try
            {
                string[] lines = await File.ReadAllLinesAsync(filePath);
                linesRead = lines.Length;

                foreach (string line in lines)
                {
                    string trimmedCookie = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedCookie) && trimmedCookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
                    {
                        potentialCookiesFound++;
                        if (!cookiesFromFile.Contains(trimmedCookie))
                        {
                            cookiesFromFile.Add(trimmedCookie);
                        }
                        else
                        {
                            Logger.LogWarning($"Skipping duplicate cookie found within file, hash: {ConsoleUI.GetShortCookieHash(trimmedCookie)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"An unexpected error occurred reading the file: {ex.Message}");
                return;
            }

            Logger.LogInfo($"Read {linesRead} lines from file. Found {potentialCookiesFound} potential cookies ({cookiesFromFile.Count} unique).");

            if (cookiesFromFile.Count > 0)
            {
                await ImportAccountsAsync(cookiesFromFile);
            }
            else
            {
                Logger.LogError("No valid, unique cookies found in the specified file.");
            }
        }

        public async Task ImportAccountsAsync(List<string> cookiesToImport)
        {
            if (cookiesToImport == null || cookiesToImport.Count == 0)
            {
                Logger.LogError("No cookies provided to import.");
                return;
            }

            Logger.NewLine();
            Logger.LogInfo($"Starting import/validation process for {cookiesToImport.Count} potential cookie(s)...");

            SessionData.LastImportFailedCookies.Clear();
            bool enablePause = false;
            if (cookiesToImport.Count > 150)
            {
                enablePause = AnsiConsole.Confirm($"\n   [[?]] Your list is large ({cookiesToImport.Count} accounts). Do you want to automatically pause for 10 seconds every 100 accounts to reduce rate limit risk?", defaultValue: true);
            }

            int successCount = 0, duplicateCount = 0, invalidCount = 0, fetchFailCount = 0, errorCount = 0;
            var stopwatch = Stopwatch.StartNew();
            var errorMessages = new ConcurrentBag<string>();

            int processedCount = 0;
            const int maxConcurrency = 5;
            const int batchSize = 100;
            int numBatches = (int)Math.Ceiling((double)cookiesToImport.Count / batchSize);

            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var importTask = ctx.AddTask($"[{Theme.Current.Success}]Importing cookies[/]", new ProgressTaskSettings { MaxValue = cookiesToImport.Count });
                    using var semaphore = new SemaphoreSlim(maxConcurrency);

                    for (int batchNum = 0; batchNum < numBatches; batchNum++)
                    {
                        var currentBatch = cookiesToImport.Skip(batchNum * batchSize).Take(batchSize).ToList();
                        var tasks = new List<Task>();

                        foreach (string cookie in currentBatch)
                        {
                            await semaphore.WaitAsync();
                            tasks.Add(Task.Run(async () =>
                            {
                                string shortHash = ConsoleUI.GetShortCookieHash(cookie);
                                try
                                {
                                    if (_accountManager.GetAllAccounts().Any(a => a.Cookie == cookie))
                                    {
                                        Interlocked.Increment(ref duplicateCount);
                                        Logger.LogWarning($"Skipped (Duplicate): Cookie '{shortHash}'");
                                        return;
                                    }

                                    var (isValid, userId, username) = await AuthenticationService.ValidateCookieAsync(cookie, suppressOutput: true);
                                    if (isValid && userId > 0)
                                    {
                                        string xcsrfRaw = await AuthenticationService.FetchXCSRFTokenAsync(cookie, suppressOutput: true);
                                        string xcsrf = xcsrfRaw?.Trim() ?? "";
                                        var newAccount = new Account { Cookie = cookie, UserId = userId, Username = username, XcsrfToken = xcsrf, IsValid = !string.IsNullOrEmpty(xcsrf) };

                                        _accountManager.AddAccount(newAccount);
                                        if (newAccount.IsValid)
                                        {
                                            Interlocked.Increment(ref successCount);
                                            Logger.LogSuccess($"Added account [bold]{Markup.Escape(username)}[/] (ID: {userId})");
                                        }
                                        else
                                        {
                                            Interlocked.Increment(ref fetchFailCount);
                                            Logger.LogError($"User [bold]{Markup.Escape(username)}[/] (ID: {userId}) - Cookie validated, but XCSRF fetch failed.");
                                        }
                                    }
                                    else
                                    {
                                        Interlocked.Increment(ref invalidCount);
                                        SessionData.LastImportFailedCookies.Add(cookie);
                                        Logger.LogError($"Cookie '[yellow]{shortHash}[/]' failed validation.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref errorCount);
                                    errorMessages.Add($"Failed to process cookie '{shortHash}': {ex.GetType().Name}");
                                    Logger.LogError($"Processing cookie '{shortHash}': {ex.GetType().Name}");
                                }
                                finally
                                {
                                    importTask.Increment(1);
                                    Interlocked.Increment(ref processedCount);
                                    semaphore.Release();
                                    await Task.Delay(AppConfig.CurrentImportDelayMs);
                                }
                            }));
                        }
                        await Task.WhenAll(tasks);

                        if (enablePause && batchNum < numBatches - 1)
                        {
                            Logger.NewLine();
                            Logger.LogAccent($">>> Processed batch {batchNum + 1}/{numBatches}. Pausing for 30 seconds to avoid getting rate limited...");
                            Logger.NewLine();
                            await Task.Delay(30000);
                        }
                    }
                });

            _accountManager.SortAccountsByUsername();
            stopwatch.Stop();
            Logger.NewLine();

            var summaryTable = new Table().Centered().Title("[bold underline]Import Summary[/]");

            summaryTable.AddColumn("Status", c => c.NoWrap()).AddColumn("Count");
            summaryTable.AddRow($"[{Theme.Current.Success}]Added & Valid[/]", $"[{Theme.Current.Success}]{successCount}[/]");
            summaryTable.AddRow($"[{Theme.Current.Warning}]Duplicates Skipped[/]", $"[{Theme.Current.Warning}]{duplicateCount}[/]");
            summaryTable.AddRow($"[{Theme.Current.Error}]Invalid / Validation Failed[/]", $"[{Theme.Current.Error}]{invalidCount}[/]");
            summaryTable.AddRow($"[{Theme.Current.Error}]Valid but XCSRF Fetch Failed[/]", $"[{Theme.Current.Error}]{fetchFailCount}[/]");

            if (errorCount > 0) summaryTable.AddRow($"[bold {Theme.Current.Error}]Errors during processing[/]", $"[bold {Theme.Current.Error}]{errorCount}[/]");

            summaryTable.AddRow($"[bold {Theme.Current.Header}]Total accounts in pool[/]", $"[{Theme.Current.Header}]{_accountManager.GetAllAccounts().Count}[/]");
            summaryTable.AddRow($"[bold {Theme.Current.Prompt}]Total time[/]", $"[{Theme.Current.Prompt}]{stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)[/]");

            AnsiConsole.Write(summaryTable);

            if (SessionData.LastImportFailedCookies.Count > 0)
            {
                Logger.LogInfo($"Found {SessionData.LastImportFailedCookies.Count} invalid cookies. Use the 'Export Session Data' option in the main menu to save them.");
            }

            if (!errorMessages.IsEmpty)
            {
                Logger.NewLine();
                Logger.LogError("Encountered errors during import: " + string.Join(", ", errorMessages));
            }
        }
    }
}