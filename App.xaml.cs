using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ERHandlerManager
{
    public partial class App : Application
    {
        private static readonly string LogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERHandlerManager", "crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => LogCrash("AppDomain", a.ExceptionObject?.ToString());
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("Dispatcher", e.Exception.ToString());
            e.Handled = false;
        }

        private static void LogCrash(string kind, string? detail)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\n{detail}\n\n");
            }
            catch { }
        }
    }
}
