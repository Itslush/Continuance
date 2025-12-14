using Continuance.Actions;
using Continuance.CLI;
using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Continuance.Roblox.Http;
using Continuance.Roblox.Services;
using Newtonsoft.Json;
using Spectre.Console;
using System.Runtime.InteropServices;

namespace Continuance
{
    public partial class Initialize
    {
        private const int MF_BYCOMMAND = 0x00000000;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_SIZE = 0xF000;

        [DllImport("user32.dll")]
        private static extern int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private const string SettingsFilePath = "ContinuanceSettings.json";
        private const string AccountsFilePath = "ContinuanceAccounts.json";

        private static BackgroundKeepAliveService? _bgService;

        private static void LoadSettings()
        {
            AppSettings settings = new();
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    Logger.LogSuccess($"Loaded settings from {SettingsFilePath}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load settings from {SettingsFilePath}: {ex.Message}. Using defaults.");
                    SaveSettings(settings);
                }
            }
            else
            {
                Logger.LogWarning($"Settings file ({SettingsFilePath}) not found. Using defaults and creating file.");
                SaveSettings(settings);
            }

            AppConfig.UpdateRuntimeDefaults(settings);
            Theme.Load(settings.Theme);
        }

        private static void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save initial default settings to {SettingsFilePath}: {ex.Message}");
            }
        }

        private static async Task LoadAccounts(AccountManager accountManager)
        {
            if (File.Exists(AccountsFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(AccountsFilePath);
                    var accounts = JsonConvert.DeserializeObject<List<Account>>(json);
                    if (accounts != null)
                    {
                        accountManager.ClearAccounts();
                        accountManager.AddAccounts(accounts);
                        Logger.LogSuccess($"Loaded {accounts.Count} accounts from {AccountsFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to load accounts from {AccountsFilePath}: {ex.Message}");
                }
            }
        }

        private static async Task SaveAccounts(AccountManager accountManager)
        {
            try
            {
                var accounts = accountManager.GetAllAccounts();
                string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
                await File.WriteAllTextAsync(AccountsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save accounts on exit: {ex.Message}");
            }
        }

        private static async Task RefreshAllTokensOnLaunch(AccountManager accountManager)
        {
            var accounts = accountManager.GetAllAccounts().Where(a => a.IsValid).ToList();
            if (accounts.Count == 0) return;

            Logger.NewLine();
            Logger.LogInfo($"Performing startup XCSRF token refresh for {accounts.Count} valid account(s)...");

            int successCount = 0;
            int failCount = 0;

            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[{Theme.Current.Success}]Refreshing Tokens[/]", new ProgressTaskSettings { MaxValue = accounts.Count });
                    foreach (var acc in accounts)
                    {
                        task.Description = $"[{Theme.Current.Warning}]Checking:[/] [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]";
                        bool refreshed = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(acc);
                        if (refreshed && acc.IsValid)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref failCount);
                        }
                        task.Increment(1);
                        await Task.Delay(100);
                    }
                });
            Logger.LogInfo($"Token refresh complete. Success: [{Theme.Current.Success}]{successCount}[/], Failed: [{Theme.Current.Error}]{failCount}[/]");
        }

        public static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try { Console.Title = "✦ Continuance ✦"; } catch { }
            Logger.Initialize();

            AccountManager? accountManager = null;
            try
            {
                IntPtr handle = GetConsoleWindow();
                IntPtr sysMenu = GetSystemMenu(handle, false);

                if (handle != IntPtr.Zero)
                {
                    _ = DeleteMenu(sysMenu, SC_MAXIMIZE, MF_BYCOMMAND);
                    _ = DeleteMenu(sysMenu, SC_SIZE, MF_BYCOMMAND);
                }
            }
            catch { }

            try
            {
                Logger.LogHeader(" ✦ Continuance ✦ ");
                Logger.LogInfo("Initializing Application Components...");

                LoadSettings();
                await WebDriverManager.EnsureBrowserDownloadedAsync();

                var robloxHttpClient = new RobloxHttpClient();
                var authService = new AuthenticationService(robloxHttpClient);
                var userService = new UserService(robloxHttpClient);
                var avatarService = new AvatarService(robloxHttpClient);
                var groupService = new GroupService(robloxHttpClient);
                var friendService = new FriendService(robloxHttpClient);
                var badgeService = new BadgeService(robloxHttpClient);
                var webDriverManager = new WebDriverManager();
                var gameLauncher = new GameLauncher(authService, badgeService);

                accountManager = new AccountManager();
                await LoadAccounts(accountManager);

                var accountImporter = new AccountImporter(accountManager, authService);
                var accountSelector = new AccountSelector(accountManager);
                var actionExecutor = new AccountActionExecutor(accountManager);

                var actionsMenu = new ActionsMenu(
                    accountManager,
                    accountSelector,
                    actionExecutor,
                    userService,
                    avatarService,
                    gameLauncher,
                    badgeService,
                    friendService,
                    webDriverManager,
                    authService);

                var mainMenu = new MainMenu(
                    accountManager,
                    accountImporter,
                    accountSelector,
                    actionsMenu);

                Logger.LogSuccess("Initialization Complete.");

                await RefreshAllTokensOnLaunch(accountManager);

                _bgService = new BackgroundKeepAliveService(accountManager, authService);
                _bgService.Start();

                Logger.LogInfo("Clearing console and launching Main Menu...");
                await Task.Delay(2500);
                Console.Clear();

                await mainMenu.Show();
            }
            catch (Exception ex)
            {
                Console.Clear();
                Logger.LogError("A critical, unhandled error occurred and the application must close.");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
            finally
            {
                if (_bgService != null) await _bgService.StopAsync();

                if (accountManager != null)
                {
                    Logger.LogInfo("Attempting to save accounts before exiting...");
                    await SaveAccounts(accountManager);
                }
                Console.WriteLine();
                Logger.LogWarning("Application shutting down. Press Enter to close window.");
                Console.ReadLine();
            }
        }
    }
}