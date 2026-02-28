// ═══════════════════════════════════════════════════════════════════════════
//  INSTRUCTIONS — Modifier MainWindow.xaml.cs
//
//  1. SUPPRIMER ces éléments dans MainWindow.xaml.cs :
//     - Les const HOTKEY_ID, MOD_CONTROL, VK_X
//     - Les [DllImport] RegisterHotKey et UnregisterHotKey
//     - Le champ _hwndSource
//     - La méthode MainWindow_SourceInitialized (et son abonnement dans le constructeur)
//     - L'abonnement SourceInitialized += MainWindow_SourceInitialized
//     - UnregisterHotKey dans MainWindow_Closed
//     - La méthode WndProc entière
//     - L'ancienne méthode ToggleOverlay()
//
//  2. AJOUTER le champ _hotkey et brancher les événements (voir ci-dessous)
//
//  3. AJOUTER GlobalHotkeyService.cs au projet (déjà fourni)
// ═══════════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Input;
using MixOverlays.Services;
using MixOverlays.ViewModels;

namespace MixOverlays.Views
{
    public partial class MainWindow
    {
        // ── Remplace RegisterHotKey ────────────────────────────────────────────
        private GlobalHotkeyService? _hotkey;

        /// <summary>
        /// Appelé dans le constructeur de MainWindow, après InitializeComponent().
        /// Remplace l'ancien bloc RegisterHotKey / SourceInitialized.
        /// </summary>
        private void InitGlobalHotkey()
        {
            _hotkey = new GlobalHotkeyService();
            _hotkey.CtrlXPressed += (_, _) => ToggleOverlay();
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.OverlayWindow == null)
                    App.OverlayWindow = new OverlayWindow();

                App.OverlayWindow.SetTeamData(_vm.AllyTeam, _vm.EnemyTeam);

                if (App.OverlayWindow.IsVisible)
                {
                    // Fermeture toujours autorisée
                    App.OverlayWindow.Hide();
                }
                else
                {
                    // Ouverture uniquement si LoL est en cours de partie
                    if (_vm.ClientState == LcuState.InGame)
                    {
                        App.OverlayWindow.Show();
                    }
                    // Silencieux hors-jeu : pas de message parasite
                }
            });
        }
    }
}
