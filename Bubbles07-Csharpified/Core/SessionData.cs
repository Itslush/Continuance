using Continuance.Models;

namespace Continuance.Core
{
    public static class SessionData
    {
        public static List<string> LastImportFailedCookies { get; set; } = [];
        public static List<Account> LastVerificationFailedAccounts { get; set; } = [];
    }
}