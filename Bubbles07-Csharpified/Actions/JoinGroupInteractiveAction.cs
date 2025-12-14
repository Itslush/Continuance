using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Automation;
using PuppeteerSharp;
using Spectre.Console;

namespace Continuance.Actions
{
    public class JoinGroupInteractiveAction(WebDriverManager webDriverManager, long targetGroupId) : IContinuanceAction
    {
        private readonly WebDriverManager _webDriverManager = webDriverManager ?? throw new ArgumentNullException(nameof(webDriverManager));
        private readonly long _targetGroupId = targetGroupId;

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            if (_targetGroupId <= 0)
            {
                Logger.LogWarning($"Skipping JoinGroup: Invalid targetGroupId ({_targetGroupId}).");
                return (false, true);
            }

            string groupUrl = $"{AppConfig.RobloxWebBaseUrl}/groups/{_targetGroupId}/about";
            Logger.LogDefault($"Initiating browser session for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] to join Group [{Theme.Current.Warning}]{_targetGroupId}[/]...");
            Logger.LogDefault($"Target URL: [link={Markup.Escape(groupUrl)}]{Markup.Escape(groupUrl)}[/]");

            IBrowser? browser = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                browser = await WebDriverManager.StartBrowserWithCookie(acc, groupUrl, headless: false);

                if (browser == null)
                {
                    Logger.LogError("Failed to launch browser session.");
                    return (false, false);
                }

                Logger.LogSuccess($"Browser opened for {Markup.Escape(acc.Username)}.");
                Logger.LogInfo("Please find the 'Join Group' button on the page.");
                Logger.LogInfo("Click it and solve any CAPTCHA that appears in the browser.");
                Logger.LogInfo("Do NOT close the browser window yourself yet.");

                AnsiConsole.Prompt(new TextPrompt<string>("          >> Press Enter in THIS console window once done (or to skip):").AllowEmpty());

                Logger.LogInfo("User confirmed action completed or skipped in browser.");
                return (true, false);
            }
            catch (OperationCanceledException)
            {
                Logger.NewLine();
                Logger.LogError("Action cancelled by user during browser operation.");
                return (false, false);
            }
            catch (Exception ex)
            {
                Logger.LogError($"An error occurred during the interactive join group process: {Markup.Escape(ex.Message)}");
                return (false, false);
            }
            finally
            {
                if (browser != null)
                {
                    Logger.LogInfo($"Attempting to close browser window for {Markup.Escape(acc.Username)}...");
                    try
                    {
                        await browser.CloseAsync();
                        Logger.LogMuted("Browser closed.");
                    }
                    catch (Exception closeEx)
                    {
                        Logger.LogWarning($"Non-critical error closing browser: {Markup.Escape(closeEx.Message)}");
                    }
                }
            }
        }
    }
}