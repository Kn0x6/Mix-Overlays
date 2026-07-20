using System.Linq;
using System.Globalization;
using System.Windows.Media;
using MixOverlays.Models;

namespace MixOverlays.ViewModels
{
    public partial class PlayerViewModel
    {
        // ── Champion live ──────────────────────────────────────────────────────

        public string LiveChampionName =>
            !string.IsNullOrEmpty(_data.ChampionName) ? _data.ChampionName :
            _data.CurrentChampionName ?? string.Empty;

        // ── Rune live (keystone) ───────────────────────────────────────────────

        public int  LiveRuneId   => _data.LiveRuneId;
        public bool HasLiveRune  => _data.LiveRuneId > 0;

        // ── Winrate avec le champion actuel ────────────────────────────────────

        public double ChampionWinRate
        {
            get
            {
                var champ = LiveChampionName;
                if (string.IsNullOrEmpty(champ)) return 0;

                var champMatches = _data.RecentMatches
                    .Where(m => string.Equals(m.ChampionName, champ,
                                System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (champMatches.Count == 0) return 0;
                return (double)champMatches.Count(m => m.Win) / champMatches.Count;
            }
        }

        public int ChampionGamesPlayed
        {
            get
            {
                var champ = LiveChampionName;
                if (string.IsNullOrEmpty(champ)) return 0;
                return _data.RecentMatches.Count(m =>
                    string.Equals(m.ChampionName, champ,
                                  System.StringComparison.OrdinalIgnoreCase));
            }
        }

        public string ChampionWinRateDisplay
        {
            get
            {
                int g = ChampionGamesPlayed;
                if (g == 0) return "—";
                return $"{ChampionWinRate:P0} ({g}G)";
            }
        }

        // ── Maîtrise du champion actuel ────────────────────────────────────────

        public long CurrentChampionMasteryPoints
        {
            get
            {
                var championId = _data.ChampionId;
                if (championId <= 0) return 0;

                // L'identifiant Riot est stable, contrairement au nom affiché
                // (apostrophes, accents ou clé Data Dragon différente).
                var mastery = _data.TopMasteries.FirstOrDefault(m => m.championId == championId);
                return mastery?.championPoints ?? 0;
            }
        }

        public string CurrentChampionMasteryPointsDisplay =>
            $"{CurrentChampionMasteryPoints.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} POINTS";
    }
}
