using System.Collections.Generic;
using MixOverlays.ViewModels;

namespace MixOverlays.Models
{
    /// <summary>
    /// Représente une paire de participants d'une même lane (allié vs ennemi)
    /// </summary>
    public class MatchParticipantPair
    {
        public MatchParticipantViewModel Ally { get; set; } = new();
        public MatchParticipantViewModel Enemy { get; set; } = new();
        public string Lane { get; set; } = string.Empty;
    }

    /// <summary>
    /// Représente une lane avec ses participants (alliés et ennemis)
    /// </summary>
    public class MatchLaneViewModel
    {
        public string Lane { get; set; } = string.Empty;
        public List<MatchParticipantViewModel> AllyPlayers { get; set; } = new();
        public List<MatchParticipantViewModel> EnemyPlayers { get; set; } = new();
        
        /// <summary>
        /// Retourne true si cette lane contient des joueurs des deux équipes
        /// </summary>
        public bool HasBothTeams => AllyPlayers.Count > 0 && EnemyPlayers.Count > 0;
        
        /// <summary>
        /// Retourne true si cette lane contient au moins un joueur
        /// </summary>
        public bool HasAnyPlayers => AllyPlayers.Count > 0 || EnemyPlayers.Count > 0;
    }

    /// <summary>
    /// Extension pour organiser les participants par lane
    /// </summary>
    public static class MatchParticipantExtensions
    {
        /// <summary>
        /// Convertit les participants d'un match en paires par lane
        /// </summary>
        public static List<MatchParticipantPair> ToLanePairs(this List<MatchParticipantViewModel> participants, string playerPuuid)
        {
            var pairs = new List<MatchParticipantPair>();
            
            // Exclure le joueur principal
            var filteredParticipants = participants.FindAll(p => p.Puuid != playerPuuid);
            
            // Grouper par équipe et position
            var allyTeam = filteredParticipants.FindAll(p => p.Win);
            var enemyTeam = filteredParticipants.FindAll(p => !p.Win);
            
            // Définir l'ordre des lanes
            var laneOrder = new[] { "TOP", "JUNGLE", "MID", "ADC", "SUPPORT" };
            
            foreach (var lane in laneOrder)
            {
                var allyPlayers = allyTeam.FindAll(p => GetLaneFromPosition(p.Position) == lane);
                var enemyPlayers = enemyTeam.FindAll(p => GetLaneFromPosition(p.Position) == lane);
                
                // Apparier les joueurs de chaque côté
                var maxCount = System.Math.Max(allyPlayers.Count, enemyPlayers.Count);
                
                for (int i = 0; i < maxCount; i++)
                {
                    var pair = new MatchParticipantPair
                    {
                        Lane = lane
                    };
                    
                    if (i < allyPlayers.Count)
                        pair.Ally = allyPlayers[i];
                    
                    if (i < enemyPlayers.Count)
                        pair.Enemy = enemyPlayers[i];
                    
                    // Ne pas ajouter les paires vides
                    if (pair.Ally.Puuid != null || pair.Enemy.Puuid != null)
                        pairs.Add(pair);
                }
            }
            
            return pairs;
        }

        /// <summary>
        /// Convertit les participants en vue par lane pour un affichage plus structuré
        /// </summary>
        public static List<MatchLaneViewModel> ToLanes(this List<MatchParticipantViewModel> participants, string playerPuuid)
        {
            var lanes = new List<MatchLaneViewModel>();
            
            // Exclure le joueur principal
            var filteredParticipants = participants.FindAll(p => p.Puuid != playerPuuid);
            
            // Grouper par équipe et position
            var allyTeam = filteredParticipants.FindAll(p => p.Win);
            var enemyTeam = filteredParticipants.FindAll(p => !p.Win);
            
            // Définir l'ordre des lanes
            var laneOrder = new[] { "TOP", "JUNGLE", "MID", "ADC", "SUPPORT" };
            
            foreach (var lane in laneOrder)
            {
                var laneViewModel = new MatchLaneViewModel { Lane = lane };
                
                laneViewModel.AllyPlayers = allyTeam.FindAll(p => GetLaneFromPosition(p.Position) == lane);
                laneViewModel.EnemyPlayers = enemyTeam.FindAll(p => GetLaneFromPosition(p.Position) == lane);
                
                if (laneViewModel.HasAnyPlayers)
                    lanes.Add(laneViewModel);
            }
            
            return lanes;
        }

        /// <summary>
        /// Convertit une position en lane standardisée
        /// </summary>
        private static string GetLaneFromPosition(string position)
        {
            if (string.IsNullOrEmpty(position))
                return "UNKNOWN";
                
            return position.ToUpper() switch
            {
                "TOP" or "TOPLANE" => "TOP",
                "JUNGLE" or "JUNG" => "JUNGLE",
                "MID" or "MIDDLE" => "MID",
                "ADC" or "BOTTOM" or "BOT" => "ADC",
                "SUPPORT" or "UTILITY" => "SUPPORT",
                _ => position.ToUpper()
            };
        }
    }
}