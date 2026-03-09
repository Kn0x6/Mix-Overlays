using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using H.NotifyIcon;
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
        public static OverlayWindow?   OverlayWindow   { get; set; }

        // ─── Tray ─────────────────────────────────────────────────────────────
        private TaskbarIcon? _trayIcon;
        public static bool IsReallyClosing { get; private set; } = false;

        // ─── Log ──────────────────────────────────────────────────────────────
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "MixOverlays_debug.log");
        private static StreamWriter? _logWriter;

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);
            try { lock (LogPath) { _logWriter?.WriteLine(line); _logWriter?.Flush(); } }
            catch { }
        }

        // ─── Démarrage ────────────────────────────────────────────────────────

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                _logWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                Log("=== MixOverlays démarré ===");
                Log($"OS: {Environment.OSVersion}");
            }
            catch { }

            Trace.Listeners.Add(new TextWriterTraceListener(LogPath, "fileListener"));
            AppDomain.CurrentDomain.UnhandledException +=
                (s, ex) => Log($"[CRASH] {ex.ExceptionObject}");

            // Initialiser le tray (TaskbarIcon déclaré dans App.xaml)
            try
            {
                _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
                Log("[Tray] TaskbarIcon initialisé.");
            }
            catch (Exception ex)
            {
                // Ne pas planter l'appli si l'icône est introuvable
                Log($"[Tray] Échec init TaskbarIcon : {ex.Message}");
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        // ─── Handlers menu tray (définis dans App.xaml) ───────────────────────
        private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowMainWindow();
        private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApp();

        public static void ShowMainWindow()
        {
            Current.Dispatcher.Invoke(() =>
            {
                if (Current.MainWindow == null) return;
                Current.MainWindow.Show();
                Current.MainWindow.WindowState = WindowState.Normal;
                Current.MainWindow.Activate();
            });
        }

        public static void ExitApp()
        {
            IsReallyClosing = true;
            Current.Dispatcher.Invoke(() =>
            {
                var app = (App)Current;
                if (app._trayIcon != null)
                {
                    app._trayIcon.Dispose();
                    app._trayIcon = null;
                }
                Current.Shutdown();
            });
        }

        // ─── Sortie ───────────────────────────────────────────────────────────
        protected override void OnExit(ExitEventArgs e)
        {
            Log("=== MixOverlays fermé ===");
            _trayIcon?.Dispose();
            _logWriter?.Close();
            base.OnExit(e);
        }
    }
}
