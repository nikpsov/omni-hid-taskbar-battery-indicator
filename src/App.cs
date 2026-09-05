using System;
using System.IO;
using System.Windows;
using OmniHidTaskbar.Core;
using OmniHidTaskbar.UI;

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