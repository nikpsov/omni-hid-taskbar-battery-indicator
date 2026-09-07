using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace OmniHidTaskbar.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Application Configuration Model
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configuration model representing user preferences, display styles,
    /// polling intervals, and device filter lists persisted in <c>settings.json</c>.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Gets or sets the taskbar rendering mode: 0 = Battery percentage (e.g. 85%), 1 = Glyph only.
        /// </summary>
        public int DisplayStyle { get; set; }

        /// <summary>
        /// Gets or sets the visual representation mode: 0 = Floating taskbar overlay widget, 1 = System tray icon only.
        /// </summary>
        public int DisplayMode { get; set; }

        /// <summary>
        /// Gets or sets whether the taskbar overlay automatically hides when all devices are sleeping or disconnected.
        /// </summary>
        public bool HideWhenDisconnected { get; set; }

        /// <summary>
        /// Gets or sets whether the application launches automatically on Windows logon.
        /// </summary>
        public bool RunOnStartup { get; set; }

        /// <summary>
        /// Gets or sets the timer frequency in seconds between peripheral telemetry queries.
        /// </summary>
        public int PollIntervalSeconds { get; set; }

        /// <summary>
        /// Gets or sets the timer frequency in seconds between peripheral telemetry queries in background mode (e.g. fullscreen games, locked session).
        /// </summary>
        public int BackgroundPollIntervalSeconds { get; set; }

        /// <summary>
        /// Gets or sets the collection of peripheral model names or identifiers explicitly hidden by the user.
        /// </summary>
        public List<string> HiddenDevices { get; set; }

        /// <summary>
        /// Gets or sets the ordered collection of device identifiers for custom display ordering.
        /// </summary>
        public List<string> DeviceOrder { get; set; }

        /// <summary>
        /// Gets or sets user-defined friendly alias names mapped by unique device identifier.
        /// </summary>
        public Dictionary<string, string> CustomDeviceNames { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="AppSettings"/> with default options.
        /// </summary>
        public AppSettings()
        {
            DisplayStyle = 0;
            DisplayMode = 0;
            HideWhenDisconnected = true;
            RunOnStartup = false;
            PollIntervalSeconds = 15;
            BackgroundPollIntervalSeconds = 300;
            HiddenDevices = new List<string>();
            DeviceOrder = new List<string>();
            CustomDeviceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Settings Persistence & File Manager
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Singleton manager responsible for loading, parsing, and persisting application configuration.
    /// Supports zero-dependency portable local JSON, %AppData% fallback, and Windows Run registry integration.
    /// </summary>
    public class SettingsManager
    {
        private static SettingsManager _instance;
        private static readonly object _instanceLock = new object();

        /// <summary>
        /// Gets the global singleton instance of <see cref="SettingsManager"/>.
        /// </summary>
        public static SettingsManager Instance
        {
            get
            {
                lock (_instanceLock)
                {
                    if (_instance == null)
                    {
                        _instance = new SettingsManager();
                    }
                    return _instance;
                }
            }
        }

        private readonly object _lock = new object();
        private AppSettings _settings;
        private string _activeSettingsFilePath;

        /// <summary>
        /// Gets the active in-memory settings snapshot, loading it from disk upon first access.
        /// </summary>
        public AppSettings Current
        {
            get
            {
                lock (_lock)
                {
                    if (_settings == null)
                    {
                        _settings = LoadInternal();
                    }
                    return _settings;
                }
            }
        }

        /// <summary>
        /// Forces a reload of settings from persistent storage.
        /// </summary>
        /// <returns>Freshly reloaded <see cref="AppSettings"/> instance.</returns>
        public AppSettings Reload()
        {
            lock (_lock)
            {
                _settings = LoadInternal();
                return _settings;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Configuration Paths & Loading
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets the absolute filepath to <c>settings.json</c> inside the application binary directory.
        /// </summary>
        private string GetProgramDirFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        }

        /// <summary>
        /// Gets the absolute filepath to <c>settings.json</c> inside user %AppData%\OmniHid.
        /// </summary>
        private string GetAppDataFilePath()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniHid");
            return Path.Combine(folder, "settings.json");
        }

        /// <summary>
        /// Probes whether the application base directory is writable by standard user permissions.
        /// </summary>
        /// <returns><c>true</c> if portable local writing is allowed; otherwise <c>false</c> (e.g. Program Files).</returns>
        private static bool IsProgramDirectoryWritable()
        {
            try
            {
                string testFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads settings with portable and installed priority:
        /// Portable mode (writable folder) prioritizes local JSON, while installed mode (Program Files) prioritizes %AppData%.
        /// </summary>
        /// <returns>A populated <see cref="AppSettings"/> instance.</returns>
        private AppSettings LoadInternal()
        {
            AppSettings settings = new AppSettings();
            string localPath = GetProgramDirFilePath();
            string appDataPath = GetAppDataFilePath();
            bool isWritable = IsProgramDirectoryWritable();

            // 1. If running in portable mode (writable folder) and local settings exist, prioritize local file
            if (isWritable && File.Exists(localPath))
            {
                if (TryReadFile(localPath, settings))
                {
                    _activeSettingsFilePath = localPath;
                    Logger.Log("Settings loaded from portable program folder: " + localPath);
                    return settings;
                }
            }

            // 2. In installed / protected mode (or if no local settings exist), prioritize %AppData%
            if (File.Exists(appDataPath))
            {
                if (TryReadFile(appDataPath, settings))
                {
                    _activeSettingsFilePath = appDataPath;
                    Logger.Log("Settings loaded from AppData folder: " + appDataPath);
                    return settings;
                }
            }

            // 3. Fallback: If AppData does not exist yet, check local template (e.g. initial setup from Program Files)
            if (File.Exists(localPath))
            {
                if (TryReadFile(localPath, settings))
                {
                    _activeSettingsFilePath = isWritable ? localPath : appDataPath;
                    Logger.Log(string.Format("Settings initialized from template: {0} (Target: {1})", localPath, _activeSettingsFilePath));
                    return settings;
                }
            }

            // Check RunOnStartup from Windows Run registry key
            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (runKey != null)
                    {
                        object val = runKey.GetValue("OmniHidTaskbar");
                        settings.RunOnStartup = (val != null);
                    }
                }
            }
            catch { }

            // 5. If no file existed anywhere, create initial default configuration
            _activeSettingsFilePath = isWritable ? localPath : appDataPath;
            try
            {
                string dir = Path.GetDirectoryName(_activeSettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_activeSettingsFilePath, SerializeToJson(settings), Encoding.UTF8);
                Logger.Log("Default settings.json created at: " + _activeSettingsFilePath);
            }
            catch { }

            return settings;
        }

        /// <summary>
        /// Reads and parses an existing JSON configuration file into the target <see cref="AppSettings"/> instance.
        /// </summary>
        /// <param name="path">Absolute path to the JSON file.</param>
        /// <param name="target">Target settings object to populate.</param>
        /// <returns><c>true</c> if successfully read; otherwise <c>false</c>.</returns>
        private bool TryReadFile(string path, AppSettings target)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                target.DisplayStyle = ParseIntField(json, "DisplayStyle", target.DisplayStyle);
                target.DisplayMode = ParseIntField(json, "DisplayMode", target.DisplayMode);
                target.HideWhenDisconnected = ParseBoolField(json, "HideWhenDisconnected", target.HideWhenDisconnected);
                target.RunOnStartup = ParseBoolField(json, "RunOnStartup", target.RunOnStartup);
                target.PollIntervalSeconds = ParseIntField(json, "PollIntervalSeconds", target.PollIntervalSeconds);
                target.BackgroundPollIntervalSeconds = ParseIntField(json, "BackgroundPollIntervalSeconds", target.BackgroundPollIntervalSeconds);
                if (target.BackgroundPollIntervalSeconds <= 0) target.BackgroundPollIntervalSeconds = 300;
                target.HiddenDevices = ParseStringArrayField(json, "HiddenDevices");
                target.DeviceOrder = ParseStringArrayField(json, "DeviceOrder");
                target.CustomDeviceNames = ParseStringDictionaryField(json, "CustomDeviceNames");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log(string.Format("Failed to parse settings from {0}: {1}", path, ex.Message));
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Persistence & Registry Synchronization
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Persists the current configuration to disk (respecting active target path, local directory in portable mode, or %AppData%),
        /// and updates the Windows startup registry run key accordingly.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                if (_settings == null) return;

                string json = SerializeToJson(_settings);
                string targetPath = _activeSettingsFilePath;

                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = IsProgramDirectoryWritable() ? GetProgramDirFilePath() : GetAppDataFilePath();
                }

                // 1. First attempt: primary target path
                try
                {
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllText(targetPath, json, Encoding.UTF8);
                    _activeSettingsFilePath = targetPath;
                    Logger.Log("Settings successfully saved to: " + targetPath);
                    UpdateStartupRegistry(_settings.RunOnStartup);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Log("Target directory is write-protected (e.g. Program Files). Falling back to AppData.");
                }
                catch (Exception ex)
                {
                    Logger.Log("Error saving settings to " + targetPath + ": " + ex.Message + ". Trying AppData fallback.");
                }

                // 2. Fallback attempt: %AppData%
                try
                {
                    string appDataPath = GetAppDataFilePath();
                    string dir = Path.GetDirectoryName(appDataPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllText(appDataPath, json, Encoding.UTF8);
                    _activeSettingsFilePath = appDataPath;
                    Logger.Log("Settings saved to AppData fallback: " + appDataPath);
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to save settings to AppData: " + ex);
                }

                UpdateStartupRegistry(_settings.RunOnStartup);
            }
        }

        /// <summary>
        /// Adds or removes the application executable path from <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
        /// </summary>
        /// <param name="runOnStartup"><c>true</c> to register for auto-launch; <c>false</c> to deregister.</param>
        private void UpdateStartupRegistry(bool runOnStartup)
        {
            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null)
                    {
                        if (runOnStartup)
                        {
                            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                            runKey.SetValue("OmniHidTaskbar", "\"" + exePath + "\"");
                        }
                        else
                        {
                            runKey.DeleteValue("OmniHidTaskbar", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to update Windows startup registry: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Zero-Dependency JSON Serialization & Parsing Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Formats an <see cref="AppSettings"/> instance into a formatted JSON string without external libraries.
        /// </summary>
        /// <param name="s">Settings instance to serialize.</param>
        /// <returns>Formatted JSON string.</returns>
        private static string SerializeToJson(AppSettings s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat("  \"DisplayStyle\": {0},\n", s.DisplayStyle);
            sb.AppendFormat("  \"DisplayMode\": {0},\n", s.DisplayMode);
            sb.AppendFormat("  \"HideWhenDisconnected\": {0},\n", s.HideWhenDisconnected ? "true" : "false");
            sb.AppendFormat("  \"RunOnStartup\": {0},\n", s.RunOnStartup ? "true" : "false");
            sb.AppendFormat("  \"PollIntervalSeconds\": {0},\n", s.PollIntervalSeconds);
            sb.AppendFormat("  \"BackgroundPollIntervalSeconds\": {0},\n", s.BackgroundPollIntervalSeconds);
            sb.Append("  \"HiddenDevices\": [");
            if (s.HiddenDevices != null && s.HiddenDevices.Count > 0)
            {
                sb.AppendLine();
                for (int i = 0; i < s.HiddenDevices.Count; i++)
                {
                    string escaped = s.HiddenDevices[i].Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendFormat("    \"{0}\"{1}\n", escaped, i < s.HiddenDevices.Count - 1 ? "," : "");
                }
                sb.AppendLine("  ],");
            }
            else
            {
                sb.AppendLine("],");
            }

            sb.Append("  \"DeviceOrder\": [");
            if (s.DeviceOrder != null && s.DeviceOrder.Count > 0)
            {
                sb.AppendLine();
                for (int i = 0; i < s.DeviceOrder.Count; i++)
                {
                    string escaped = s.DeviceOrder[i].Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendFormat("    \"{0}\"{1}\n", escaped, i < s.DeviceOrder.Count - 1 ? "," : "");
                }
                sb.AppendLine("  ],");
            }
            else
            {
                sb.AppendLine("],");
            }

            sb.Append("  \"CustomDeviceNames\": {");
            if (s.CustomDeviceNames != null && s.CustomDeviceNames.Count > 0)
            {
                sb.AppendLine();
                int idx = 0;
                int count = s.CustomDeviceNames.Count;
                foreach (var kvp in s.CustomDeviceNames)
                {
                    string key = kvp.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string val = (kvp.Value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendFormat("    \"{0}\": \"{1}\"{2}\n", key, val, idx < count - 1 ? "," : "");
                    idx++;
                }
                sb.AppendLine("  }");
            }
            else
            {
                sb.AppendLine("}");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Parses a string dictionary ({ "key": "value", ... }) from raw JSON text.
        /// </summary>
        /// <param name="json">Raw JSON payload.</param>
        /// <param name="fieldName">Field name identifier.</param>
        /// <returns>Dictionary containing parsed key-value pairs.</returns>
        private static Dictionary<string, string> ParseStringDictionaryField(string json, string fieldName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string search = "\"" + fieldName + "\":";
                int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return result;
                idx += search.Length;

                int openBrace = json.IndexOf('{', idx);
                if (openBrace < 0) return result;

                int closeBrace = json.IndexOf('}', openBrace);
                if (closeBrace < 0) return result;

                int pos = openBrace + 1;
                while (pos < closeBrace)
                {
                    int kQuoteStart = json.IndexOf('"', pos);
                    if (kQuoteStart < 0 || kQuoteStart >= closeBrace) break;

                    int kQuoteEnd = kQuoteStart + 1;
                    while (kQuoteEnd < closeBrace)
                    {
                        if (json[kQuoteEnd] == '"' && json[kQuoteEnd - 1] != '\\')
                            break;
                        kQuoteEnd++;
                    }
                    if (kQuoteEnd >= closeBrace) break;

                    string key = json.Substring(kQuoteStart + 1, kQuoteEnd - kQuoteStart - 1)
                        .Replace("\\\"", "\"").Replace("\\\\", "\\");

                    int colon = json.IndexOf(':', kQuoteEnd);
                    if (colon < 0 || colon >= closeBrace) break;

                    int vQuoteStart = json.IndexOf('"', colon);
                    if (vQuoteStart < 0 || vQuoteStart >= closeBrace) break;

                    int vQuoteEnd = vQuoteStart + 1;
                    while (vQuoteEnd < closeBrace)
                    {
                        if (json[vQuoteEnd] == '"' && json[vQuoteEnd - 1] != '\\')
                            break;
                        vQuoteEnd++;
                    }
                    if (vQuoteEnd >= closeBrace) break;

                    string val = json.Substring(vQuoteStart + 1, vQuoteEnd - vQuoteStart - 1)
                        .Replace("\\\"", "\"").Replace("\\\\", "\\");

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        result[key.Trim()] = val.Trim();
                    }

                    pos = vQuoteEnd + 1;
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Parses a string array field from raw JSON text.
        /// </summary>
        /// <param name="json">Raw JSON payload.</param>
        /// <param name="fieldName">Field name identifier.</param>
        /// <returns>Extracted list of string items.</returns>
        private static List<string> ParseStringArrayField(string json, string fieldName)
        {
            var result = new List<string>();
            try
            {
                string search = "\"" + fieldName + "\":";
                int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return result;
                idx += search.Length;

                int openBracket = json.IndexOf('[', idx);
                if (openBracket < 0) return result;

                int closeBracket = json.IndexOf(']', openBracket);
                if (closeBracket < 0) return result;

                int pos = openBracket + 1;
                while (pos < closeBracket)
                {
                    int quoteStart = json.IndexOf('"', pos);
                    if (quoteStart < 0 || quoteStart >= closeBracket) break;

                    int quoteEnd = quoteStart + 1;
                    while (quoteEnd < closeBracket)
                    {
                        if (json[quoteEnd] == '"' && json[quoteEnd - 1] != '\\')
                            break;
                        quoteEnd++;
                    }

                    if (quoteEnd >= closeBracket) break;

                    string item = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    item = item.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        result.Add(item.Trim());
                    }

                    pos = quoteEnd + 1;
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Extracts an integer value for a given field from raw JSON text.
        /// </summary>
        /// <param name="json">Raw JSON string.</param>
        /// <param name="fieldName">Field name identifier.</param>
        /// <param name="defaultValue">Default value if not found or unparseable.</param>
        /// <returns>Parsed integer or default value.</returns>
        private static int ParseIntField(string json, string fieldName, int defaultValue)
        {
            try
            {
                string search = "\"" + fieldName + "\":";
                int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return defaultValue;
                idx += search.Length;

                while (idx < json.Length && (char.IsWhiteSpace(json[idx]))) idx++;
                int start = idx;
                while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '-')) idx++;

                string numStr = json.Substring(start, idx - start);
                int result;
                if (int.TryParse(numStr, out result)) return result;
            }
            catch { }
            return defaultValue;
        }

        /// <summary>
        /// Extracts a boolean value for a given field from raw JSON text.
        /// </summary>
        /// <param name="json">Raw JSON string.</param>
        /// <param name="fieldName">Field name identifier.</param>
        /// <param name="defaultValue">Default value if not found or unparseable.</param>
        /// <returns>Parsed boolean or default value.</returns>
        private static bool ParseBoolField(string json, string fieldName, bool defaultValue)
        {
            try
            {
                string search = "\"" + fieldName + "\":";
                int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return defaultValue;
                idx += search.Length;

                while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
                if (idx + 4 <= json.Length && json.Substring(idx, 4).ToLower() == "true") return true;
                if (idx + 5 <= json.Length && json.Substring(idx, 5).ToLower() == "false") return false;
            }
            catch { }
            return defaultValue;
        }
    }
}
