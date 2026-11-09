using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarMusicWidget
{
    public partial class App : Application
    {
        // All logs live in %LocalAppData%\TaskbarMusicWidget\logs so the app works on any PC
        // and never writes next to the executable or into a hardcoded user folder.
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarMusicWidget", "logs");

        private const long MaxLogSizeBytes = 1 * 1024 * 1024; // 1 MB, then rotate

        private static readonly object LogLock = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            this.Exit += (s, args) => LogError("AppExit", $"Exit code {args.ApplicationExitCode}");
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("DispatcherUnhandledException", e.Exception.ToString());
            e.Handled = true; // Keep the widget alive instead of crashing
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogError("UnhandledException", e.ExceptionObject?.ToString() ?? "(null)");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogError("UnobservedTaskException", e.Exception.ToString());
            e.SetObserved();
        }

        /// <summary>
        /// Writes a line to the widget error log. Never throws: a failing logger must not
        /// take the app down from inside an exception handler.
        /// </summary>
        public static void LogError(string type, string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    string logFile = Path.Combine(LogDirectory, "widget_error.log");

                    var info = new FileInfo(logFile);
                    if (info.Exists && info.Length > MaxLogSizeBytes)
                    {
                        string oldFile = Path.Combine(LogDirectory, "widget_error.old.log");
                        File.Copy(logFile, oldFile, overwrite: true);
                        File.Delete(logFile);
                    }

                    File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {type}: {message}\n");
                }
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }
}
