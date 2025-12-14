using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Spectre.Console;

namespace Continuance.Actions
{
    public class LaunchGameAction(GameLauncher gameLauncher, string gameId) : IContinuanceAction
    {
        private readonly GameLauncher _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
        private readonly string _gameId = gameId;

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_gameId))
            {
                Logger.LogError("Skipping LaunchGame: No valid Game ID provided.");
                return (false, true);
            }

            Logger.LogDefault($"   Attempting to launch game [{Theme.Current.Warning}]{Markup.Escape(_gameId)}[/] for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]...");
            cancellationToken.ThrowIfCancellationRequested();
            bool launchInitiatedSuccessfully = await GameLauncher.LaunchGameAsync(acc, _gameId);

            if (launchInitiatedSuccessfully)
            {
                Logger.LogSuccess($"Game launch sequence reported as initiated successfully for {Markup.Escape(acc.Username)}.");
                return (true, false);
            }
            else
            {
                Logger.LogError($"Game launch sequence failed to initiate for {Markup.Escape(acc.Username)}.");
                return (false, false);
            }
        }
    }
}