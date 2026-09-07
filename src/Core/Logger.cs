using System;
using System.IO;
using System.Text;

namespace OmniHidTaskbar.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Diagnostic Logging & File Rotation Subsystem
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thread-safe diagnostics logging engine with file rotation, console mirroring,
    /// and dynamic debug activation via flags, executable name, or registry settings.
    /// </summary>
    public static class Logger
    {
        private static readonly object _logLock = new object();
        private static string _resolvedLogFilePath;

        /// <summary>
        /// Gets the resolved absolute path to <c>debug.log</c>, prioritizing the application directory
        /// in portable mode or falling back to user <c>%APPDATA%\OmniHid</c> when installed in write-protected paths.
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                if (_resolvedLogFilePath == null)
                {
                    _resolvedLogFilePath = ResolveLogFilePath();
                }
                return _resolvedLogFilePath;
            }
        }

        private static string ResolveLogFilePath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (IsDirectoryWritable(baseDir))
                {
                    return Path.Combine(baseDir, "debug.log");
                }
            }
            catch { }

            try
            {
                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniHid");
                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }
                return Path.Combine(appDataFolder, "debug.log");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
            }
        }

        /// <summary>
        /// Probes whether a specific directory is writable under current user security permissions.
        /// </summary>
        public static bool IsDirectoryWritable(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return false;
            try
            {
                string testFile = Path.Combine(directoryPath, ".write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private const long MaxLogFileSizeBytes = 1024 * 1024; // 1 MB limit per log file
        private const int MaxLogFileBackups = 2; // Retain at most debug.log.1 and debug.log.2

        private static bool? _isDebugLoggingEnabled = null;

        /// <summary>
        /// Gets or sets whether diagnostic log statements are written to stdout and <c>debug.log</c>.
        /// </summary>
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

        // ═══════════════════════════════════════════════════════════════════════
        // Diagnostics Activation Detection
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Inspects command-line arguments and binary filename to decide whether debug logging is active.
        /// </summary>
        /// <returns><c>true</c> if debug output is requested; otherwise <c>false</c>.</returns>
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
            }
            catch { }
            return false;
#endif
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Log Dispatch & Rotation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Formats and persists a timestamped log entry to console and log file if logging is enabled.
        /// </summary>
        /// <param name="message">The informative log message.</param>
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

        /// <summary>
        /// Rotates the active log file into generation backups (.1, .2) when exceeding <see cref="MaxLogFileSizeBytes"/>.
        /// </summary>
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
