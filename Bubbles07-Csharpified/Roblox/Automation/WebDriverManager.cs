using PuppeteerSharp;
using Continuance.Models;
using Spectre.Console;
using Continuance.CLI;

namespace Continuance.Roblox.Automation
{
    public class WebDriverManager
    {
        private static bool _browserDownloaded = false;
        private static readonly string[] InteractiveArgs =
        [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--window-size=1280,720"
        ];

        private static readonly string[] HeadlessArgs =
        [
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-extensions",
            "--window-size=1280,720"
        ];

        private static readonly string[] IgnoredArgs = ["--enable-automation"];

        public static async Task EnsureBrowserDownloadedAsync()
        {
            if (_browserDownloaded) return;

            var browserFetcher = new BrowserFetcher();

            Logger.LogInfo("Verifying Chromium browser for automation...");

            await AnsiConsole.Status()
                .StartAsync("Downloading Chromium...", async ctx =>
                {
                    await browserFetcher.DownloadAsync();
                });

            Logger.LogSuccess("Chromium is ready.");
            _browserDownloaded = true;
        }

        public static async Task<string?> CaptureCookieInteractiveAsync()
        {
            await EnsureBrowserDownloadedAsync();
            IBrowser? browser = null;
            try
            {
                Logger.LogInfo("Initializing Interactive Browser...");

                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    DefaultViewport = null,
                    Args = InteractiveArgs,
                    IgnoredDefaultArgs = IgnoredArgs
                });

                var pages = await browser.PagesAsync();
                var page = pages.Length > 0 ? pages[0] : await browser.NewPageAsync();

                Logger.LogInfo("Navigating to Roblox Login...");
                await page.GoToAsync("https://www.roblox.com/login", new NavigationOptions { Timeout = 60000 });

                string? detectedCookie = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Waiting for user to log in...", async ctx =>
                    {
                        while (true)
                        {
                            if (browser.IsClosed) break;

                            try
                            {
                                var cookies = await page.GetCookiesAsync("https://www.roblox.com");
                                var securityCookie = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");

                                if (securityCookie != null && !string.IsNullOrWhiteSpace(securityCookie.Value))
                                {
                                    if (securityCookie.Value.Contains("WARNING"))
                                    {
                                        detectedCookie = securityCookie.Value;
                                        ctx.Status("Login detected! Capturing cookie...");
                                        await Task.Delay(1000);
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                break;
                            }

                            await Task.Delay(1000);
                        }
                    });

                return detectedCookie;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Browser Login Error: {Markup.Escape(ex.Message)}");
                return null;
            }
            finally
            {
                if (browser != null) await browser.CloseAsync();
            }
        }

        public static async Task<IBrowser?> StartBrowserWithCookie(Account account, string url, bool headless = false)
        {
            await EnsureBrowserDownloadedAsync();

            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Logger.LogError($"Cannot start browser for {Markup.Escape(account.Username)}: Account cookie is missing.");
                return null;
            }
            try
            {
                Logger.LogInfo($"Initializing Puppeteer Browser (Headless: {headless})...");

                var launchOptions = new LaunchOptions
                {
                    Headless = headless,
                    DefaultViewport = null,
                    Args = HeadlessArgs,
                    IgnoredDefaultArgs = IgnoredArgs
                };

                IBrowser browser = await Puppeteer.LaunchAsync(launchOptions);
                var pages = await browser.PagesAsync();
                var page = pages.Length > 0 ? pages[0] : await browser.NewPageAsync();

                Logger.LogInfo("Setting authentication cookie...");

                await page.SetCookieAsync(new CookieParam
                {
                    Name = ".ROBLOSECURITY",
                    Value = account.Cookie,
                    Domain = ".roblox.com",
                    Path = "/",
                    Secure = true,
                    HttpOnly = true,
                    SameSite = SameSite.Lax,
                    Expires = DateTimeOffset.Now.AddYears(1).ToUnixTimeSeconds()
                });

                Logger.LogInfo($"Navigating to target URL: {Markup.Escape(url)}");

                // Optimized WaitUntil using collection expression
                await page.GoToAsync(url, new NavigationOptions
                {
                    Timeout = 60000,
                    WaitUntil = [WaitUntilNavigation.DOMContentLoaded]
                });

                try
                {
                    await page.WaitForSelectorAsync("#nav-robux-balance, #nav-username", new WaitForSelectorOptions { Timeout = 15000 });
                    Logger.LogSuccess("Login Confirmed via Page Element.");
                }
                catch (WaitTaskTimeoutException)
                {
                    Logger.LogWarning("Could not confirm successful login via page element. Proceeding anyway.");
                }

                return browser;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Browser Init Error for {Markup.Escape(account.Username)}: {Markup.Escape(ex.Message)}");
                return null;
            }
        }
    }
}