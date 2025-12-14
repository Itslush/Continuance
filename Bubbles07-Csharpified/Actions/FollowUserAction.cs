using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Spectre.Console;

namespace Continuance.Actions
{
    public class FollowUserAction(GameLauncher gameLauncher, long targetUserId) : IContinuanceAction
    {
        private readonly GameLauncher _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
        private readonly long _targetUserId = targetUserId;

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            if (_targetUserId <= 0)
            {
                Logger.LogError("Skipping FollowUser: No valid User ID provided.");
                return (false, true);
            }

            Logger.LogDefault($"   Attempting to follow user ID [{Theme.Current.Warning}]{_targetUserId}[/] with [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]...");

            cancellationToken.ThrowIfCancellationRequested();

            bool launchSuccess = await GameLauncher.LaunchFollowUserAsync(acc, _targetUserId);

            if (launchSuccess)
            {
                Logger.LogSuccess($"Follow launch initiated for {Markup.Escape(acc.Username)}.");
                return (true, false);
            }
            else
            {
                Logger.LogError($"Failed to initiate follow launch for {Markup.Escape(acc.Username)}.");
                return (false, false);
            }
        }
    }
}