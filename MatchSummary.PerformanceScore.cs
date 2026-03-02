// ─── INSTRUCTIONS D'INTÉGRATION ─────────────────────────────────────────────
//
// Dans PlayerData.cs, changer la ligne :
//
//   public class MatchSummary
//
// en :
//
//   public partial class MatchSummary
//
// Puis ajouter ce fichier au projet dans Models/MatchSummary.PerformanceScore.cs
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace MixOverlays.Models
{
    public partial class MatchSummary
    {
        // ── Score de performance (0..100) ─────────────────────────────────────
        //
        // Formule :
        //   KDA  → 40 pts (plafonné à KDA = 6)
        //   CS   → 30 pts (référence 7 CS/min)
        //   Win  → 30 pts (bonus victoire)
        //
        // Valeurs spéciales :
        //   100 → badge MVP (victoire + KDA ≥ 5 + CS/min ≥ 6)
        //    99 → badge ACE (KDA ≥ 4 et score ≥ 75, toute issue)

        public int PerformanceScore
        {
            get
            {
                if (GameDuration <= 0) return 0;

                double minutes    = GameDuration / 60.0;
                double kda        = Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;
                double csPerMin   = minutes > 0 ? CS / minutes : 0;

                double kdaScore   = Math.Min(kda / 6.0, 1.0) * 40.0;
                double csScore    = Math.Min(csPerMin / 7.0, 1.0) * 30.0;
                double winScore   = Win ? 30.0 : 0.0;

                int raw = Math.Clamp((int)Math.Round(kdaScore + csScore + winScore), 0, 98);

                if (Win && kda >= 5.0 && csPerMin >= 6.0) return 100;
                if (kda >= 4.0 && raw >= 75)              return 99;

                return raw;
            }
        }

        public string PerformanceBadge => PerformanceScore switch
        {
            100 => "MVP",
            99  => "ACE",
            _   => string.Empty
        };
    }
}
