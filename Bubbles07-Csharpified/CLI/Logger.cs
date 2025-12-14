using Spectre.Console;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Continuance.CLI
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Accent,
        Muted,
        Header,
        Prompt,
        Default
    }

    public record LogEntry(DateTime Timestamp, LogLevel Level, string Message);

    public static class Logger
    {
        private static readonly ConcurrentQueue<LogEntry> _logHistory = new();

        #pragma warning disable SYSLIB1045
        private static readonly Regex _markupDetector = new(@"\[[a-zA-Z0-9#]+(?:=.*?)?\].*?\[/\]", RegexOptions.Compiled);
        #pragma warning restore SYSLIB1045

        private static string? _currentSessionLogPath;
        private static readonly object _fileLock = new();

        public static void Initialize()
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string fileName = $"Session_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
                _currentSessionLogPath = Path.Combine(logDirectory, fileName);

                File.WriteAllText(_currentSessionLogPath, $"--- Continuance Session Log Started: {DateTime.Now} ---\n\n");
            }
            catch { }
        }

        private static void Log(LogLevel level, string message)
        {
            var timestamp = DateTime.Now;
            var entry = new LogEntry(timestamp, level, message);
            _logHistory.Enqueue(entry);

            string consoleTimestamp = $"[[{timestamp:HH:mm:ss}]]";
            string symbol = GetSymbolForLevel(level);
            string color = GetColorForLevel(level);

            string prefixMarkup = $"[{color}]{Markup.Escape(symbol)}[/]";

            bool isMarkup = _markupDetector.IsMatch(message);
            string messageMarkup = isMarkup ? message : Markup.Escape(message);

            string finalMessageMarkup = level == LogLevel.Default || level == LogLevel.Muted
                ? messageMarkup
                : $"[{color}]{messageMarkup}[/]";

            string consoleOutput = $"[{Theme.Current.Muted}]{consoleTimestamp}[/] :: {prefixMarkup} {finalMessageMarkup}";
            AnsiConsole.MarkupLine(consoleOutput);

            WriteToFile(entry);
        }

        private static void WriteToFile(LogEntry entry)
        {
            if (string.IsNullOrEmpty(_currentSessionLogPath)) return;

            string cleanMessage = Markup.Remove(entry.Message);


            string levelString = $"[{entry.Level.ToString().ToUpper()}]".PadRight(9);
            string fileLine = $"[{entry.Timestamp:HH:mm:ss}] {levelString} ::" +
                $" {cleanMessage}";

            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(_currentSessionLogPath, fileLine + Environment.NewLine);
                }
                catch { }
            }
        }

        private static string GetSymbolForLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Success => "[+]",
                LogLevel.Error => "[!]",
                LogLevel.Warning => "[?]",
                LogLevel.Info => "[*]",
                LogLevel.Accent => "[->]",
                LogLevel.Header => "[#]",
                _ => "   "
            };
        }

        public static void LogInfo(string message) => Log(LogLevel.Info, message);
        public static void LogSuccess(string message) => Log(LogLevel.Success, message);
        public static void LogWarning(string message) => Log(LogLevel.Warning, message);
        public static void LogError(string message) => Log(LogLevel.Error, message);
        public static void LogAccent(string message) => Log(LogLevel.Accent, message);
        public static void LogMuted(string message) => Log(LogLevel.Muted, message);
        public static void LogHeader(string message) => Log(LogLevel.Header, message);
        public static void LogPrompt(string message) => Log(LogLevel.Prompt, message);
        public static void LogDefault(string message) => Log(LogLevel.Default, message);
        public static void NewLine()
        {
            AnsiConsole.WriteLine();

            lock (_fileLock)
            {
                try { if (_currentSessionLogPath != null) File.AppendAllText(_currentSessionLogPath, Environment.NewLine); } catch { }
            }
        }

        public static void ClearHistory()
        {
            _logHistory.Clear();
        }

        public static async Task ExportLogHistoryAsync(string filePath)
        {
            if (string.IsNullOrEmpty(_currentSessionLogPath) || !File.Exists(_currentSessionLogPath))
            {
                LogError("No active log file to export.");
                return;
            }

            try
            {
                File.Copy(_currentSessionLogPath, filePath, true);
                LogSuccess($"Successfully saved copy of log to: {filePath}");
            }
            catch (Exception ex)
            {
                LogError($"An unexpected error occurred during log export: {ex.Message}");
            }
        }

        private static string GetColorForLevel(LogLevel level) => level switch
        {
            LogLevel.Info => Theme.Current.Info,
            LogLevel.Success => Theme.Current.Success,
            LogLevel.Warning => Theme.Current.Warning,
            LogLevel.Error => Theme.Current.Error,
            LogLevel.Accent => Theme.Current.Accent1,
            LogLevel.Muted => Theme.Current.Muted,
            LogLevel.Header => Theme.Current.Header,
            LogLevel.Prompt => Theme.Current.Prompt,
            _ => "default"
        };
    }
}