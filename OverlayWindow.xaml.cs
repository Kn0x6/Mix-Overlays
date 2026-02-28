using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using MixOverlays.ViewModels;

namespace MixOverlays.Views
{
    /// <summary>
    /// Fenêtre overlay in-game : transparente, Topmost, togglable via Ctrl+X.
    ///
    /// Le raccourci global Ctrl+X est enregistré dans MainWindow.xaml.cs via
    /// RegisterHotKey (user32). Quand WM_HOTKEY arrive, MainWindow appelle
    /// _overlay.Toggle(). Cette classe gère aussi le Ctrl+X local (si la
    /// fenêtre a le focus) et le bouton ✕.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
        }

        // ─── Données ──────────────────────────────────────────────────────────

        /// <summary>
        /// Lie les collections alliés / ennemis aux deux ItemsControl de l'overlay.
        /// Peut être appelé avant ou après Show() car ObservableCollection
        /// met l'UI à jour automatiquement à l'arrivée des données.
        /// </summary>
        public void SetTeamData(
            ObservableCollection<PlayerViewModel> allies,
            ObservableCollection<PlayerViewModel>? enemies = null)
        {
            AllyList.ItemsSource  = allies;

            if (enemies != null)
                EnemyList.ItemsSource = enemies;
        }

        // ─── Toggle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Affiche ou masque l'overlay.
        /// Appelé par MainWindow quand WM_HOTKEY Ctrl+X est reçu.
        /// </summary>
        public void Toggle()
        {
            if (IsVisible)
                Hide();
            else
            {
                Show();
                // Ne pas voler le focus à LoL
                // On utilise ShowActivated = false dans le constructeur si nécessaire
            }
        }

        // ─── Interactions UI ──────────────────────────────────────────────────

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
            => Hide();

        /// <summary>
        /// Ctrl+X local (au cas où la fenêtre a le focus).
        /// </summary>
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
