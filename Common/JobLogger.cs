using System;
using System.Linq;
using System.Threading.Tasks;

namespace db_restore_tool.Common
{
    public class JobLogger : IConsoleLogger
    {
        private readonly object _consoleLock;
        private int _lastPercent = -1;
        private bool _progressBarActive = false;

        public JobLogger(string serverName, object consoleLock)
        {
            _consoleLock = consoleLock;
        }

        public void LogHeader()
        {
            lock (_consoleLock)
            {
                Console.WriteLine("## Database Restore Utility");
                Console.WriteLine();
            }
        }

        public void LogStep(int stepNumber, string description)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                Console.WriteLine(); // Add blank line before steps for clean spacing
                Console.WriteLine(description);
            }
        }

        public void LogDetail(string label, string value)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                Console.WriteLine($"{label.PadRight(14)}: {value}");
            }
        }

        public void LogStatus(string status, bool isSuccess = true)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                Console.WriteLine($"Status        : {status}");
            }
        }

        public void LogWarning(string message)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARNING: {message}");
                Console.ForegroundColor = originalColor;
                Console.WriteLine();
            }
        }

        public void LogError(string message)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: {message}");
                Console.ForegroundColor = originalColor;
                Console.WriteLine();
            }
        }

        public void LogSummary(string dbName, string size, string time)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                Console.WriteLine();
                Console.WriteLine(new string('-', 40));
                Console.WriteLine("## Restore Completed");
                Console.WriteLine($"Database Name : {dbName}");
                Console.WriteLine($"Database Size : {size}");
                Console.WriteLine($"Restore Time  : {time}");
                Console.WriteLine(new string('-', 40));
                Console.WriteLine();
            }
        }

        public void RenderProgressBar(int percent)
        {
            lock (_consoleLock)
            {
                if (percent == _lastPercent) return;
                _lastPercent = percent;

                percent = Math.Max(0, Math.Min(100, percent));

                int totalBars = 30;
                int filledBars = (int)Math.Round((percent / 100.0) * totalBars);
                int emptyBars = totalBars - filledBars;

                string bar = new string('=', Math.Max(0, filledBars - 1)) + (filledBars > 0 && percent < 100 ? ">" : (percent == 100 ? "=" : "")) + new string(' ', emptyBars);
                
                Console.Write($"\r[{bar}] {percent}%");
                _progressBarActive = true;

                if (percent == 100)
                {
                    Console.WriteLine();
                    _progressBarActive = false;
                    _lastPercent = -1;
                }
            }
        }

        public void ClearProgressBar()
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
            }
        }

        private void ClearProgressBarInternal()
        {
            if (_progressBarActive)
            {
                Console.Write("\r" + new string(' ', 60) + "\r");
                _progressBarActive = false;
                _lastPercent = -1;
            }
        }

        public string PromptForAction(string promptMessage, string[] validOptions)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                while (true)
                {
                    Console.WriteLine(promptMessage);
                    var input = Console.ReadLine()?.Trim();
                    if (validOptions.Contains(input, StringComparer.OrdinalIgnoreCase))
                    {
                        return input;
                    }
                }
            }
        }

        public string PromptForString(string promptMessage)
        {
            lock (_consoleLock)
            {
                ClearProgressBarInternal();
                Console.WriteLine(promptMessage);
                return Console.ReadLine()?.Trim();
            }
        }

        public async Task CopyToClipboardAsync(string text)
        {
            try
            {
                string safeText = text.Replace("'", "''");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-command \"Set-Clipboard -Value '{safeText}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(psi)) 
                {
                    await process.WaitForExitAsync();
                }
            }
            catch { }
        }
    }
}
