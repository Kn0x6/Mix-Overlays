using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using MixOverlays.ViewModels;
using MixOverlays.Models;
using MixOverlays.Services;

namespace MixOverlays.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm = null!;
        private string _currentPage = "MyAccount";
        private GlobalHotkeyService? _hotkey;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _vm = new MainViewModel();
                DataContext = _vm;
                HeaderSearchHistoryPopup.DataContext = _vm;
                _vm.SettingsSaved += MainViewModel_SettingsSaved;

                _vm.PropertyChanged += (s, e) =>
                {
                    // Le détail est maintenant piloté directement par le binding du
                    // nouveau panneau de résumé dans MainWindow.xaml.

                    if (e.PropertyName == nameof(MainViewModel.IsLiveSessionAvailable))
                    {
                        if (_vm.IsLiveSessionAvailable)
                            SetActiveNav("Live");
                        else if (_currentPage == "Live")
                            SetActiveNav("MyAccount");
                    }

                    // L'overlay s'affiche UNIQUEMENT via Ctrl+X (raccourci global).
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erreur ViewModel");
            }

            KeyDown           += MainWindow_KeyDown;
            PreviewMouseDown  += MainWindow_PreviewMouseDown;
            Closed            += MainWindow_Closed;
            InitGlobalHotkey();

            SetActiveNav("MyAccount");
        }

        private void MainWindowRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not Border root)
                return;

            root.Clip = new RectangleGeometry(
                new Rect(0, 0, root.ActualWidth, root.ActualHeight),
                14,
                14);
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (_vm != null)
                _vm.SettingsSaved -= MainViewModel_SettingsSaved;
            DisposeGlobalHotkey();
            _vm?.Dispose();
        }

        private void MainViewModel_SettingsSaved(object? sender, EventArgs e)
        {
            DisposeGlobalHotkey();
            InitGlobalHotkey();

            // Keep a visible overlay in sync with its newly saved visual preferences.
            App.OverlayWindow?.ApplySettings();
        }

        /// <summary>
        /// Appelé dans le constructeur de MainWindow, après InitializeComponent().
        /// Remplace l'ancien bloc RegisterHotKey / SourceInitialized.
        /// </summary>
        private void InitGlobalHotkey()
        {
            _hotkey = new GlobalHotkeyService(_vm?.Settings.OverlayHotkey);
            _hotkey.HotkeyPressed += (_, _) => ToggleOverlay();
        }

        /// <summary>
        /// Appelé dans MainWindow_Closed.
        /// Remplace UnregisterHotKey.
        /// </summary>
        private void DisposeGlobalHotkey()
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }

        // ─── Nouveau ToggleOverlay — conditionné à LcuState.InGame ────────────
        private void ToggleOverlay()
        {
            try
            {
                // HotkeyPressed est déjà envoyé sur le Dispatcher par GlobalHotkeyService.
                // BeginInvoke évite un Invoke synchrone imbriqué et laisse le hook clavier retourner immédiatement.
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (App.OverlayWindow == null)
                            App.OverlayWindow = new OverlayWindow();

                        App.OverlayWindow.ApplySettings();
                        App.OverlayWindow.SetTeamData(_vm.AllyTeam, _vm.EnemyTeam);

                        if (App.OverlayWindow.IsVisible)
                        {
                            // Fermeture toujours autorisée
                            App.OverlayWindow.Hide();
                        }
                        else if (_vm.ClientState == LcuState.InGame && _vm.Settings.ShowOverlayInGame)
                        {
                            // Ouverture uniquement si LoL est en cours de partie
                            App.OverlayWindow.Show();
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[Overlay] Impossible de basculer l'overlay : {ex}");
                        App.OverlayWindow = null;
                    }
                });
            }
            catch (Exception ex)
            {
                App.Log($"[Overlay] Impossible de planifier l'affichage : {ex}");
            }
        }

        // ─── Hotkey local (MixOverlays au premier plan) ───────────────────────
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _vm?.SelectedMatch != null)
            {
                _vm.SelectedMatch = null;
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!HeaderSearchHistoryPopup.IsOpen)
                return;

            var popupContent = HeaderSearchHistoryPopup.Child as FrameworkElement;
            var clickIsInsideSearchPreview = HeaderSearchBox.IsMouseOver
                                          || HeaderSearchHistoryList.IsMouseOver
                                          || popupContent?.IsMouseOver == true;

            if (!clickIsInsideSearchPreview)
                CloseHeaderSearchHistoryPopup();
        }

        // ─── Fermeture du panneau detail ──────────────────────────────────────
        private void CloseMatchDetailPanel_Click(object sender, RoutedEventArgs e)
            => _vm.SelectedMatch = null;

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav(_currentPage == "Settings" ? "MyAccount" : "Settings");
        }

        private void BackToMyAccountButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav("MyAccount");
        }

        private void OpenLiveSessionButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveNav("Live");
        }

        // ─── Clic sur un participant ───────────────────────────────────────────
        private DateTime _matchDetailOpenTime = DateTime.MinValue;

        private void MatchParticipant_Click(object sender, MouseButtonEventArgs e)
        {
            // Bloquer si le panneau vient de s'ouvrir (clic "partagé" entre la ligne et le joueur)
            if ((DateTime.UtcNow - _matchDetailOpenTime).TotalMilliseconds < 350)
            {
                e.Handled = true;
                return;
            }
            // ... reste du code existant
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is MatchParticipantViewModel p)
                {
                    OpenMatchParticipantProfile(p);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MatchParticipant_Click] {ex.Message}");
            }
        }

        private void ViewMatchParticipantProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var participant = (sender as FrameworkElement)?.DataContext as MatchParticipantViewModel;

                // Les ContextMenu WPF vivent hors de l'arbre visuel principal :
                // fallback via leur DataContext si le MenuItem ne l'a pas hérité.
                if (participant == null && sender is MenuItem { Parent: ContextMenu menu })
                    participant = menu.DataContext as MatchParticipantViewModel;

                if (participant == null)
                    return;

                OpenMatchParticipantProfile(participant);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewMatchParticipantProfile_Click] {ex.Message}");
            }
        }

        private void OpenMatchParticipantProfile(MatchParticipantViewModel participant)
        {
            if (participant == null)
                return;

            _vm.SelectedMatch = null;

            if (_vm.MyAccount?.Puuid == participant.Puuid)
            {
                SetActiveNav("MyAccount");
                return;
            }

            SetActiveNav("Search");
            _vm.SearchInput = participant.DisplayName;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_vm.SearchPlayerCommand.CanExecute(null))
                    _vm.SearchPlayerCommand.Execute(null);
            });
        }

        // ─── Title bar ────────────────────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideToTray();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsReallyClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnClosing(e);
        }

        private void HideToTray()
        {
            try
            {
                ShowInTaskbar = false;
                Hide();
                App.Log("[Tray] Fenêtre masquée dans le tray.");
            }
            catch (Exception ex)
            {
                App.Log($"[Tray] Erreur HideToTray : {ex.Message}");
            }
        }

        private bool _isFullscreen;
        private double _prevLeft, _prevTop, _prevWidth, _prevHeight;

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isFullscreen)
            {
                _prevLeft = Left; _prevTop = Top; _prevWidth = Width; _prevHeight = Height;
                var wa = WpfScreen.GetWorkingAreaFrom(this);
                Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
                BtnFullscreen.Content = "❐"; BtnFullscreen.ToolTip = "Restaurer";
                _isFullscreen = true;
            }
            else
            {
                Left = _prevLeft; Top = _prevTop; Width = _prevWidth; Height = _prevHeight;
                BtnFullscreen.Content = "⛶"; BtnFullscreen.ToolTip = "Plein ecran";
                _isFullscreen = false;
            }
        }

        private static class WpfScreen
        {
            public static System.Windows.Rect GetWorkingAreaFrom(Window window)
            {
                var handle   = new WindowInteropHelper(window).Handle;
                var hMonitor = MonitorNative.MonitorFromWindow(handle, 2);
                var info     = new MonitorNative.MONITORINFO
                               { cbSize = Marshal.SizeOf(typeof(MonitorNative.MONITORINFO)) };
                MonitorNative.GetMonitorInfo(hMonitor, ref info);
                double sc = MonitorNative.GetDpiForMonitor(hMonitor) / 96.0;
                return new System.Windows.Rect(
                    info.rcWork.left / sc, info.rcWork.top / sc,
                    (info.rcWork.right  - info.rcWork.left) / sc,
                    (info.rcWork.bottom - info.rcWork.top)  / sc);
            }
        }

        private static class MonitorNative
        {
            [DllImport("user32.dll")] public  static extern IntPtr MonitorFromWindow(IntPtr hwnd, int f);
            [DllImport("user32.dll")] public  static extern bool   GetMonitorInfo(IntPtr h, ref MONITORINFO i);
            [DllImport("shcore.dll")] private static extern int    GetDpiForMonitor(IntPtr h, int t, out uint x, out uint y);
            public static uint GetDpiForMonitor(IntPtr h) { GetDpiForMonitor(h, 0, out uint x, out _); return x == 0 ? 96u : x; }

            [StructLayout(LayoutKind.Sequential)]
            public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int left, top, right, bottom; }
        }

        // ─── Navigation ───────────────────────────────────────────────────────
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string page)
            {
                SetActiveNav(page);
                if (page == "MyAccount" && _vm.MyAccount == null && _vm.IsConnected)
                    _ = _vm.LoadMyAccountAsync();
            }
        }

        private void SetActiveNav(string page)
        {
            if (_vm == null)
                return;

            var requestedPage = page;

            if (page == "Live" && !_vm.IsLiveSessionAvailable)
                page = "MyAccount";

            var isComingSoonPage = page is "Dashboard" or "Champions" or "Stats" or "Analysis" or "Compare";

            _currentPage = page;

            PageLive.Visibility       = page == "Live"       ? Visibility.Visible : Visibility.Collapsed;
            PageMyAccount.Visibility  = page == "MyAccount"  ? Visibility.Visible : Visibility.Collapsed;
            PageSearch.Visibility     = page == "Search"     ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility   = page == "Settings"   ? Visibility.Visible : Visibility.Collapsed;
            PageComingSoon.Visibility = isComingSoonPage      ? Visibility.Visible : Visibility.Collapsed;

            if (isComingSoonPage)
                UpdateComingSoonPage(page);

            UpdateSideNavState(requestedPage, page);

            if (_vm != null) _vm.SelectedMatch = null;
        }

        private void UpdateComingSoonPage(string page)
        {
            var title = page switch
            {
                "Dashboard" => "Dashboard",
                "Champions" => "Champions",
                "Stats"     => "Stats",
                "Analysis"  => "Analyse",
                "Compare"   => "Comparateur",
                _           => "Module"
            };

            ComingSoonTitle.Text = title;
            ComingSoonDescription.Text = $"Le module {title} est prévu pour une prochaine version. Il reste visible dans la navigation pour coller à la maquette sans simuler de données.";
        }

        private void UpdateSideNavState(string requestedPage, string actualPage)
        {
            var activeKey = actualPage;
            var buttons = new (Button Button, string Key)[]
            {
                (BtnSideMyAccount, "MyAccount"),
                (BtnSideDashboard, "Dashboard"),
                (BtnSideChampions, "Champions"),
                (BtnSideStats, "Stats"),
                (BtnSideAnalysis, "Analysis"),
                (BtnSideCompare, "Compare"),
                (BtnSideLive, "Live"),
                (BtnSideSettings, "Settings"),
            };

            foreach (var (button, key) in buttons)
            {
                var isActive = key == activeKey;
                button.Background = isActive
                    ? (Brush)FindResource("SidebarActiveBrush")
                    : Brushes.Transparent;
                button.BorderBrush = isActive
                    ? (Brush)FindResource("SoftBorderBrush")
                    : Brushes.Transparent;
                button.Foreground = isActive
                    ? (Brush)FindResource("TextPrimaryBrush")
                    : (Brush)FindResource("TextSecondaryBrush");
            }
        }

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Return && _vm.SearchPlayerCommand.CanExecute(null))
            {
                CloseHeaderSearchHistoryPopup();
                SetActiveNav("Search");
                _vm.SearchPlayerCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                CloseHeaderSearchHistoryPopup();
                e.Handled = true;
            }
        }

        private void SearchPlayerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SearchPlayerCommand.CanExecute(null))
            {
                CloseHeaderSearchHistoryPopup();
                SetActiveNav("Search");
                _vm.SearchPlayerCommand.Execute(null);
            }
        }

        private void HeaderSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            OpenHeaderSearchHistoryPopupIfAvailable();
        }

        private void HeaderSearchBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenHeaderSearchHistoryPopupIfAvailable();
        }

        private void OpenHeaderSearchHistoryPopupIfAvailable()
        {
            if (_vm?.HasSearchHistory == true)
                HeaderSearchHistoryPopup.IsOpen = true;
        }

        private void CloseHeaderSearchHistoryPopup()
        {
            HeaderSearchHistoryPopup.IsOpen = false;
        }

        private void HeaderSearchHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HeaderSearchHistoryList.SelectedItem is not PlayerViewModel player)
                return;

            CloseHeaderSearchHistoryPopup();
            HeaderSearchHistoryList.SelectedItem = null;

            _vm.SearchInput = player.Data.DisplayName;
            SetActiveNav("Search");

            if (_vm.SearchPlayerCommand.CanExecute(null))
                _vm.SearchPlayerCommand.Execute(null);
            else
                _vm.SearchedPlayer = player;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Border border && border.DataContext is PlayerViewModel player)
                {
                    _vm.SearchedPlayer = player;
                    _vm.SearchInput    = player.Data.DisplayName;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryItem_Click] {ex.Message}");
            }
        }
    }

}
