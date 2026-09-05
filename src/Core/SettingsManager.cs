using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace OmniHidTaskbar.Core
{
    public class AppSettings
    {
        public int DisplayStyle { get; set; } // 0 = Percent, 1 = Glyphs
        public bool HideWhenDisconnected { get; set; }
        public bool RunOnStartup { get; set; }
        public int PollIntervalSeconds { get; set; }

        public AppSettings()
        {
            DisplayStyle = 0;
            HideWhenDisconnected = true;
            RunOnStartup = false;
            PollIntervalSeconds = 15;
        }
    }

    public class SettingsManager
    {
        private static SettingsManager _instance;
        private static readonly object _instanceLock = new object();

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

        private string GetProgramDirFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        }

        private string GetAppDataFilePath()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniHidTaskbar");
            return Path.Combine(folder, "settings.json");
        }

        private AppSettings LoadInternal()
        {
            AppSettings settings = new AppSettings();

            // 1. Try reading from Program Directory (preferred by user)
            string localPath = GetProgramDirFilePath();
            if (File.Exists(localPath))
            {
                if (TryReadFile(localPath, settings))
                {
                    _activeSettingsFilePath = localPath;
                    Logger.Log("Settings loaded from program folder: " + localPath);
                    return settings;
                }
            }

            // 2. Fallback to AppData
            string appDataPath = GetAppDataFilePath();
            if (File.Exists(appDataPath))
            {
                if (TryReadFile(appDataPath, settings))
                {
                    _activeSettingsFilePath = appDataPath;
                    Logger.Log("Settings loaded from AppData folder: " + appDataPath);
                    return settings;
                }
            }

            // 3. Fallback to Windows Registry (if migrating from old version)
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\OmniHidTaskbar"))
                {
                    if (key != null)
                    {
                        object ds = key.GetValue("DisplayStyle");
                        if (ds is int) settings.DisplayStyle = (int)ds;

                        object hwd = key.GetValue("HideWhenDisconnected");
                        if (hwd is int) settings.HideWhenDisconnected = (int)hwd == 1;

                        object pis = key.GetValue("PollInterval");
                        if (pis is int) settings.PollIntervalSeconds = (int)pis;
                    }
                }
            }
            catch { }

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

            // 3. If no file existed, create default settings.json in program directory
            if (!File.Exists(localPath) && !File.Exists(appDataPath))
            {
                try
                {
                    File.WriteAllText(localPath, SerializeToJson(settings), Encoding.UTF8);
                    Logger.Log("Default settings.json created in program folder: " + localPath);
                }
                catch { }
            }

            _activeSettingsFilePath = localPath;
            return settings;
        }

        private bool TryReadFile(string path, AppSettings target)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                target.DisplayStyle = ParseIntField(json, "DisplayStyle", target.DisplayStyle);
                target.HideWhenDisconnected = ParseBoolField(json, "HideWhenDisconnected", target.HideWhenDisconnected);
                target.RunOnStartup = ParseBoolField(json, "RunOnStartup", target.RunOnStartup);
                target.PollIntervalSeconds = ParseIntField(json, "PollIntervalSeconds", target.PollIntervalSeconds);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log(string.Format("Failed to parse settings from {0}: {1}", path, ex.Message));
                return false;
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                if (_settings == null) return;

                string json = SerializeToJson(_settings);

                // 1. First attempt: program directory
                string localPath = GetProgramDirFilePath();
                try
                {
                    File.WriteAllText(localPath, json, Encoding.UTF8);
                    _activeSettingsFilePath = localPath;
                    Logger.Log("Settings successfully saved to program folder: " + localPath);
                    UpdateStartupRegistry(_settings.RunOnStartup);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Log("Program directory is write-protected (e.g. Program Files). Falling back to AppData.");
                }
                catch (Exception ex)
                {
                    Logger.Log("Error saving settings to program folder: " + ex.Message + ". Trying AppData fallback.");
                }

                // 2. Fallback attempt: %AppData%
                try
                {
                    string appDataPath = GetAppDataFilePath();
                    string dir = Path.GetDirectoryName(appDataPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    File.WriteAllText(appDataPath, json, Encoding.UTF8);
                    _activeSettingsFilePath = appDataPath;
                    Logger.Log("Settings saved to AppData folder: " + appDataPath);
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to save settings to AppData: " + ex);
                }

                UpdateStartupRegistry(_settings.RunOnStartup);
            }
        }

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

        private static string SerializeToJson(AppSettings s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat("  \"DisplayStyle\": {0},\n", s.DisplayStyle);
            sb.AppendFormat("  \"HideWhenDisconnected\": {0},\n", s.HideWhenDisconnected ? "true" : "false");
            sb.AppendFormat("  \"RunOnStartup\": {0},\n", s.RunOnStartup ? "true" : "false");
            sb.AppendFormat("  \"PollIntervalSeconds\": {0}\n", s.PollIntervalSeconds);
            sb.AppendLine("}");
            return sb.ToString();
        }

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
