namespace MixOverlays.Models
{
    /// <summary>
    /// Extension de PlayerData pour les données live de l'overlay losange.
    /// Ce fichier utilise une partial class — PlayerData doit déjà être déclarée
    /// partial dans PlayerData.cs (ajouter le mot-clé si absent).
    /// </summary>
    public partial class PlayerData
    {
        /// <summary>
        /// ID de la rune keystone équipée pour la partie en cours.
        /// Peuplé depuis SpectatorGameInfo.participants[i].perks.perkIds[0]
        /// dans RiotApiService.
        /// </summary>
        public int LiveRuneId { get; set; }
    }

    // ─── Perks spectateur ────────────────────────────────────────────────────
    // À ajouter dans SpectatorParticipant si absent :
    //   public SpectatorPerks? perks { get; set; }

    public class SpectatorPerks
    {
        /// <summary>Liste ordonnée des IDs de runes. Index 0 = keystone.</summary>
        public System.Collections.Generic.List<int> perkIds { get; set; } = new();

        /// <summary>ID du chemin principal (ex: 8000 = Précision).</summary>
        public int perkStyle { get; set; }

        /// <summary>ID du chemin secondaire.</summary>
        public int perkSubStyle { get; set; }
    }
}
