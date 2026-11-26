using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KiwisCoOpModCore;
using Xunit;

namespace KiwisCoOpModCore.Tests
{
    // Log is static, so every test of it lives in this class: xUnit runs one class's tests one at a time (UPGRADE_PLAN.md B5).
    public sealed class LogTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "kcom-log-tests-" + Guid.NewGuid().ToString("N"));
        private readonly RollingFileLog previousFileLog = Log.FileLog;
        private readonly TimeSpan previousThrottleWindow = Log.ThrottleWindow;
        private readonly List<LogEntry> shown = new();

        public LogTests()
        {
            Log.FileLog = new RollingFileLog(directory, maxFileBytes: 1000, maxFiles: 3);
            Log.Logged += Record;
        }

        public void Dispose()
        {
            Log.Logged -= Record;
            Log.FileLog = previousFileLog;
            Log.ThrottleWindow = previousThrottleWindow;
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private void Record(LogEntry entry) => shown.Add(entry);

        private string[] LogFiles() => Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

        [Fact]
        public void Error_WritesExceptionToFileAndOneLineToListeners()
        {
            Log.Error("Gamemode", new InvalidOperationException("boom"), "Failed to handle Think");

            LogEntry entry = Assert.Single(shown);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Equal("Gamemode: Failed to handle Think (InvalidOperationException: boom)", entry.Summary);
            string text = File.ReadAllText(Path.Combine(directory, Assert.Single(LogFiles())));
            Assert.Contains("ERROR [Gamemode] Failed to handle Think", text);
            Assert.Contains("System.InvalidOperationException: boom", text);
        }

        [Fact]
        public void FileName_AndTimestamp_IgnoreCurrentCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Log.Warning("Culture", "message");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            string name = Assert.Single(LogFiles());
            Assert.Matches(@"^kcom-\d{4}-\d{2}-\d{2}\.log$", name);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} WARNING \[Culture\] message", File.ReadAllText(Path.Combine(directory, name)));
        }

        [Fact]
        public void NotifyFalse_OnlyWritesTheFile()
        {
            Log.Warning("Lua", "already shown by printerr", notify: false);

            Assert.Empty(shown);
            Assert.Single(LogFiles());
        }

        [Fact]
        public void RepeatedError_IsCountedThenReported()
        {
            Log.ThrottleWindow = TimeSpan.FromHours(1);
            for (int i = 0; i < 50; i++)
                Log.Error("Think", new InvalidOperationException("boom"));

            Assert.Single(shown);

            Log.ThrottleWindow = TimeSpan.Zero;
            Log.Error("Think", new InvalidOperationException("boom"));

            Assert.Equal(2, shown.Count);
            Assert.Contains("repeated 49 more times since", shown[1].Message);
        }

        [Fact]
        public void FullFiles_RollOver_AndOnlyTheNewestAreKept()
        {
            Log.ThrottleWindow = TimeSpan.Zero;
            for (int i = 0; i < 200; i++)
                Log.Warning("Roll", "message " + i, notify: false);

            string[] files = LogFiles();
            Assert.Equal(3, files.Length);
            // The first file (no number) was deleted and not reused, and the newest file has the last message.
            Assert.All(files, f => Assert.Matches(@"\.\d+\.log$", f));
            string newest = files.OrderBy(f => int.Parse(f.Split('.')[1], CultureInfo.InvariantCulture)).Last();
            Assert.EndsWith("message 199" + Environment.NewLine, File.ReadAllText(Path.Combine(directory, newest)));
        }

        [Fact]
        public void FailingListener_DoesNotThrow()
        {
            void Throw(LogEntry entry) => throw new InvalidOperationException("listener");
            Log.Logged += Throw;
            try
            {
                Log.Warning("Listener", "message");
            }
            finally
            {
                Log.Logged -= Throw;
            }

            Assert.Contains("A Log.Logged listener failed", File.ReadAllText(Path.Combine(directory, Assert.Single(LogFiles()))));
        }
    }
}
