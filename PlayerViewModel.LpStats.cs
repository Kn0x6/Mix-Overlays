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
            App.Log($"[PlayerVM] SetLpHistory — {_lpHistory.Count} snapshots, HasLpData={HasLpData}");
            RefreshLpStats();
        }

        // ─── Propriétés bindées ───────────────────────────────────────────────

        /// <summary>
        /// Snapshots ordonnés du plus récent au plus ancien (max 30).
        /// Le premier point est normalisé avec le SoloRank actuel afin que le graphique
        /// ne reste jamais en retard par rapport au rang affiché en haut de la carte.
        /// </summary>
        public List<LpSnapshot> LpSnapshots => BuildDisplayLpSnapshots();

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

        private List<LpSnapshot> BuildDisplayLpSnapshots()
        {
            var snapshots = _lpHistory
                .OrderByDescending(s => s.Timestamp)
                .Take(30)
                .ToList();

            var solo = _data.SoloRank;
            if (solo == null || string.IsNullOrWhiteSpace(solo.tier))
                return snapshots;

            var current = new LpSnapshot
            {
                Timestamp    = DateTime.UtcNow,
                LeaguePoints = solo.leaguePoints,
                Tier         = solo.tier,
                Rank         = solo.rank,
                ChampionName = "Actuel",
                Puuid        = _data.Puuid,
                GameName     = _data.GameName,
                TagLine      = _data.TagLine,
            };

            if (snapshots.Count == 0)
                return new List<LpSnapshot> { current };

            var latest = snapshots[0];
            bool alreadyCurrent = string.Equals(latest.Tier, current.Tier, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(latest.Rank, current.Rank, StringComparison.OrdinalIgnoreCase)
                               && latest.LeaguePoints == current.LeaguePoints;

            if (alreadyCurrent)
                return snapshots;

            current.LpDelta = ComputeDeltaBetweenSnapshots(latest, current);
            current.IsWin   = current.LpDelta > 0 ? true : current.LpDelta < 0 ? false : null;

            snapshots.Insert(0, current);
            return snapshots.Take(30).ToList();
        }

        private static int ComputeDeltaBetweenSnapshots(LpSnapshot pre, LpSnapshot post)
        {
            if (string.Equals(pre.Tier, post.Tier, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pre.Rank, post.Rank, StringComparison.OrdinalIgnoreCase))
                return post.LeaguePoints - pre.LeaguePoints;

            int rs = (TierVal(post.Tier) - TierVal(pre.Tier)) * 400
                   + (RankVal(post.Rank) - RankVal(pre.Rank))  * 100;

            if (rs > 0) return  post.LeaguePoints + (100 - pre.LeaguePoints)  + Math.Max(0, rs - 100);
            if (rs < 0) return -(pre.LeaguePoints + (100 - post.LeaguePoints) + Math.Max(0, -rs - 100));
            return post.LeaguePoints - pre.LeaguePoints;
        }

        private static int TierVal(string t) => t?.ToUpper() switch
        {
            "IRON" => 0, "BRONZE" => 1, "SILVER" => 2, "GOLD" => 3, "PLATINUM" => 4,
            "EMERALD" => 5, "DIAMOND" => 6, "MASTER" => 7, "GRANDMASTER" => 8, "CHALLENGER" => 9,
            _ => -1,
        };

        private static int RankVal(string r) => r?.ToUpper() switch
        { "IV" => 0, "III" => 1, "II" => 2, "I" => 3, _ => 0 };

        private static string FormatDelta(int delta) =>
            delta >= 0 ? $"+{delta} LP" : $"{delta} LP";

        private static string DeltaHex(int delta) =>
            delta > 0 ? "#3FB950" : delta < 0 ? "#F85149" : "#8B949E";

        // ─── Refresh ──────────────────────────────────────────────────────────

        private void RefreshLpStats()
        {
            App.Log($"[PlayerVM] RefreshLpStats — firing PropertyChanged(LpSnapshots), count={_lpHistory.Count}");
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
