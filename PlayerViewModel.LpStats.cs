using System;
using System.Collections.Generic;
using System.Linq;
using MixOverlays.Models;

namespace MixOverlays.ViewModels
{
    /// <summary>
    /// Partial class PlayerViewModel — propriétés LP Stats pour le panneau "LP Stats"
    /// dans PlayerCard.xaml.
    ///
    /// INSTALLATION :
    ///   1. Ajouter ce fichier au projet (partial class de PlayerViewModel).
    ///   2. Dans MainViewModel, connecter LpTrackerService.HistoryUpdated
    ///      pour appeler RefreshLpStats() sur MyAccount.
    ///   3. Binder les propriétés ci-dessous dans PlayerCard.xaml.
    /// </summary>
    public partial class PlayerViewModel
    {
        // ─── Référence vers l'historique LP (injecté depuis MainViewModel) ────

        private List<LpSnapshot> _lpHistory = new();

        /// <summary>
        /// Met à jour l'historique LP et rafraîchit les propriétés bindées.
        /// Appeler depuis MainViewModel.OnLpHistoryUpdated().
        /// </summary>
        public void SetLpHistory(IEnumerable<LpSnapshot> history)
        {
            _lpHistory = history.ToList();
            RefreshLpStats();
        }

        // ─── Propriétés bindées ───────────────────────────────────────────────

        /// <summary>Snapshots ordonnés du plus récent au plus ancien (max 30).</summary>
        public List<LpSnapshot> LpSnapshots => _lpHistory.Take(30).ToList();

        /// <summary>True si on a au moins 2 snapshots (courbe possible).</summary>
        public bool HasLpData => _lpHistory.Count >= 2;

        /// <summary>LP gagnés/perdus sur les 7 derniers jours.</summary>
        public int LpDelta7d => ComputeDelta(7);

        /// <summary>LP gagnés/perdus sur les 30 derniers jours.</summary>
        public int LpDelta30d => ComputeDelta(30);

        /// <summary>Affichage "+124 LP" ou "−18 LP" pour 7 jours.</summary>
        public string LpDelta7dDisplay  => FormatDelta(LpDelta7d);

        /// <summary>Affichage "+124 LP" ou "−18 LP" pour 30 jours.</summary>
        public string LpDelta30dDisplay => FormatDelta(LpDelta30d);

        /// <summary>Couleur du delta 7 jours (#3FB950 vert / #F85149 rouge / gris).</summary>
        public string LpDelta7dHex  => DeltaHex(LpDelta7d);

        /// <summary>Couleur du delta 30 jours.</summary>
        public string LpDelta30dHex => DeltaHex(LpDelta30d);

        /// <summary>Nombre de parties classées tracées.</summary>
        public int LpTrackedGames => _lpHistory.Count(s => s.LpDelta.HasValue);

        /// <summary>Résumé court, ex: "12 parties tracées".</summary>
        public string LpTrackedGamesDisplay =>
            LpTrackedGames == 0 ? "Aucune partie tracée"
            : LpTrackedGames == 1 ? "1 partie tracée"
            : $"{LpTrackedGames} parties tracées";

        // ─── Helpers ──────────────────────────────────────────────────────────

        private int ComputeDelta(int days)
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            return _lpHistory
                .Where(s => s.Timestamp >= cutoff && s.LpDelta.HasValue)
                .Sum(s => s.LpDelta!.Value);
        }

        private static string FormatDelta(int delta) =>
            delta >= 0 ? $"+{delta} LP" : $"{delta} LP";

        private static string DeltaHex(int delta) =>
            delta > 0 ? "#3FB950" : delta < 0 ? "#F85149" : "#8B949E";

        // ─── Refresh ──────────────────────────────────────────────────────────

        private void RefreshLpStats()
        {
            OnPropertyChanged(nameof(LpSnapshots));
            OnPropertyChanged(nameof(HasLpData));
            OnPropertyChanged(nameof(LpDelta7d));
            OnPropertyChanged(nameof(LpDelta30d));
            OnPropertyChanged(nameof(LpDelta7dDisplay));
            OnPropertyChanged(nameof(LpDelta30dDisplay));
            OnPropertyChanged(nameof(LpDelta7dHex));
            OnPropertyChanged(nameof(LpDelta30dHex));
            OnPropertyChanged(nameof(LpTrackedGames));
            OnPropertyChanged(nameof(LpTrackedGamesDisplay));
        }
    }
}
