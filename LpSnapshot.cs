using System;

namespace MixOverlays.Models
{
    /// <summary>
    /// Représente un instantané des LP à un moment donné.
    /// Stocké avant et après chaque partie classée pour calculer le delta.
    /// </summary>
    public class LpSnapshot
    {
        /// <summary>Date et heure de la capture.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>LP au moment de la capture (0–100, ou valeur absolue si on calcule la cumul).</summary>
        public int LeaguePoints { get; set; }

        /// <summary>Tier au moment de la capture (ex: "GOLD", "PLATINUM").</summary>
        public string Tier { get; set; } = string.Empty;

        /// <summary>Division au moment de la capture (ex: "I", "II", "III", "IV").</summary>
        public string Rank { get; set; } = string.Empty;

        /// <summary>
        /// Delta LP par rapport au snapshot précédent (+N ou -N).
        /// null si c'est le tout premier snapshot ou si la partie a été ignorée.
        /// </summary>
        public int? LpDelta { get; set; }

        /// <summary>Résultat de la partie qui a conduit à ce snapshot (true=V, false=D, null=inconnu).</summary>
        public bool? IsWin { get; set; }

        /// <summary>Nom du champion joué pendant la partie (peut être vide).</summary>
        public string ChampionName { get; set; } = string.Empty;

        // ── Helpers affichage ──────────────────────────────────────────────────

        /// <summary>Rang lisible : "Gold I", "Platinum IV", etc.</summary>
        public string RankDisplay =>
            string.IsNullOrEmpty(Tier) ? "Unranked" : $"{FormatTier(Tier)} {Rank}";

        /// <summary>Delta formaté avec signe : "+21 LP", "−18 LP".</summary>
        public string DeltaDisplay => LpDelta.HasValue
            ? (LpDelta.Value >= 0 ? $"+{LpDelta.Value} LP" : $"{LpDelta.Value} LP")
            : string.Empty;

        private static string FormatTier(string t) => t.Length > 0
            ? char.ToUpper(t[0]) + t.Substring(1).ToLower()
            : t;
    }
}
