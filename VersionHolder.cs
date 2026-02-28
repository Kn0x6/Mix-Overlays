namespace MixOverlays.Services
{
    /// <summary>
    /// Shared singleton holding the current DDragon version.
    /// Set once during app startup by ChampionDataService.EnsureLoadedAsync().
    /// Converters read from here to always use the correct patch version.
    /// </summary>
    public static class VersionHolder
    {
        private static string _latest = "15.1.1";

        /// <summary>
        /// The latest DDragon patch version. Updated by ChampionDataService at startup.
        /// </summary>
        public static string Latest
        {
            get => _latest;
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    _latest = value;
            }
        }
    }
}
