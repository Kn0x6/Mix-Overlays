using System.Linq;
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

        public int CurrentChampionMasteryPoints
        {
            get
            {
                var champ = LiveChampionName;
                if (string.IsNullOrEmpty(champ)) return 0;
                var mastery = _data.TopMasteries.FirstOrDefault(m =>
                    string.Equals(m.ChampionName, champ,
                                  System.StringComparison.OrdinalIgnoreCase));
                return (int)(mastery?.championPoints ?? 0);
            }
        }

        // ── Niveau d'expertise ─────────────────────────────────────────────────
        //  DÉBUTANT      : < 30 000 pts
        //  INTERMÉDIAIRE : 30 000 – 150 000 pts  ET >= 2 parties récentes
        //  EXPERT        : > 150 000 pts          ET >= 3 parties récentes

        public string ExpertiseLabel
        {
            get
            {
                int pts    = CurrentChampionMasteryPoints;
                int recent = ChampionGamesPlayed;

                if (pts >= 150_000 && recent >= 3) return "EXPERT";
                if (pts >= 30_000  && recent >= 2) return "INTERMEDIAIRE";
                return "DEBUTANT";
            }
        }

        public SolidColorBrush ExpertiseBadgeBackground => ExpertiseLabel switch
        {
            "EXPERT"        => new SolidColorBrush(Color.FromArgb(0xCC, 0xCA, 0x8A, 0x04)),
            "INTERMEDIAIRE" => new SolidColorBrush(Color.FromArgb(0xCC, 0x1D, 0x4E, 0xD8)),
            _               => new SolidColorBrush(Color.FromArgb(0xCC, 0x1F, 0x2A, 0x3A)),
        };

        public SolidColorBrush ExpertiseBadgeForeground => ExpertiseLabel switch
        {
            "EXPERT"        => new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
            "INTERMEDIAIRE" => new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),
            _               => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
        };
    }
}
