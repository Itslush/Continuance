using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Services;
using Spectre.Console;

namespace Continuance.Actions
{
    public class SetAvatarAction(AvatarService avatarService, long targetUserId) : IContinuanceAction
    {
        private readonly AvatarService _avatarService = avatarService ?? throw new ArgumentNullException(nameof(avatarService));
        private static AvatarDetails? _targetAvatarDetailsCache;
        private static long _targetAvatarCacheSourceId = -1;
        private static readonly object _avatarCacheLock = new();
        private static async Task<AvatarDetails?> GetOrFetchTargetAvatarDetailsAsync(long sourceUserId, CancellationToken cancellationToken)
        {
            lock (_avatarCacheLock)
            {
                if (_targetAvatarDetailsCache != null && _targetAvatarCacheSourceId == sourceUserId)
                {
                    return _targetAvatarDetailsCache;
                }
            }

            Logger.LogDefault($"   [[*]] Fetching target avatar details from User ID [{Theme.Current.Warning}]{sourceUserId}[/] for comparison/cache...");
            cancellationToken.ThrowIfCancellationRequested();
            var fetchedDetails = await AvatarService.FetchAvatarDetailsAsync(sourceUserId);

            if (fetchedDetails != null)
            {
                lock (_avatarCacheLock)
                {
                    _targetAvatarDetailsCache = fetchedDetails;
                    _targetAvatarCacheSourceId = sourceUserId;
                    Logger.LogDefault($"   [[+]] Target avatar details cached successfully for [{Theme.Current.Warning}]{sourceUserId}[/].");
                }
                return fetchedDetails;
            }
            else
            {
                Logger.LogError($"Failed to fetch target avatar details for comparison ({sourceUserId}). Cannot perform pre-check.");
                lock (_avatarCacheLock) { _targetAvatarDetailsCache = null; _targetAvatarCacheSourceId = -1; }
                return null;
            }
        }

        public async Task<(bool Success, bool Skipped)> ExecuteAsync(Account acc, CancellationToken cancellationToken)
        {
            if (targetUserId <= 0)
            {
                Logger.LogWarning($"Skipping SetAvatar: No valid targetUserId ({targetUserId}) provided.");
                return (false, true);
            }
            Logger.LogDefault($"   Checking current avatar for [{Theme.Current.Info}]{Markup.Escape(acc.Username)}[/] against target [{Theme.Current.Warning}]{targetUserId}[/]...");

            AvatarDetails? targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(targetUserId, cancellationToken);
            if (targetAvatarDetails == null)
            {
                Logger.LogError($"Critical Error: Could not get target avatar details for {targetUserId}. Cannot perform check or set avatar.");
                return (false, false);
            }

            Logger.LogInfo($"Fetching current avatar details for {Markup.Escape(acc.Username)}...");
            cancellationToken.ThrowIfCancellationRequested();
            AvatarDetails? currentAvatarDetails = await AvatarService.FetchAvatarDetailsAsync(acc.UserId);

            if (currentAvatarDetails == null)
            {
                Logger.LogWarning($"Failed to fetch current avatar details for {Markup.Escape(acc.Username)}. Proceeding with set attempt...");
                bool setResult = await AvatarService.SetAvatarAsync(acc, targetUserId);
                if (setResult) { Logger.LogSuccess("Avatar set successfully (blind attempt)."); }
                else { Logger.LogError("Avatar set failed (blind attempt)."); }
                return (setResult, false);
            }
            else
            {
                bool match = AvatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails);
                if (match)
                {
                    Logger.LogWarning($"Skipping SetAvatar: Current avatar already matches target {targetUserId}.");
                    return (true, true);
                }
                else
                {
                    Logger.LogInfo("Current avatar differs from target. Attempting update...");
                    bool setResult = await AvatarService.SetAvatarAsync(acc, targetUserId);
                    if (setResult) { Logger.LogSuccess("Avatar set successfully."); }
                    else { Logger.LogError("Avatar set failed."); }
                    return (setResult, false);
                }
            }
        }
    }
}