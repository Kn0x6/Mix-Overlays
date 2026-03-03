using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MixOverlays.ViewModels;

namespace MixOverlays.Views
{
    /// <summary>
    /// Fenêtre overlay in-game : transparente, Topmost, togglable via Ctrl+X.
    ///
    /// Fix plein écran : après Show(), on appelle SetWindowPos via P/Invoke avec
    /// HWND_TOPMOST + SWP_NOACTIVATE pour forcer l'overlay au-dessus même en mode
    /// plein écran fenêtré (LoL borderless windowed). En mode exclusif DirectX pur,
    /// aucun overlay WPF ne peut apparaître — LoL utilise borderless par défaut.
    ///
    /// Fix propagation de clic : ShowActivated = false évite de voler le focus à LoL.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        // ─── Win32 P/Invoke ──────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy,
            uint uFlags);

        private static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        private const uint             SWP_NOSIZE     = 0x0001;
        private const uint             SWP_NOMOVE     = 0x0002;
        private const uint             SWP_NOACTIVATE = 0x0010;
        private const uint             SWP_SHOWWINDOW = 0x0040;

        // ─── Constructeur ─────────────────────────────────────────────────────

        public OverlayWindow()
        {
            InitializeComponent();

            // Ne pas voler le focus à LoL au moment de Show()
            ShowActivated = false;
        }

        // ─── Données ──────────────────────────────────────────────────────────

        public void SetTeamData(
            ObservableCollection<PlayerViewModel> allies,
            ObservableCollection<PlayerViewModel>? enemies = null)
        {
            AllyList.ItemsSource = allies;

            if (enemies != null)
                EnemyList.ItemsSource = enemies;
        }

        // ─── Toggle ────────────────────────────────────────────────────────────

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
                ForceTopmost();
            }
        }

        /// <summary>
        /// Force la fenêtre au-dessus via Win32, sans l'activer.
        /// Nécessaire pour les jeux en plein écran fenêtré (borderless) :
        /// WPF Topmost=True seul peut être ignoré après un changement de focus.
        /// </summary>
        private void ForceTopmost()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                SetWindowPos(
                    hwnd,
                    HWND_TOPMOST,
                    0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Overlay] ForceTopmost erreur: {ex.Message}");
            }
        }

        // ─── Interactions UI ──────────────────────────────────────────────────

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
            => Hide();

        private void Overlay_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (ctrl && e.Key == Key.X)
            {
                e.Handled = true;
                Hide();
            }
        }
    }
}
