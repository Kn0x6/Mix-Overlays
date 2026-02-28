namespace MixOverlays.Models
{
    public class AppSettings
    {
        public string RiotApiKey { get; set; } = string.Empty;
        public string Region { get; set; } = "EUW1";
        public string RegionalRoute { get; set; } = "EUROPE";
        public bool AutoStartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool ShowOverlayInGame { get; set; } = true;
        public double OverlayOpacity { get; set; } = 0.92;
        public int OverlayX { get; set; } = 20;
        public int OverlayY { get; set; } = 100;
        public int MatchHistoryCount { get; set; } = 20;
        public bool ShowChampionMastery { get; set; } = true;
        public bool ShowMatchHistory { get; set; } = true;

        // Échelle de l'interface (1.0 = normal, 1.15 = grand, 1.3 = très grand)
        public double UiScale { get; set; } = 1.0;

        // Raccourci clavier pour l'overlay in-game (ex: "Ctrl+X")
        public string OverlayHotkey { get; set; } = "Ctrl+X";

        // Historique de recherche persisté
        public System.Collections.Generic.List<SearchHistoryEntry> SearchHistory { get; set; } = new();
    }

    public class SearchHistoryEntry
    {
        public string Puuid    { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string TagLine  { get; set; } = string.Empty;
    }
}
