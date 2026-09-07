using System;
using System.IO;
using System.Windows;
using System.Reflection;
using OmniHidTaskbar.Core;
using OmniHidTaskbar.UI;

[assembly: AssemblyTitle("OmniHID Taskbar Battery Indicator")]
[assembly: AssemblyDescription("Universal Taskbar & Fluent Flyout Battery Monitor for Gaming Peripherals")]
[assembly: AssemblyVersion("0.2.2.0")]
[assembly: AssemblyFileVersion("0.2.2.0")]
[assembly: AssemblyInformationalVersion("0.2.2")]

namespace OmniHidTaskbar
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Application Entry Point
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents the main WPF application instance and execution lifecycle entry point.
    /// </summary>
    public class App : Application
    {
        /// <summary>
        /// Main application entry point requiring a single-threaded apartment (STA) state for WPF and Win32 interop.
        /// </summary>
        [STAThread]
        public static void Main()
        {
            try
            {
                Logger.Log("OmniHID Taskbar Battery Indicator started");

                var app = new App();
                app.Run(new OverlayWindow());
            }
            catch (Exception ex)
            {
                Logger.Log("Fatal Application Crash: " + ex);
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string crashPath = Path.Combine(baseDir, "crash.log");
                    if (!Logger.IsDirectoryWritable(baseDir))
                    {
                        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniHid");
                        if (!Directory.Exists(appData))
                        {
                            Directory.CreateDirectory(appData);
                        }
                        crashPath = Path.Combine(appData, "crash.log");
                    }
                    File.WriteAllText(crashPath, ex.ToString());
                }
                catch { }
            }
        }
    }
}