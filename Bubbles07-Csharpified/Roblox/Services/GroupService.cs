using Continuance.Roblox.Http;


namespace Continuance.Roblox.Services
{
    public class GroupService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
    }
}