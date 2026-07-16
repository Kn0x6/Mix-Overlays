using System;
using System.Collections.Generic;

namespace MixOverlays.Models
{
    public class ChampionRecommendation
    {
        public int ChampionId { get; set; }
        public string ChampionName { get; set; } = string.Empty;
        public string ChampionKey { get; set; } = string.Empty;

        public List<int> RuneIds { get; set; } = new();
        public List<int> StartingItemIds { get; set; } = new();
        public List<int> BootsItemIds { get; set; } = new();
        public List<int> CoreItemIds { get; set; } = new();

        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; } = DateTime.Now;
        public bool IsFallback { get; set; }

        public bool HasRunes => RuneIds.Count > 0;
        public bool HasStartingItems => StartingItemIds.Count > 0;
        public bool HasBoots => BootsItemIds.Count > 0;
        public bool HasCoreItems => CoreItemIds.Count > 0;
    }
}