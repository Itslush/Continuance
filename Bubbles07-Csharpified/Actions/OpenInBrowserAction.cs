using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Automation;
using PuppeteerSharp;
using Spectre.Console;

namespace Continuance.Actions
{
    public class OpenInBrowserAction(WebDriverManager webDriverManager) : IContinuanceAction
    {
        private readonly WebDriverManager _webDriverManager = webDriverManager ?? throw new ArgumentNullException(nameof(webDriverManager));

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            Logger.LogDefault($"   Initiating browser session for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]...");
            cancellationToken.ThrowIfCancellationRequested();

            IBrowser? browser = await WebDriverManager.StartBrowserWithCookie(acc, AppConfig.HomePageUrl, headless: false);

            if (browser == null)
            {
                Logger.LogError("Failed to launch browser session.");
                return (false, false);
            }
            else
            {
                Logger.LogSuccess($"Browser session initiated for {Markup.Escape(acc.Username)}.");
                Logger.LogInfo("The browser will close when you close this application or the window.");

                return (true, false);
            }
        }
    }
}