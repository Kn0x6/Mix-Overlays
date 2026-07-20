using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using MixOverlays.Services;
using MixOverlays.Views;
using System.Threading;
using System.Threading.Tasks;

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
        private static readonly string LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MixOverlays", "Logs");
        private static readonly string LogPath = Path.Combine(LogFolder, "MixOverlays.log");
        private static readonly object LogLock = new();
        private static StreamWriter? _logWriter;
        private static int _resourcesDisposed;

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.WriteLine(line);
            try
            {
                lock (LogLock)
                    _logWriter?.WriteLine(line);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Log] Échec écriture: {ex.Message}");
            }
        }

        // ─── Démarrage ────────────────────────────────────────────────────────

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                _logWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                Log("=== MixOverlays démarré ===");
                Log($"OS: {Environment.OSVersion}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Startup] Impossible d'initialiser le fichier de log: {ex.Message}");
            }

            AppDomain.CurrentDomain.UnhandledException +=
                (s, ex) => Log($"[CRASH] {ex.ExceptionObject}");
            DispatcherUnhandledException += (_, ex) =>
                Log($"[UI CRASH] {ex.Exception}");
            TaskScheduler.UnobservedTaskException += (_, ex) =>
            {
                Log($"[TASK CRASH] {ex.Exception}");
                ex.SetObserved();
            };

            // Créer le tray en code-behind (évite l'erreur pack URI en XAML)
            try
            {
                var iconUri = new Uri("pack://application:,,,/Icone.ico");
                var iconStream = GetResourceStream(iconUri)?.Stream;

                _trayIcon = new TaskbarIcon();

                if (iconStream != null)
                    _trayIcon.Icon = new System.Drawing.Icon(iconStream);

                _trayIcon.ToolTipText = "MixOverlays — en arrière-plan";
                _trayIcon.Visibility = Visibility.Visible;
                _trayIcon.ForceCreate(); // ← AJOUTER cette ligne

                // Menu contextuel
                var menu = new ContextMenu();

                var showItem = new MenuItem { Header = "📂  Afficher MixOverlays" };
                showItem.Click += TrayShow_Click;

                var separator = new Separator();

                var exitItem = new MenuItem { Header = "✖  Quitter MixOverlays", FontWeight = FontWeights.Bold };
                exitItem.Click += TrayExit_Click;

                menu.Items.Add(showItem);
                menu.Items.Add(separator);
                menu.Items.Add(exitItem);

                _trayIcon.ContextMenu = menu;

                // Double-clic pour rouvrir
                _trayIcon.TrayMouseDoubleClick += TrayShow_Click;

                Log("[Tray] TaskbarIcon initialisé.");
            }
            catch (Exception ex)
            {
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
                Current.MainWindow.ShowInTaskbar = true;
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
                Current.Shutdown();
            });
        }

        // ─── Sortie ───────────────────────────────────────────────────────────
        protected override void OnExit(ExitEventArgs e)
        {
            Log("=== MixOverlays fermé ===");
            DisposeResources();
            base.OnExit(e);
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
                return;

            try
            {
                if (MainWindow?.DataContext is IDisposable disposableViewModel)
                    disposableViewModel.Dispose();

                if (OverlayWindow != null)
                {
                    OverlayWindow.Close();
                    OverlayWindow = null;
                }

                LcuService?.Dispose();
                LcuService = null;

                RiotApiService?.Dispose();
                RiotApiService = null;

                _trayIcon?.Dispose();
                _trayIcon = null;
            }
            catch (Exception ex)
            {
                Log($"[Shutdown] Erreur de libération: {ex}");
            }
            finally
            {
                lock (LogLock)
                {
                    _logWriter?.Dispose();
                    _logWriter = null;
                }
            }
        }
    }
}
