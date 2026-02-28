using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MixOverlays.ViewModels;
using MixOverlays.Models;

namespace MixOverlays.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm = null!;
        private string _currentPage = "MyAccount";

        // ─── Global Hotkey Ctrl+X ─────────────────────────────────────────────
        private const int  HOTKEY_ID    = 9000;
        private const uint MOD_CONTROL  = 0x0002;
        private const uint VK_X         = 0x58;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _hwndSource;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _vm = new MainViewModel();
                DataContext = _vm;

                _vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SelectedMatch))
                    {
                        if (_vm.SelectedMatch != null)
                            MatchDetailPanel.Visibility = Visibility.Visible;
                        else
                            MatchDetailPanel.Visibility = Visibility.Collapsed;
                    }
                    // L'overlay s'affiche UNIQUEMENT via Ctrl+X (raccourci global).
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Erreur ViewModel");
            }

            KeyDown           += MainWindow_KeyDown;
            Closed            += MainWindow_Closed;
            InitGlobalHotkey();

            SetActiveNav("MyAccount");
        }

        // ─── Enregistrement du raccourci global ───────────────────────────────
        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);

            if (!RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL, VK_X))
                System.Diagnostics.Debug.WriteLine("[Hotkey] Echec enregistrement Ctrl+X.");
            else
                System.Diagnostics.Debug.WriteLine("[Hotkey] Ctrl+X enregistre globalement.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            DisposeGlobalHotkey();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleOverlay();
                handled = true;
            }
            return IntPtr.Zero;
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

        // ─── Fermeture du panneau detail ──────────────────────────────────────
        private void CloseMatchDetailPanel_Click(object sender, RoutedEventArgs e)
            => _vm.SelectedMatch = null;

        // ─── Clic sur un participant ───────────────────────────────────────────
        private void MatchParticipant_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is MatchParticipantViewModel p)
                {
                    _vm.SelectedMatch = null;

                    if (_vm.MyAccount?.Puuid == p.Puuid)
                    {
                        SetActiveNav("MyAccount");
                        return;
                    }

                    SetActiveNav("Search");
                    _vm.SearchInput = $"{p.GameName}#{p.TagLine}";

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_vm.SearchPlayerCommand.CanExecute(null))
                                _vm.SearchPlayerCommand.Execute(null);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MatchParticipant_Click] {ex.Message}");
            }
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
        private void CloseButton_Click(object sender, RoutedEventArgs e)    => Close();

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
            if (sender is Button btn && btn.Tag is string page)
            {
                SetActiveNav(page);
                if (page == "MyAccount" && _vm.MyAccount == null && _vm.IsConnected)
                    _ = _vm.LoadMyAccountAsync();
            }
        }

        private void SetActiveNav(string page)
        {
            if (page == "Search" && _currentPage != "Search" && !_vm.IsSearching)
            {
                _vm.SearchedPlayer = null;
                _vm.SearchInput    = string.Empty;
            }

            _currentPage = page;

            PageLive.Visibility      = page == "Live"      ? Visibility.Visible : Visibility.Collapsed;
            PageMyAccount.Visibility = page == "MyAccount" ? Visibility.Visible : Visibility.Collapsed;
            PageSearch.Visibility    = page == "Search"    ? Visibility.Visible : Visibility.Collapsed;
            PageSettings.Visibility  = page == "Settings"  ? Visibility.Visible : Visibility.Collapsed;

            HighlightNavButton(BtnLive,      page == "Live");
            HighlightNavButton(BtnMyAccount, page == "MyAccount");
            HighlightNavButton(BtnSearch,    page == "Search");
            HighlightNavButton(BtnSettings,  page == "Settings");

            if (_vm != null) _vm.SelectedMatch = null;
        }

        private static void HighlightNavButton(Button btn, bool active)
        {
            btn.Background = active ? new SolidColorBrush(Color.FromArgb(30, 31, 111, 235)) : Brushes.Transparent;
            btn.Foreground = active
                ? new SolidColorBrush(Color.FromRgb(56, 139, 253))
                : new SolidColorBrush(Color.FromRgb(139, 148, 158));
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && _vm.SearchPlayerCommand.CanExecute(null))
                _vm.SearchPlayerCommand.Execute(null);
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
