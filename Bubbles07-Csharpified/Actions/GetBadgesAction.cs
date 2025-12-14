using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Actions
{
    public class GetBadgesAction(GameLauncher gameLauncher, BadgeService badgeService, int badgeGoal, string gameId) : IContinuanceAction
    {
        private readonly GameLauncher _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
        private readonly BadgeService _badgeService = badgeService ?? throw new ArgumentNullException(nameof(badgeService));
        private readonly int _badgeGoal = badgeGoal;
        private readonly string _gameId = gameId;

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            if (_badgeGoal <= 0)
            {
                Logger.LogWarning("Skipping GetBadges: Badge goal is zero or negative.");
                return (true, true);
            }
            if (string.IsNullOrWhiteSpace(_gameId))
            {
                Logger.LogError("Skipping GetBadges: No valid Game ID provided.");
                return (false, true);
            }

            Logger.LogDefault($"   Checking current badge count for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] (Goal: >= [{Theme.Current.Warning}]{_badgeGoal}[/])...");

            cancellationToken.ThrowIfCancellationRequested();
            int apiLimitForCheck = _badgeGoal <= 10 ? 10 : _badgeGoal <= 25 ? 25 : _badgeGoal <= 50 ? 50 : 100;
            int currentBadgeCount = await BadgeService.GetBadgeCountAsync(acc, limit: apiLimitForCheck);

            if (currentBadgeCount == -1)
            {
                Logger.LogWarning("Failed to fetch current badge count. Will proceed with game launch attempt anyway...");
            }
            else if (currentBadgeCount >= _badgeGoal)
            {
                Logger.LogWarning($"Skipping GetBadges: Account already has {currentBadgeCount} (>= {_badgeGoal}) recent badges (checked up to {apiLimitForCheck}).");
                return (true, true);
            }
            else
            {
                Logger.LogDefault($"   Current badge count is [{Theme.Current.Warning}]{currentBadgeCount}[/] (< {_badgeGoal}). Needs game launch (checked up to {apiLimitForCheck}).");
            }

            Logger.LogDefault($"   Attempting to launch game [{Theme.Current.Warning}]{Markup.Escape(_gameId)}[/]...");
            cancellationToken.ThrowIfCancellationRequested();

            bool launchInitiatedSuccessfully = await GameLauncher.LaunchGameForBadgesAsync(acc, _gameId, _badgeGoal);

            if (launchInitiatedSuccessfully)
            {
                Logger.LogSuccess($"Game launch sequence reported as initiated successfully for {Markup.Escape(acc.Username)}.");
                return (true, false);
            }
            else
            {
                Logger.LogError($"Game launch sequence failed to initiate for {Markup.Escape(acc.Username)} (e.g., auth ticket failed).");
                return (false, false);
            }
        }
    }
}