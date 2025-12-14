using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Newtonsoft.Json;
using Spectre.Console;
using System.Text;

namespace Continuance.CLI
{
    public class MainMenu(AccountManager accountManager, AccountImporter accountImporter, AccountSelector accountSelector, ActionsMenu actionsMenu)
    {
        private readonly AccountManager _accountManager = accountManager;
        private readonly AccountImporter _accountImporter = accountImporter;
        private readonly AccountSelector _accountSelector = accountSelector;
        private readonly ActionsMenu _actionsMenu = actionsMenu;
        private const string SettingsFilePath = "ContinuanceSettings.json";
        private const string AccountsFilePath = "ContinuanceAccounts.json";
        private static readonly char[] separator = [' ', ','];

        public async Task Show()
        {
            bool exit = false;
            while (!exit)
            {
                int totalAccounts = _accountManager.GetAllAccounts().Count;
                int selectedCount = _accountManager.GetSelectedAccountIndices().Count;
                int validCount = _accountManager.GetAllAccounts().Count(a => a.IsValid);
                int invalidCount = totalAccounts - validCount;

                Console.Clear();

                string nextStep;
                if (totalAccounts == 0)
                {
                    nextStep = $"[{Theme.Current.Warning}]To get started, import accounts using options 2, 3 or 4.[/]";
                }
                else if (selectedCount == 0)
                {
                    nextStep = $"[{Theme.Current.Warning}]Accounts are loaded. Use option 6 to select which accounts to use.[/]";
                }
                else
                {
                    nextStep = $"[{Theme.Current.Success}]Accounts selected. Proceed to the Actions Menu (option 9).[/]";
                }

                var statusGrid = new Grid()
                    .AddColumn()
                    .AddColumn(new GridColumn().RightAligned())
                    .AddRow($"[bold]Loaded Accounts:[/] [{Theme.Current.Accent1}]{totalAccounts}[/]", $"[{Theme.Current.Success}]{validCount} Valid[/] / [{Theme.Current.Error}]{invalidCount} Invalid[/]")
                    .AddRow($"[bold]Selected Accounts:[/] [{Theme.Current.Accent1}]{selectedCount}[/]", "");

                var statusPanel = new Panel(new Padder(statusGrid, new Padding(1, 0)))
                {
                    Header = new PanelHeader($"[bold {Theme.Current.Info}] ✦ Continuance Status ✦ [/]"),
                    Border = BoxBorder.Rounded,
                    Padding = new Padding(2, 1),
                    Expand = true
                };

                AnsiConsole.Write(statusPanel);
                AnsiConsole.MarkupLine($" [bold]Next Step:[/] {nextStep}\n");

                var accountManagementTable = new Table().Border(TableBorder.Rounded).Expand();
                accountManagementTable.AddColumn(new TableColumn($"[bold {Theme.Current.Header}]Account Management[/]").Centered());

                var managementItems = new[]
                {
                    $"[bold {Theme.Current.Info}] Account Importing [/]",
                    $"[bold]1.[/] Add Account [grey](Single Cookie)[/]",
                    $"[bold {Theme.Current.Warning}]2.[/] ✦ Import from Text File [grey](.txt)[/]",
                    $"[bold]3.[/] Import from Clipboard",
                    $"[bold {Theme.Current.Accent1}]4.[/] Add Account via Browser Login [grey](Interactive)[/]",
                    "",
                    $"[bold {Theme.Current.Info}] Account Selection & Account Review [/]",
                    $"[bold]5.[/] List All Accounts",
                    $"[bold]6.[/] Select / Deselect Accounts",
                    $"[bold]7.[/] Show Selected Accounts",
                    $"[bold]8.[/] Show Full Cookies"
                };

                foreach (var item in managementItems)
                {
                    accountManagementTable.AddRow(new Markup(item));
                }
                AnsiConsole.Write(accountManagementTable);

                var executionTable = new Table().Border(TableBorder.Rounded).Expand();
                executionTable.AddColumn(new TableColumn($"[bold {Theme.Current.Header}]Execution & Utilities[/]").Centered());

                var utilityItems = new[]
                {
                    $"[bold {Theme.Current.Info}] Account utilities & Adjustments [/]",
                    $"[bold {Theme.Current.Warning}]9.[/] ✦ Account Actions Menu",
                    $"[bold]10.[/] Adjust Rate Limits",
                    $"[bold]11.[/] Export Accounts to File",
                    "",
                    $"[bold {Theme.Current.Info}] Program Customization & Data Exporter [/]",
                    $"[bold]12.[/] Adjust Theme & Colors",
                    $"[bold {Theme.Current.Warning}]13.[/] ✦ Save Settings & Accounts",
                    $"[bold]14.[/] Export Session Data",
                    $"[bold {Theme.Current.Error}]15.[/] Exit Application"
                };

                foreach (var item in utilityItems)
                {
                    executionTable.AddRow(new Markup(item));
                }
                AnsiConsole.Write(executionTable);

                var choice = AnsiConsole.Prompt(new TextPrompt<string>($"\n[bold {Theme.Current.Prompt}]Choose option:[/] ").PromptStyle(Theme.Current.Prompt));

                switch (choice)
                {
                    case "1": await AddAccountUI(); await SaveAccounts(); break;
                    case "2": await ImportCookiesFromFileUI(); await SaveAccounts(); break;
                    case "3": await ImportCookiesFromInputUI(); await SaveAccounts(); break;
                    case "4": await AddAccountViaBrowserUI(); await SaveAccounts(); break;
                    case "5": Console.Clear(); AnsiConsole.Write(new Rule("[bold]Account Pool[/]").LeftJustified()); ListAccountsUI(); break;
                    case "6": SelectAccountsUI(); break;
                    case "7": Console.Clear(); AnsiConsole.Write(new Rule("[bold]Selected Accounts[/]").LeftJustified()); ShowSelectedAccountsUI(); break;
                    case "8": ShowAccountsWithCookiesUI(); break;
                    case "9":
                        if (selectedCount == 0) Logger.LogError("No accounts selected. Use option 6 first.");
                        else await _actionsMenu.Show();
                        break;
                    case "10": ActionsMenu.AdjustRateLimitsUI(); break;
                    case "11": await ExportAccountsToFileUI(); break;
                    case "12": AdjustThemeUI(); break;
                    case "13": await SaveSettingsAndAccountsUI(); break;
                    case "14": await ExportSessionDataUI(); break;
                    case "15": Logger.LogWarning("Exiting application..."); exit = true; break;
                    default: Logger.LogError("Invalid choice. Please enter a number from the menu."); break;
                }

                if (!exit && choice != "9")
                {
                    AnsiConsole.Prompt(new TextPrompt<string>($"\n[{Theme.Current.Muted}]Press Enter to return to the Main Menu...[/]").AllowEmpty());
                }
                else if (choice == "9" && selectedCount == 0)
                {
                    AnsiConsole.Prompt(new TextPrompt<string>($"\n[{Theme.Current.Muted}]Press Enter to return to the Main Menu...[/]").AllowEmpty());
                }
            }
        }

        private async Task AddAccountViaBrowserUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Add Account via Browser Login[/]").LeftJustified());
            Logger.LogInfo("A Chrome browser window will open.");
            Logger.LogInfo("1. Log in to your Roblox account normally.");
            Logger.LogInfo("2. Complete any 2FA or Captcha requirements.");
            Logger.LogInfo("3. Once the home page loads, the cookie will be grabbed automatically.");
            Logger.LogMuted("Close the browser window to cancel.");
            Logger.NewLine();

            string? capturedCookie = await WebDriverManager.CaptureCookieInteractiveAsync();

            if (!string.IsNullOrEmpty(capturedCookie))
            {
                Logger.LogSuccess("Cookie captured from browser session!");
                await _accountImporter.AddAccountAsync(capturedCookie);
            }
            else
            {
                Logger.LogWarning("Browser closed or login not detected. No account added.");
            }
        }

        private async Task AddAccountUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Panel($"Enter the full .ROBLOSECURITY cookie value below.\nIt should start with: [{Theme.Current.Warning}]_|WARNING:-[/]")
            {
                Header = new PanelHeader("[bold]Add Single Account[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1),
                Expand = true
            });

            string? cookie = AnsiConsole.Prompt(new TextPrompt<string>($"[{Theme.Current.Prompt}]Cookie:[/] ").PromptStyle(Theme.Current.Prompt));

            if (!string.IsNullOrWhiteSpace(cookie))
            {
                await _accountImporter.AddAccountAsync(cookie);
            }
            else
            {
                Logger.LogError("Input empty. Aborting.");
            }
        }

        private async Task ImportCookiesFromFileUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Panel($"Enter the full path to the text file containing cookies.\nThe file should have one cookie per line.\n[{Theme.Current.Muted}]Example: C:\\Users\\YourUser\\Desktop\\cookies.txt[/]")
            {
                Header = new PanelHeader("[bold]Import Cookies from File[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1),
                Expand = true
            });

            string? filePath = AnsiConsole.Prompt(new TextPrompt<string>($"[{Theme.Current.Prompt}]File Path:[/] ").PromptStyle(Theme.Current.Prompt));
            await _accountImporter.ImportAccountsFromFileAsync(filePath);
        }

        private async Task ImportCookiesFromInputUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Panel($"Paste one cookie per line below. Format: [{Theme.Current.Warning}]_|WARNING:-...[/]\n[{Theme.Current.Muted}]Press Enter on an empty line when finished.[/]")
            {
                Header = new PanelHeader("[bold]Import Cookies from Clipboard[/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1),
                Expand = true
            });

            var cookiesToImport = new List<string>();
            string? line;
            AnsiConsole.Markup($"   [[[{Theme.Current.Accent1}]1[/]]] >>> ");
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                string trimmedCookie = line.Trim();
                if (trimmedCookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase) && !cookiesToImport.Contains(trimmedCookie)) cookiesToImport.Add(trimmedCookie);
                AnsiConsole.Markup($"   [[[{Theme.Current.Accent1}]{cookiesToImport.Count + 1}[/]]] >>> ");
            }

            await _accountImporter.ImportAccountsAsync(cookiesToImport);
        }

        private void ListAccountsUI(bool showFooter = true)
        {
            var accounts = _accountManager.GetAllAccounts();
            if (accounts.Count == 0) { Logger.LogWarning("No accounts loaded."); return; }

            var table = new Table().Expand();
            table.AddColumn($"[{Theme.Current.Info}]Sel[/]").AddColumn($"[{Theme.Current.Info}]#[/]").AddColumn($"[{Theme.Current.Info}]Status[/]")
                 .AddColumn($"[{Theme.Current.Info}]ID[/]").AddColumn($"[{Theme.Current.Info}]Username[/]").AddColumn($"[{Theme.Current.Info}]Verify[/]");

            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                string sel = selectedIndices.Contains(i) ? $"[{Theme.Current.Warning}]*[/]" : $"[{Theme.Current.Muted}]-[/]";
                string status = account.IsValid ? $"[{Theme.Current.Success}]OK[/]" : $"[{Theme.Current.Error}]BAD[/]";
                string verify = _accountManager.GetVerificationStatus(account.UserId) switch
                {
                    VerificationStatus.Passed => $"[{Theme.Current.Success}]PASS[/]",
                    VerificationStatus.Failed => $"[{Theme.Current.Error}]FAIL[/]",
                    VerificationStatus.Error => $"[bold {Theme.Current.Error}]ERR[/]",
                    _ => $"[{Theme.Current.Muted}]N/A[/]"
                };
                table.AddRow(sel, (i + 1).ToString(), status, account.UserId.ToString(), $"[white]{Markup.Escape(account.Username)}[/]", verify);
            }
            AnsiConsole.Write(table);

            if (showFooter) Logger.LogMuted("Use option 6 to select/deselect accounts.");
        }

        private void ShowSelectedAccountsUI()
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            if (selectedIndices.Count == 0) { Logger.LogWarning("None selected."); return; }

            var table = new Table().Expand();
            table.AddColumn($"[{Theme.Current.Info}]#[/]").AddColumn($"[{Theme.Current.Info}]Status[/]").AddColumn($"[{Theme.Current.Info}]ID[/]")
                 .AddColumn($"[{Theme.Current.Info}]Username[/]").AddColumn($"[{Theme.Current.Info}]Verify[/]");

            var allAccounts = _accountManager.GetAllAccounts();
            foreach (int index in selectedIndices.OrderBy(i => i))
            {
                var account = allAccounts[index];
                string status = account.IsValid ? $"[{Theme.Current.Success}]OK[/]" : $"[{Theme.Current.Error}]BAD[/]";
                string verify = _accountManager.GetVerificationStatus(account.UserId) switch
                {
                    VerificationStatus.Passed => $"[{Theme.Current.Success}]PASS[/]",
                    VerificationStatus.Failed => $"[{Theme.Current.Error}]FAIL[/]",
                    VerificationStatus.Error => $"[bold {Theme.Current.Error}]ERR[/]",
                    _ => $"[{Theme.Current.Muted}]N/A[/]"
                };
                table.AddRow((index + 1).ToString(), status, account.UserId.ToString(), $"[white]{Markup.Escape(account.Username)}[/]", verify);
            }
            AnsiConsole.Write(table);
        }

        private void ShowAccountsWithCookiesUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Accounts with Full Cookies[/]").LeftJustified());
            var accounts = _accountManager.GetAllAccounts();
            if (accounts.Count == 0) { Logger.LogWarning("No accounts loaded."); return; }

            var table = new Table().Expand();
            table.AddColumn($"[{Theme.Current.Info}]#[/]").AddColumn($"[{Theme.Current.Info}]Status[/]").AddColumn($"[{Theme.Current.Info}]ID[/]")
                 .AddColumn($"")
                 .AddColumn($"[{Theme.Current.Info}]Username[/]").AddColumn($"[{Theme.Current.Info}]Cookie[/]");

            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                string status = account.IsValid ? $"[{Theme.Current.Success}]OK[/]" : $"[{Theme.Current.Error}]BAD[/]";
                table.AddRow((i + 1).ToString(), status, account.UserId.ToString(), $"[white]{Markup.Escape(account.Username)}[/]", $"[{Theme.Current.Muted}]{account.Cookie}[/]");
            }
            AnsiConsole.Write(table);
        }

        private void SelectAccountsUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Select / Deselect Accounts[/]").LeftJustified());
            ListAccountsUI(showFooter: false);

            var accounts = _accountManager.GetAllAccounts();
            if (accounts.Count == 0) return;

            Logger.NewLine();
            Logger.LogDefault($"Enter numbers (e.g., [{Theme.Current.Warning}]1 3 5[/]) or ranges (e.g., [{Theme.Current.Warning}]1-5 8 10-12[/]) to toggle.");
            Logger.LogDefault($"Commands: '[{Theme.Current.Warning}]all[/]', '[{Theme.Current.Warning}]none[/]', '[{Theme.Current.Warning}]valid[/]', '[{Theme.Current.Warning}]invalid[/]', '[{Theme.Current.Warning}]failed[/]'");
            string? input = AnsiConsole.Prompt(new TextPrompt<string>($"[{Theme.Current.Prompt}]Selection Input:[/] ").PromptStyle(Theme.Current.Prompt));

            if (string.IsNullOrWhiteSpace(input)) { Logger.LogError("No input provided."); return; }

            switch (input.ToLowerInvariant())
            {
                case "all": _accountSelector.SelectAll(); break;
                case "none": case "clear": _accountSelector.SelectNone(); break;
                case "valid": _accountSelector.SelectValid(); break;
                case "invalid": _accountSelector.SelectInvalid(); break;
                case "failed": _accountSelector.SelectFailedVerification(); break;
                default:
                    var indices = new List<int>();
                    foreach (var part in input.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (part.Contains('-'))
                        {
                            var range = part.Split('-');
                            if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                                indices.AddRange(Enumerable.Range(start, end - start + 1));
                        }
                        else if (int.TryParse(part, out int index)) indices.Add(index);
                    }
                    _accountSelector.UpdateSelection(indices);
                    break;
            }

            Logger.NewLine();
            AnsiConsole.Write(new Rule("[bold]Updated Selection[/]").LeftJustified());
            ShowSelectedAccountsUI();
        }

        private async Task ExportAccountsToFileUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Export Cookies & Usernames[/]").LeftJustified());

            var allAccounts = _accountManager.GetAllAccounts();
            if (allAccounts.Count == 0) { Logger.LogError("No accounts are loaded to export."); return; }

            var filter = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Which accounts to export?").AddChoices(["All", "Selected", "Passed Verification", "Failed Verification", "Valid", "Invalid"]));
            List<Account> toExport = filter switch
            {
                "Selected" => _accountManager.GetSelectedAccounts(),
                "Passed Verification" => [.. allAccounts.Where(a => _accountManager.GetVerificationStatus(a.UserId) == VerificationStatus.Passed)],
                "Failed Verification" => [.. allAccounts.Where(a => _accountManager.GetVerificationStatus(a.UserId) is VerificationStatus.Failed or VerificationStatus.Error)],
                "Valid" => [.. allAccounts.Where(a => a.IsValid)],
                "Invalid" => [.. allAccounts.Where(a => !a.IsValid)],
                _ => [.. allAccounts],
            };

            if (toExport.Count == 0) { Logger.LogError($"No accounts found matching filter '{filter}'."); return; }

            string fileName = AnsiConsole.Prompt(new TextPrompt<string>($"[{Theme.Current.Prompt}]Enter filename:[/] ").DefaultValue("cookies_export.txt"));
            bool sort = AnsiConsole.Confirm("Sort usernames alphabetically?", true);

            await AccountExporter.ExportAccountsToFileAsync(fileName, toExport, sort);
        }

        private static void AdjustThemeUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule($"[bold]Adjust Theme & Colors[/]").LeftJustified());

            var choices = PresetThemes.Themes.Keys.ToList();
            choices.Add("Edit Current Colors Manually...");
            choices.Add("Back");

            var selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a preset theme or edit manually:")
                    .PageSize(10)
                    .MoreChoicesText($"[{Theme.Current.Muted}](Move up and down to reveal more choices)[/]")
                    .AddChoices(choices)
            );

            if (selection == "Back")
            {
                return;
            }
            else if (selection == "Edit Current Colors Manually...")
            {
                EditThemeManuallyUI();
            }
            else
            {
                var selectedTheme = PresetThemes.Themes[selection];
                Theme.Load(new ColorPalette
                {
                    Success = selectedTheme.Success,
                    Error = selectedTheme.Error,
                    Warning = selectedTheme.Warning,
                    Info = selectedTheme.Info,
                    Prompt = selectedTheme.Prompt,
                    Header = selectedTheme.Header,
                    Muted = selectedTheme.Muted,
                    Accent1 = selectedTheme.Accent1
                });
                Logger.LogSuccess($"Theme set to '{Markup.Escape(selection)}'.");
            }
            Logger.LogMuted("Use 'Save Settings & Accounts' in the main menu to make this permanent.");
        }

        private static void EditThemeManuallyUI()
        {
            Logger.LogMuted("\nEnter a color name (e.g., blue) or hex code (e.g., #FF00FF). Press Enter to keep current value.\n");

            var theme = Theme.Current;
            static string prompt(string name, string current) => AnsiConsole.Prompt(new TextPrompt<string>(name.PadRight(15)).DefaultValue(current).PromptStyle(current));

            theme.Success = prompt("Success", theme.Success);
            theme.Error = prompt("Error", theme.Error);
            theme.Warning = prompt("Warning", theme.Warning);
            theme.Info = prompt("Info", theme.Info);
            theme.Prompt = prompt("Prompt", theme.Prompt);
            theme.Header = prompt("Header", theme.Header);
            theme.Muted = prompt("Muted", theme.Muted);
            theme.Accent1 = prompt("Accent", theme.Accent1);

            Logger.NewLine();
            Logger.LogSuccess("Theme updated for this session.");
        }

        private async Task SaveSettingsAndAccountsUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Save Settings and Accounts[/]").LeftJustified());
            Logger.LogInfo("This will save the following to disk:");
            Logger.LogDefault($"  - All current rate limits, timeouts, action defaults, and theme colors to '[{Theme.Current.Warning}]{SettingsFilePath}[/]'");
            Logger.LogDefault($"  - All currently loaded accounts and their state to '[{Theme.Current.Warning}]{AccountsFilePath}[/]'");

            if (AnsiConsole.Confirm($"\n[bold {Theme.Current.Prompt}]Save current settings and accounts now?[/]"))
            {
                await SaveSettings(AppConfig.GetCurrentSettings());
                await SaveAccounts();
            }
            else
            {
                Logger.LogError("Save cancelled.");
            }
        }

        private static async Task SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                await File.WriteAllTextAsync(SettingsFilePath, json);
                Logger.LogSuccess($"Settings successfully saved to {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save settings: {Markup.Escape(ex.Message)}");
            }
        }

        private async Task SaveAccounts()
        {
            try
            {
                var accounts = _accountManager.GetAllAccounts();
                string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
                await File.WriteAllTextAsync(AccountsFilePath, json);
                Logger.LogSuccess($"{accounts.Count} accounts successfully saved to {AccountsFilePath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save accounts: {Markup.Escape(ex.Message)}");
            }
        }

        private static async Task ExportSessionDataUI()
        {
            Console.Clear();
            AnsiConsole.Write(new Rule("[bold]Export Session Data[/]").LeftJustified());
            Logger.LogInfo("This tool exports data captured during the current session.");

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("What would you like to export?")
                .AddChoices([
                    "Full Session Log",
                    "Failed Cookies (from last import)",
                    "Failed Accounts (from last verification)",
                    "Back"
                ]));

            if (choice == "Back") return;

            string fileTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName;

            switch (choice)
            {
                case "Full Session Log":
                    fileName = $"Continuance_Log_{fileTimestamp}.log";
                    await Logger.ExportLogHistoryAsync(fileName);
                    break;

                case "Failed Cookies (from last import)":
                    if (SessionData.LastImportFailedCookies.Count == 0)
                    {
                        Logger.LogWarning("No failed cookies were recorded in the last import session.");
                        return;
                    }
                    fileName = $"Failed_Import_Cookies_{fileTimestamp}.txt";
                    try
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"# Failed Cookies from import at {DateTime.Now}");
                        SessionData.LastImportFailedCookies.ForEach(c => sb.AppendLine(c));
                        await File.WriteAllTextAsync(fileName, sb.ToString());
                        Logger.LogSuccess($"Successfully exported {SessionData.LastImportFailedCookies.Count} failed cookies to {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to export failed cookies: {ex.Message}");
                    }
                    break;

                case "Failed Accounts (from last verification)":
                    if (SessionData.LastVerificationFailedAccounts.Count == 0)
                    {
                        Logger.LogWarning("No accounts failed the last verification run.");
                        return;
                    }
                    fileName = $"Failed_Verification_Accounts_{fileTimestamp}.txt";
                    await AccountExporter.ExportAccountsToFileAsync(fileName, SessionData.LastVerificationFailedAccounts, true);
                    break;
            }
        }
    }
}