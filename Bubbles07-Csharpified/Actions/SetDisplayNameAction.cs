using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Actions
{
    public class SetDisplayNameAction(UserService userService, string targetName) : IContinuanceAction
    {
        private readonly UserService _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        private readonly string _targetName = targetName;

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            Logger.LogDefault($"Checking current display name for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]...");

            cancellationToken.ThrowIfCancellationRequested();
            string? currentName = await UserService.GetCurrentDisplayNameAsync(acc);

            if (currentName == null)
            {
                Logger.LogWarning("Failed to fetch current display name. Proceeding with set attempt...");
                bool setResult = await UserService.SetDisplayNameAsync(acc, _targetName);
                if (setResult) { Logger.LogSuccess("Display name set successfully (blind attempt)."); }
                else { Logger.LogError("Display name set failed (blind attempt)."); }
                return (setResult, false);
            }
            else if (string.Equals(currentName, _targetName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning($"Skipping SetDisplayName: Already set to '{Markup.Escape(_targetName)}'.");
                return (true, true);
            }
            else
            {
                Logger.LogDefault($"Current name is '[{Theme.Current.Warning}]{Markup.Escape(currentName)}[/]'. Attempting update to '[{Theme.Current.Warning}]{Markup.Escape(_targetName)}[/]'...");
                bool setResult = await UserService.SetDisplayNameAsync(acc, _targetName);
                if (setResult) { Logger.LogSuccess("Display name set successfully."); }
                else { Logger.LogError("Display name set failed."); }
                return (setResult, false);
            }
        }
    }
}