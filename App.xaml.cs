using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MixOverlays.Services;
using MixOverlays.Views;

namespace MixOverlays
{
    public partial class App : Application
    {
        // ─── Services partagés ────────────────────────────────────────────────
        public static LcuService?      LcuService      { get; set; }
        public static RiotApiService?  RiotApiService  { get; set; }
        public static SettingsService? SettingsService { get; set; }

        // ─── Fenêtres partagées ───────────────────────────────────────────────
        public static OverlayWindow?   OverlayWindow   { get; set; }

        // ─── Log fichier ──────────────────────────────────────────────────────
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "MixOverlays_debug.log");

        private static StreamWriter? _logWriter;

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);
            try
            {
                lock (LogPath)
                {
                    _logWriter?.WriteLine(line);
                    _logWriter?.Flush();
                }
            }
            catch { }
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Ouvrir le fichier de log
            try
            {
                _logWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                Log("=== MixOverlays démarré ===");
                Log($"OS: {Environment.OSVersion}");
                Log($"LogPath: {LogPath}");
            }
            catch { }

            // Rediriger Trace vers le fichier
            System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(LogPath, "fileListener"));

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                Log($"[CRASH] UnhandledException: {ex.ExceptionObject}");

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log("=== MixOverlays fermé ===");
            _logWriter?.Close();
            base.OnExit(e);
        }
    }
}
