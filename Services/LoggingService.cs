using System;
using System.IO;

namespace LazerNi.Services
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
        }
    }

    public class LoggingService
    {
        private readonly string _logDirectory = "Logs";
        private readonly string _logFilePath;
        private readonly object _lockObject = new();

        public event EventHandler<LogEntry>? LogEntryAdded;

        public LoggingService()
        {
            // Create logs directory
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            _logFilePath = Path.Combine(_logDirectory, $"lazerni_{DateTime.Now:yyyyMMdd}.log");
        }

        public void Log(LogLevel level, string message)
        {
            lock (_lockObject)
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message
                };

                // Write to console
                var color = level switch
                {
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Debug => ConsoleColor.Gray,
                    _ => ConsoleColor.White
                };

                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(entry.ToString());
                Console.ForegroundColor = originalColor;

                // Write to file
                try
                {
                    File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // Can't log the error, just print to console
                    Console.WriteLine($"Failed to write to log file: {ex.Message}");
                }

                // Raise event for GUI
                LogEntryAdded?.Invoke(this, entry);
            }
        }

        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
    }
}
