using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarMusicWidget
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (s, args) =>
            {
                File.AppendAllText(@"C:\Users\Usuario\Documents\TaskbarMusicWidget\debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] DispatcherUnhandledException: {args.Exception}\n");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                File.AppendAllText(@"C:\Users\Usuario\Documents\TaskbarMusicWidget\debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] UnhandledException: {args.ExceptionObject}\n");
            };

            this.Exit += (s, args) =>
            {
                File.AppendAllText(@"C:\Users\Usuario\Documents\TaskbarMusicWidget\debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] App Exit ({args.ApplicationExitCode}):\n{Environment.StackTrace}\n");
            };
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("DispatcherUnhandledException", e.Exception);
            e.Handled = true; // Evita que la aplicación se cierre abruptamente
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("UnhandledException", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private void LogException(string type, Exception ex)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string logFile = Path.Combine(dir, "widget_error.log");
                File.AppendAllText(logFile, $"[{DateTime.Now}] {type}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch
            {
                // Ignorar fallos de escritura de logs
            }
        }
    }
}
