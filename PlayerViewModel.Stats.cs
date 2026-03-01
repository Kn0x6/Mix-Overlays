using System;
using System.Collections.Generic;
using System.Linq;

namespace MixOverlays.ViewModels
{
    /// <summary>
    /// Stats calculées à partir de l'historique récent (RecentMatches).
    /// Aucun appel API supplémentaire — tout est dérivé des données déjà chargées.
    ///
    /// INSTALLATION :
    ///   1. Ajouter ce fichier au projet (partial class de PlayerViewModel).
    ///   2. Dans RefreshMatchesAndPagination(), appeler RefreshStats() en fin de méthode.
    ///   3. Ajouter ChampionRecentStat au projet (déjà dans ce fichier).
    /// </summary>
    public partial class PlayerViewModel
    {
        // ── Winrate récent (sur les parties chargées) ─────────────────────────
        // Valeur 0..100 pour compatibilité avec WinRateBrushConverter existant

        public int    RecentWins   => _data.RecentMatches.Count(m => m.Win);
        public int    RecentLosses => _data.RecentMatches.Count(m => !m.Win);
        public int    RecentGames  => _data.RecentMatches.Count;

        /// <summary>0..100 — compatible avec WinRateBrushConverter.</summary>
        public double RecentWinRate => RecentGames > 0
            ? (double)RecentWins / RecentGames * 100.0
            : 0;

        public string RecentWinRateDisplay => RecentGames > 0
            ? $"{RecentWinRate:F0}%"
            : "—";

        // ── KDA moyen ─────────────────────────────────────────────────────────

        public double AvgKills   => RecentGames > 0 ? _data.RecentMatches.Average(m => m.Kills)   : 0;
        public double AvgDeaths  => RecentGames > 0 ? _data.RecentMatches.Average(m => m.Deaths)  : 0;
        public double AvgAssists => RecentGames > 0 ? _data.RecentMatches.Average(m => m.Assists) : 0;

        public double AvgKDA => AvgDeaths < 0.01
            ? AvgKills + AvgAssists
            : (AvgKills + AvgAssists) / AvgDeaths;

        public string AvgKDADisplay      => $"{AvgKDA:F2}";
        public string AvgKillsDisplay    => $"{AvgKills:F1}";
        public string AvgDeathsDisplay   => $"{AvgDeaths:F1}";
        public string AvgAssistsDisplay  => $"{AvgAssists:F1}";

        // ── CS moyen ─────────────────────────────────────────────────────────

        public double AvgCS => RecentGames > 0
            ? _data.RecentMatches.Average(m => m.CS)
            : 0;

        public string AvgCSDisplay => $"{AvgCS:F0}";

        // CS/min (GameDuration est en secondes dans MatchSummary)
        public double AvgCSPerMin
        {
            get
            {
                var valid = _data.RecentMatches.Where(m => m.GameDuration > 60).ToList();
                if (valid.Count == 0) return 0;
                return valid.Average(m => m.CS / (m.GameDuration / 60.0));
            }
        }

        public string AvgCSPerMinDisplay => $"{AvgCSPerMin:F1}";

        // ── Durée moyenne ─────────────────────────────────────────────────────

        public string AvgDurationDisplay
        {
            get
            {
                if (RecentGames == 0) return "—";
                double avgSec = _data.RecentMatches.Average(m => (double)m.GameDuration);
                int mins = (int)(avgSec / 60);
                int secs = (int)(avgSec % 60);
                return $"{mins}:{secs:D2}";
            }
        }

        // ── Streak en cours ───────────────────────────────────────────────────

        public int CurrentStreak
        {
            get
            {
                if (RecentGames == 0) return 0;
                bool first = _data.RecentMatches[0].Win;
                return _data.RecentMatches.TakeWhile(m => m.Win == first).Count();
            }
        }

        public bool   IsWinStreak   => RecentGames > 0 && _data.RecentMatches[0].Win;
        public string StreakDisplay => CurrentStreak == 0 ? "—"
                                     : IsWinStreak ? $"🔥 {CurrentStreak}V" : $"❄️ {CurrentStreak}D";

        // ── Stats par champion (top 3 joués récemment) ────────────────────────

        public List<ChampionRecentStat> TopChampionStats =>
            _data.RecentMatches
                .GroupBy(m => m.ChampionName)
                .Select(g =>
                {
                    int    games = g.Count();
                    int    wins  = g.Count(m => m.Win);
                    double kda   = g.Average(m => m.Deaths < 1
                        ? m.Kills + m.Assists
                        : (double)(m.Kills + m.Assists) / m.Deaths);
                    return new ChampionRecentStat
                    {
                        ChampionName = g.Key,
                        Games        = games,
                        Wins         = wins,
                        AvgKDA       = kda,
                        AvgCS        = g.Average(m => (double)m.CS),
                    };
                })
                .OrderByDescending(s => s.Games)
                .Take(3)
                .ToList();

        // ── Refresh ───────────────────────────────────────────────────────────

        /// <summary>
        /// Appeler après RefreshMatchesAndPagination() pour notifier le panneau stats.
        /// Exemple dans MainViewModel.OutOfGame.cs, méthode LoadMoreMatchesAsync :
        ///   pvm.RefreshMatchesAndPagination();
        ///   pvm.RefreshStats();          ← ajouter cette ligne
        /// </summary>
        public void RefreshStats()
        {
            foreach (var name in new[]
            {
                nameof(RecentWins), nameof(RecentLosses), nameof(RecentGames),
                nameof(RecentWinRate), nameof(RecentWinRateDisplay),
                nameof(AvgKDA), nameof(AvgKDADisplay),
                nameof(AvgKillsDisplay), nameof(AvgDeathsDisplay), nameof(AvgAssistsDisplay),
                nameof(AvgCS), nameof(AvgCSDisplay),
                nameof(AvgCSPerMin), nameof(AvgCSPerMinDisplay),
                nameof(AvgDurationDisplay),
                nameof(CurrentStreak), nameof(IsWinStreak), nameof(StreakDisplay),
                nameof(TopChampionStats),
            })
                OnPropertyChanged(name);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Modèle : performances par champion sur les dernières parties
    // ─────────────────────────────────────────────────────────────────────────

    public class ChampionRecentStat
    {
        public string ChampionName  { get; set; } = string.Empty;
        public int    Games         { get; set; }
        public int    Wins          { get; set; }
        public int    Losses        => Games - Wins;
        public double AvgKDA        { get; set; }
        public double AvgCS         { get; set; }

        /// <summary>0..100 pour compatibilité avec WinRateBrushConverter.</summary>
        public double WinRate          => Games > 0 ? (double)Wins / Games * 100.0 : 0;
        public string WinRateDisplay   => Games > 0 ? $"{WinRate:F0}%" : "—";
        public string RecordDisplay    => $"{Wins}W {Losses}L";
        public string KDADisplay       => $"{AvgKDA:F2} KDA";
    }
}
