using Spectre.Console;

namespace Continuance.CLI
{
    public static class ConsoleUI
    {
        public static string Truncate(string? value, int maxLength = 150)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            maxLength = Math.Max(0, maxLength);

            if (value.Length <= maxLength)
            {
                return Markup.Escape(value);
            }
            else
            {
                return Markup.Escape(value[..maxLength]) + "...";
            }
        }

        public static string GetCookieHash(string? cookieValue)
        {
            if (string.IsNullOrWhiteSpace(cookieValue))
            {
                return "[red]N/A[/]";
            }
            var parts = cookieValue.Split("|_");

            return parts.LastOrDefault() ?? cookieValue;
        }

        public static string GetShortCookieHash(string? cookieValue)
        {
            if (string.IsNullOrWhiteSpace(cookieValue)) return "[red]N/A[/]";

            string hash = GetCookieHash(cookieValue);
            const int length = 8;

            if (hash.Length > (length * 2))
            {
                return $"{hash[..length]}...{hash[^length..]}";
            }
            return hash;
        }
    }
}