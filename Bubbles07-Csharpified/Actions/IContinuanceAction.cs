using Continuance.Models;

namespace Continuance.Actions
{
    public interface IContinuanceAction
    {
        Task<(bool Success, bool Skipped)> ExecuteAsync(Account account, CancellationToken cancellationToken);
    }
}