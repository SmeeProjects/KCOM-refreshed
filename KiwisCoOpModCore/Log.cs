/*
    Kiwi's Co-Op Mod for Half-Life: Alyx
    Copyright (c) 2022 KiwifruitDev
    All rights reserved.
    This software is licensed under the MIT License.
    -----------------------------------------------------------------------------
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    -----------------------------------------------------------------------------
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace KiwisCoOpModCore
{
    public enum LogLevel
    {
        Warning = 0,
        Error
    }
    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string source, string message, Exception? exception)
        {
            Timestamp = timestamp;
            Level = level;
            Source = source;
            Message = message;
            Exception = exception;
        }
        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Source { get; }
        public string Message { get; }
        public Exception? Exception { get; }
        // One line for the UI: the full exception only goes to the log file.
        public string Summary => Exception == null ? Source + ": " + Message : Source + ": " + Message + " (" + Exception.GetType().Name + ": " + Exception.Message + ")";
        public override string ToString()
        {
            StringBuilder builder = new();
            builder.Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(' ').Append(Level.ToString().ToUpperInvariant());
            builder.Append(" [").Append(Source).Append("] ").Append(Message);
            if (Exception != null)
                builder.AppendLine().Append(Exception);
            return builder.ToString();
        }
    }
    // Writes log entries to logs/kcom-<date>.log, starting a new file each day or when one gets
    // too big, and keeping only the newest files (UPGRADE_PLAN.md B5).
    public sealed class RollingFileLog
    {
        private readonly object fileLock = new();
        private readonly string directory;
        private readonly long maxFileBytes;
        private readonly int maxFiles;
        private string? currentPath;
        public RollingFileLog(string directory, long maxFileBytes = 5 * 1024 * 1024, int maxFiles = 10)
        {
            this.directory = directory;
            this.maxFileBytes = maxFileBytes;
            this.maxFiles = maxFiles;
        }
        public string Directory => directory;
        public void Write(LogEntry entry)
        {
            lock (fileLock)
            {
                try
                {
                    string path = GetPath(entry.Timestamp);
                    File.AppendAllText(path, entry + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    // Nowhere left to report it: the log file itself is what failed.
                    Debug.WriteLine(e);
                }
            }
        }
        private string GetPath(DateTime timestamp)
        {
            System.IO.Directory.CreateDirectory(directory);
            string prefix = "kcom-" + timestamp.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            // Continue the day's newest file (kcom-<date>.log, then .1.log, .2.log…), or start the next one when it is full.
            // Going by the highest number keeps a deleted older file from being reused.
            int index = new DirectoryInfo(directory).GetFiles(prefix + "*.log")
                .Select(f => f.Name == prefix + ".log" ? 0 : int.TryParse(f.Name[(prefix.Length + 1)..^4], out int i) ? i : -1)
                .DefaultIfEmpty(0)
                .Max();
            string path = PathFor(prefix, index);
            if (File.Exists(path) && new FileInfo(path).Length >= maxFileBytes)
                path = PathFor(prefix, index + 1);
            if (path != currentPath)
            {
                currentPath = path;
                DeleteOldFiles(path);
            }
            return path;
        }
        private string PathFor(string prefix, int index)
        {
            return Path.Combine(directory, index == 0 ? prefix + ".log" : prefix + "." + index + ".log");
        }
        private void DeleteOldFiles(string keep)
        {
            IEnumerable<FileInfo> oldFiles = new DirectoryInfo(directory).GetFiles("kcom-*.log")
                .Where(f => !string.Equals(f.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(maxFiles - 1);
            foreach (FileInfo file in oldFiles)
                file.Delete();
        }
    }
    // Where gamemodes, plugins and the app report the errors they used to swallow (UPGRADE_PLAN.md B5).
    // Entries go to the log file and to Logged, which the UI shows in its "Errors" tab.
    public static class Log
    {
        private static readonly object throttleLock = new();
        private static readonly Dictionary<string, (DateTime first, int suppressed)> recent = new();
        // The same error repeated within this window (a broken Think handler, say) is only counted.
        public static TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromSeconds(10);
        public static RollingFileLog FileLog { get; set; } = new(Path.Combine(AppContext.BaseDirectory, "logs"));
        public static event Action<LogEntry>? Logged;
        public static void Warning(string source, string message, Exception? exception = null, bool notify = true)
        {
            Write(LogLevel.Warning, source, message, exception, notify);
        }
        public static void Error(string source, Exception exception, string? message = null, bool notify = true)
        {
            Write(LogLevel.Error, source, message ?? "Unhandled exception", exception, notify);
        }
        public static void Write(LogLevel level, string source, string message, Exception? exception = null, bool notify = true)
        {
            DateTime now = DateTime.Now;
            string key = level + "|" + source + "|" + message + "|" + exception?.GetType().FullName + "|" + exception?.Message;
            lock (throttleLock)
            {
                if (recent.TryGetValue(key, out var seen) && now - seen.first < ThrottleWindow)
                {
                    recent[key] = (seen.first, seen.suppressed + 1);
                    return;
                }
                if (seen.suppressed > 0)
                    message += " (repeated " + seen.suppressed + " more times since " + seen.first.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + ")";
                recent[key] = (now, 0);
                // Forget old errors so the table can't grow without bound.
                if (recent.Count > 256)
                    foreach (string old in recent.Where(r => now - r.Value.first >= ThrottleWindow).Select(r => r.Key).ToList())
                        recent.Remove(old);
            }
            LogEntry entry = new(now, level, source, message, exception);
            Debug.WriteLine(entry);
            FileLog.Write(entry);
            if (!notify)
                return;
            try
            {
                Logged?.Invoke(entry);
            }
            catch (Exception e)
            {
                // A failing listener must not hide the original error, which is already in the file.
                FileLog.Write(new LogEntry(DateTime.Now, LogLevel.Error, "Log", "A Log.Logged listener failed", e));
            }
        }
    }
}
