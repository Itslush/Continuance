namespace Continuance.Models
{
    public class AppSettings
    {
        public int ApiDelayMs { get; set; } = AppConfig.DefaultApiDelayMs;
        public int FriendActionDelayMs { get; set; } = AppConfig.DefaultFriendActionDelayMs;
        public int RequestTimeoutSec { get; set; } = AppConfig.DefaultRequestTimeoutSec;
        public int MaxApiRetries { get; set; } = AppConfig.DefaultMaxApiRetries;
        public int ApiRetryDelayMs { get; set; } = AppConfig.DefaultApiRetryDelayMs;
        public string DefaultDisplayName { get; set; } = AppConfig.DefaultDisplayName;
        public long DefaultGroupId { get; set; } = AppConfig.DefaultGroupId;
        public string DefaultBadgeGameId { get; set; } = AppConfig.DefaultBadgeGameId;
        public long DefaultTargetUserIdForAvatarCopy { get; set; } = AppConfig.DefaultTargetUserIdForAvatarCopy;
        public int DefaultFriendGoal { get; set; } = AppConfig.DefaultFriendGoal;
        public int DefaultBadgeGoal { get; set; } = AppConfig.DefaultBadgeGoal;
        public int ActionConfirmationThreshold { get; set; } = 15;
        public int ImportDelayMs { get; set; } = AppConfig.DefaultImportDelayMs;
        public bool HeadlessMode { get; set; } = false;
        public int BackgroundRefreshIntervalMinutes { get; set; } = AppConfig.DefaultBackgroundRefreshIntervalMinutes;
        public ColorPalette Theme { get; set; } = new();
    }
}