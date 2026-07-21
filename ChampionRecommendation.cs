using System;
using System.Collections.Generic;
using System.Linq;

namespace MixOverlays.Models
{
    public class ChampionRecommendation
    {
        public int ChampionId { get; set; }
        public string ChampionName { get; set; } = string.Empty;
        public string ChampionKey { get; set; } = string.Empty;

        public List<int> RuneIds { get; set; } = new();
        public List<int> ShardIds { get; set; } = new();
        public List<int> StartingItemIds { get; set; } = new();
        public List<int> BootsItemIds { get; set; } = new();
        public List<int> CoreItemIds { get; set; } = new();

        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; } = DateTime.Now;
        public bool IsFallback { get; set; }
        public string Role { get; set; } = string.Empty;
        public double? WinRate { get; set; }
        public int? GameCount { get; set; }

        public int PrimaryStyleId { get; set; }
        public int SecondaryStyleId { get; set; }
        public string PrimaryStyleName { get; set; } = string.Empty;
        public string SecondaryStyleName { get; set; } = string.Empty;
        public List<int> PrimaryRuneIds { get; set; } = new();
        public List<int> SecondaryRuneIds { get; set; } = new();

        public bool HasRunes => RuneIds.Count > 0;
        public bool HasStartingItems => StartingItemIds.Count > 0;
        public bool HasBoots => BootsItemIds.Count > 0;
        public bool HasCoreItems => CoreItemIds.Count > 0;
        public bool HasStats => WinRate.HasValue || GameCount.HasValue;
        public string WinRateDisplay => WinRate.HasValue ? $"{WinRate.Value:0.0}%" : "—";
        public string GameCountDisplay => GameCount.HasValue ? GameCount.Value.ToString("N0") : "—";
        public bool IsCompleteRunePage => PrimaryStyleId > 0 && SecondaryStyleId > 0 &&
                                          PrimaryRuneIds.Count == 4 && SecondaryRuneIds.Count == 2 &&
                                          ShardIds.Count == 3;

        /// <summary>Les neuf perks dans l'ordre attendu par l'API des pages de runes LCU.</summary>
        public List<int> SelectedPerkIds => PrimaryRuneIds
            .Concat(SecondaryRuneIds)
            .Concat(ShardIds)
            .ToList();
    }
}