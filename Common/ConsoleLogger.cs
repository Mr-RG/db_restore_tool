using System;
using System.Linq;
using System.Threading.Tasks;

namespace db_restore_tool
{
    public interface IConsoleLogger
    {
        void LogHeader();
        void LogStep(int stepNumber, string description);
        void LogDetail(string label, string value);
        void LogStatus(string status, bool isSuccess = true);
        void LogWarning(string message);
        void LogError(string message);
        void LogSummary(string dbName, string size, string time);
        void RenderProgressBar(int percent);
        void ClearProgressBar();
        string PromptForAction(string promptMessage, string[] validOptions);
        string PromptForString(string promptMessage);
        Task CopyToClipboardAsync(string text);
    }

    public class ConsoleLogger : IConsoleLogger
    {
        private int _lastPercent = -1;
        private bool _progressBarActive = false;

        public void LogHeader()
        {
            Console.WriteLine(new string('-', 50));
            Console.WriteLine("## Database Restore Utility");
            Console.WriteLine();
        }

        public void LogStep(int stepNumber, string description)
        {
            ClearProgressBar();
            Console.WriteLine($"{description}");
        }

        public void LogDetail(string label, string value)
        {
            ClearProgressBar();
            Console.WriteLine($"{label.PadRight(14)}: {value}");
        }

        public void LogStatus(string status, bool isSuccess = true)
        {
            ClearProgressBar();
            Console.WriteLine($"Status        : {status}");
            Console.WriteLine();
        }

        public void LogWarning(string message)
        {
            ClearProgressBar();
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNING: {message}");
            Console.ForegroundColor = originalColor;
            Console.WriteLine();
        }

        public void LogError(string message)
        {
            ClearProgressBar();
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: {message}");
            Console.ForegroundColor = originalColor;
            Console.WriteLine();
        }

        public void LogSummary(string dbName, string size, string time)
        {
            ClearProgressBar();
            Console.WriteLine(new string('-', 50));
            Console.WriteLine("## Restore Completed");
            Console.WriteLine();
            Console.WriteLine($"Database Name : {dbName}");
            Console.WriteLine($"Database Size : {size}");
            Console.WriteLine($"Restore Time  : {time}");
            Console.WriteLine();
        }

        public void RenderProgressBar(int percent)
        {
            // Only update if percentage has changed to avoid flickering
            if (percent == _lastPercent) return;
            _lastPercent = percent;

            // Constrain between 0-100
            percent = Math.Max(0, Math.Min(100, percent));

            int totalBars = 30; // Length of the bar
            int filledBars = (int)Math.Round((percent / 100.0) * totalBars);
            int emptyBars = totalBars - filledBars;

            string bar = new string('=', Math.Max(0, filledBars - 1)) + (filledBars > 0 && percent < 100 ? ">" : (percent == 100 ? "=" : "")) + new string(' ', emptyBars);
            
            Console.Write($"\r[{bar}] {percent}%");
            _progressBarActive = true;

            if (percent == 100)
            {
                Console.WriteLine(); // New line when complete
                _progressBarActive = false;
                _lastPercent = -1;
            }
        }

        public void ClearProgressBar()
        {
            if (_progressBarActive)
            {
                Console.Write("\r" + new string(' ', 50) + "\r");
                _progressBarActive = false;
                _lastPercent = -1;
            }
        }
        public string PromptForAction(string promptMessage, string[] validOptions)
        {
            ClearProgressBar();
            while (true)
            {
                Console.Write($"{promptMessage} ");
                var input = Console.ReadLine()?.Trim();
                if (validOptions.Contains(input, StringComparer.OrdinalIgnoreCase))
                {
                    return input;
                }
            }
        }

        public string PromptForString(string promptMessage)
        {
            ClearProgressBar();
            Console.Write($"{promptMessage} ");
            return Console.ReadLine()?.Trim();
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
            catch
            {
                // Ignored if clipboard fails
            }
        }
    }
}
