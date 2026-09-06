using System;
using System.IO;
using System.Windows;
using System.Reflection;
using OmniHidTaskbar.Core;
using OmniHidTaskbar.UI;

[assembly: AssemblyTitle("OmniHID Taskbar Battery Indicator")]
[assembly: AssemblyDescription("Universal Taskbar & Fluent Flyout Battery Monitor for Gaming Peripherals")]
[assembly: AssemblyVersion("0.0.2.0")]
[assembly: AssemblyFileVersion("0.0.2.0")]
[assembly: AssemblyInformationalVersion("0.0.2")]

namespace OmniHidTaskbar
{
    public class App : Application
    {
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
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
                }
                catch { }
            }
        }
    }
}