using System;
using System.IO;
using System.Text;

namespace OmniHidTaskbar.Core
{
    public static class Logger
    {
        private static readonly object _logLock = new object();
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        private const long MaxLogFileSizeBytes = 1024 * 1024; // 1 MB limit
        private const int MaxLogFileBackups = 2; // Keep at most debug.log.1 and debug.log.2

        private static bool? _isDebugLoggingEnabled = null;

        public static bool IsDebugLoggingEnabled
        {
            get
            {
                if (!_isDebugLoggingEnabled.HasValue)
                {
                    _isDebugLoggingEnabled = DetermineDebugLogging();
                }
                return _isDebugLoggingEnabled.Value;
            }
            set
            {
                _isDebugLoggingEnabled = value;
            }
        }

        private static bool DetermineDebugLogging()
        {
#if DEBUG_LOG || DEBUG
            return true;
#else
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args != null)
                {
                    for (int i = 1; i < args.Length; i++)
                    {
                        string arg = args[i].Trim().ToLowerInvariant();
                        if (arg == "--debug" || arg == "-debug" || arg == "/debug" || arg == "-d" || arg == "--verbose")
                            return true;
                    }
                }

                string processName = AppDomain.CurrentDomain.FriendlyName;
                if (!string.IsNullOrEmpty(processName) && processName.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // Check registry setting
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\OmniHidTaskbar") ??
                                 Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\OmniHidTaskbar"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("EnableDebugLog");
                        if (val != null && Convert.ToInt32(val) == 1)
                            return true;
                    }
                }
            }
            catch { }
            return false;
#endif
        }

        public static void Log(string message)
        {
            if (!IsDebugLoggingEnabled) return;

            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}", DateTime.Now, message);
            lock (_logLock)
            {
                try
                {
                    Console.WriteLine(line);
                }
                catch { }

                try
                {
                    RotateLogFilesIfNeeded();
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void RotateLogFilesIfNeeded()
        {
            try
            {
                FileInfo fi = new FileInfo(LogFilePath);
                if (fi.Exists && fi.Length >= MaxLogFileSizeBytes)
                {
                    for (int i = MaxLogFileBackups - 1; i >= 1; i--)
                    {
                        string oldFile = LogFilePath + "." + i;
                        string newFile = LogFilePath + "." + (i + 1);
                        if (File.Exists(oldFile))
                        {
                            if (File.Exists(newFile))
                            {
                                try { File.Delete(newFile); } catch { }
                            }
                            try { File.Move(oldFile, newFile); } catch { }
                        }
                    }

                    string firstBackup = LogFilePath + ".1";
                    if (File.Exists(firstBackup))
                    {
                        try { File.Delete(firstBackup); } catch { }
                    }
                    File.Move(LogFilePath, firstBackup);
                }
            }
            catch { }
        }
    }
}
