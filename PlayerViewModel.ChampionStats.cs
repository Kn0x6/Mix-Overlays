using System.Collections.Generic;
using System.Linq;
using MixOverlays.Models;

namespace MixOverlays.ViewModels
{
    public partial class PlayerViewModel
    {
        // ── Helpers privés (préfixés _cs_ pour éviter tout conflit) ──────────
        private List<MatchSummary> _csMatches   => _data.RecentMatches ?? new List<MatchSummary>();
        private int                _csCount     => _csMatches.Count;
        private double             _csAvgKills   => _csCount > 0 ? _csMatches.Average(m => m.Kills)   : 0;
        private double             _csAvgDeaths  => _csCount > 0 ? _csMatches.Average(m => m.Deaths)  : 0;
        private double             _csAvgAssists => _csCount > 0 ? _csMatches.Average(m => m.Assists) : 0;

        // ── Couleur du winrate récent ─────────────────────────────────────────
        public string RecentWinRateHex
        {
            get
            {
                if (_csCount == 0) return "#8B949E";
                if (RecentWinRate >= 0.55) return "#3FB950";
                if (RecentWinRate >= 0.50) return "#E3B341";
                return "#F85149";
            }
        }

        // ── KDA moyen ─────────────────────────────────────────────────────────
        public double RecentAvgKDA =>
            _csAvgDeaths == 0 ? _csAvgKills + _csAvgAssists : (_csAvgKills + _csAvgAssists) / _csAvgDeaths;

        public string RecentAvgKDADisplay     => _csCount == 0 ? "0,00" : $"{RecentAvgKDA:F2}".Replace('.', ',');
        public string RecentAvgKillsDisplay   => _csCount == 0 ? "0,0"  : $"{_csAvgKills:F1}".Replace('.', ',');
        public string RecentAvgDeathsDisplay  => _csCount == 0 ? "0,0"  : $"{_csAvgDeaths:F1}".Replace('.', ',');
        public string RecentAvgAssistsDisplay => _csCount == 0 ? "0,0"  : $"{_csAvgAssists:F1}".Replace('.', ',');

        public string RecentAvgKDAHex
        {
            get
            {
                if (_csCount == 0) return "#8B949E";
                if (RecentAvgKDA >= 3.0) return "#3FB950";
                if (RecentAvgKDA >= 2.0) return "#E3B341";
                return "#F85149";
            }
        }

        // ── CS / min ──────────────────────────────────────────────────────────
        public string RecentAvgCSPerMinDisplay
        {
            get
            {
                if (_csCount == 0) return "0,0";
                var valid = _csMatches.Where(m => m.GameDuration > 0).ToList();
                if (!valid.Any()) return "0,0";
                return $"{valid.Average(m => m.CS / (m.GameDuration / 60.0)):F1}".Replace('.', ',');
            }
        }

        public string RecentAvgCSDisplay =>
            _csCount == 0 ? "0" : $"{_csMatches.Average(m => m.CS):F0}";

        // ── Durée moyenne ─────────────────────────────────────────────────────
        public string RecentAvgDurationDisplay
        {
            get
            {
                if (_csCount == 0) return "—";
                double avgMins = _csMatches.Average(m => m.GameDuration / 60.0);
                int mins = (int)avgMins;
                int secs = (int)((avgMins - mins) * 60);
                return $"{mins}:{secs:D2}";
            }
        }

        // ── Streak actuel ─────────────────────────────────────────────────────
        public string CurrentStreakDisplay
        {
            get
            {
                if (_csCount == 0) return "—";
                bool firstWin = _csMatches[0].Win;
                int count = 0;
                foreach (var m in _csMatches)
                {
                    if (m.Win == firstWin) count++;
                    else break;
                }
                return firstWin ? $"{count}V" : $"{count}D";
            }
        }

        public string CurrentStreakHex =>
            CurrentStreakDisplay == "—" ? "#8B949E"
            : CurrentStreakDisplay.EndsWith("V") ? "#3FB950" : "#F85149";

        // ── Top champions depuis l'historique ─────────────────────────────────
        public bool HasChampionStats => TopChampionsFromHistory.Count > 0;

        public List<ChampionStatEntry> TopChampionsFromHistory
        {
            get
            {
                if (_csCount == 0) return new List<ChampionStatEntry>();

                return _csMatches
                    .Where(m => !string.IsNullOrEmpty(m.ChampionName))
                    .GroupBy(m => m.ChampionName)
                    .Select(g =>
                    {
                        int    games  = g.Count();
                        int    wins   = g.Count(m => m.Win);
                        double avgK   = g.Average(m => m.Kills);
                        double avgD   = g.Average(m => m.Deaths);
                        double avgA   = g.Average(m => m.Assists);
                        double avgKda = avgD == 0 ? avgK + avgA : (avgK + avgA) / avgD;
                        return new ChampionStatEntry
                        {
                            ChampionName = g.Key,
                            Games        = games,
                            Wins         = wins,
                            Losses       = games - wins,
                            WinRate      = games > 0 ? (double)wins / games : 0,
                            AvgKills     = avgK,
                            AvgDeaths    = avgD,
                            AvgAssists   = avgA,
                            AvgKDA       = avgKda
                        };
                    })
                    .OrderByDescending(c => c.Games)
                    .ThenByDescending(c => c.WinRate)
                    .Take(5)
                    .ToList();
            }
        }
    }

    // ─── Modèle d'entrée par champion ────────────────────────────────────────
    public class ChampionStatEntry
    {
        public string ChampionName { get; set; } = string.Empty;
        public int    Games        { get; set; }
        public int    Wins         { get; set; }
        public int    Losses       { get; set; }
        public double WinRate      { get; set; }
        public double AvgKills     { get; set; }
        public double AvgDeaths    { get; set; }
        public double AvgAssists   { get; set; }
        public double AvgKDA       { get; set; }

        public string WinRateDisplay => $"{WinRate * 100:F0}%";
        public string GamesDisplay   => Games == 1 ? "1 partie" : $"{Games} parties";
        public string KdaDisplay     => $"{AvgKDA:F2} KDA".Replace('.', ',');

        public string WinRateHex
        {
            get
            {
                if (WinRate >= 0.60) return "#3FB950";
                if (WinRate >= 0.50) return "#E3B341";
                return "#F85149";
            }
        }

        public string KdaHex
        {
            get
            {
                if (AvgKDA >= 3.0) return "#3FB950";
                if (AvgKDA >= 2.0) return "#E3B341";
                return "#F85149";
            }
        }

        public double WinBarWidth => WinRate * 90;
    }
}
