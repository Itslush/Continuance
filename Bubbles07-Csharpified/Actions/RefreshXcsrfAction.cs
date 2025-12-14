using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Actions
{
    public class RefreshXcsrfAction(AuthenticationService authService) : IContinuanceAction
    {
        private readonly AuthenticationService _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            Logger.LogDefault($"   Attempting to refresh XCSRF token for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/]...");
            cancellationToken.ThrowIfCancellationRequested();

            if (!acc.IsValid || string.IsNullOrEmpty(acc.Cookie))
            {
                Logger.LogWarning($"Skipping refresh: Account is marked invalid or has no cookie.");
                return (false, true);
            }

            bool refreshSuccess = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(acc);

            if (refreshSuccess && acc.IsValid && !string.IsNullOrEmpty(acc.XcsrfToken))
            {
                Logger.LogSuccess($"XCSRF token is present and valid for {Markup.Escape(acc.Username)}.");
                return (true, false);
            }
            else
            {
                Logger.LogError($"Failed to refresh or validate XCSRF token for {Markup.Escape(acc.Username)}. The account may now be invalid.");
                return (false, false);
            }
        }
    }
}