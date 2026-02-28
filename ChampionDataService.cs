using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MixOverlays.Services
{
    public class ChampionDataService
    {
        private static readonly HttpClient _http = new(); 
        private Dictionary<int, string> _idToName = new();    // id  -> display name ("Vel'Koz")
        private Dictionary<int, string> _idToKey  = new();    // id  -> DDragon key  ("VelKoz")
        private Dictionary<string, int> _nameToId = new();
        private Dictionary<string, string> _nameToKey = new(); // display name -> DDragon key
        private bool _loaded = false;
        private string _latestVersion = "15.1.1";

        // Shared spell ID -> DDragon spell name mapping, populated dynamically
        // Singleton accessible par les converters XAML (stateless)
        public static ChampionDataService? Instance { get; private set; }

        public static Dictionary<int, string> SpellIdToName { get; private set; } = new();

        /// <summary>
        /// Rune (perk) ID -> chemin d'icône DDragon
        /// ex: "perk-images/Styles/Precision/PressTheAttack/PressTheAttack.png"
        /// Peuplé depuis runesReforged.json au démarrage.
        /// </summary>
        public static Dictionary<int, string> RuneIdToIconPath { get; private set; } = new();

        /// <summary>
        /// Ensures the champion data is loaded and updates to the latest Data Dragon version.
        /// Also loads summoner spell data dynamically from DDragon.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            try
            {
                // 1️⃣ Get the latest patch version from Data Dragon
                var versionsJson = await _http.GetStringAsync(
                    "https://ddragon.leagueoflegends.com/api/versions.json");
                var versions = JsonConvert.DeserializeObject<List<string>>(versionsJson);
                var version = versions?[0] ?? "15.1.1";

                _latestVersion = version;
                VersionHolder.Latest = version; // shared with converters

                // 2️⃣ Load champion data for that version
                var champJson = await _http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json");
                var jObj = JObject.Parse(champJson);
                var data = jObj["data"] as JObject;
                if (data != null)
                {
                    foreach (var prop in data.Properties())
                    {
                        // prop.Name  = DDragon key e.g. "VelKoz", "MonkeyKing", "Fiddlesticks"
                        // "name"     = display name e.g. "Vel'Koz", "Wukong", "Fiddlesticks"
                        // "key"      = numeric ID as string e.g. "161"
                        var champObj  = prop.Value;
                        var ddKey     = prop.Name;                              // DDragon key (PascalCase)
                        var name      = champObj["name"]?.Value<string>() ?? string.Empty;
                        var idStr     = champObj["key"]?.Value<string>() ?? "0";
                        
                        if (!int.TryParse(idStr, out var id)) continue;

                        _idToName[id]    = name;
                        _idToKey[id]     = ddKey;
                        _nameToId[name]  = id;
                        _nameToKey[name] = ddKey;

                        // Also map DDragon key -> DDragon key (match API returns the key as championName)
                        if (ddKey != name)
                            _nameToKey[ddKey] = ddKey;
                    }
                }

                // 3️⃣ Load summoner spell data for that version
                await LoadSummonerSpellsAsync(version);

                // 4️⃣ Load rune icons from runesReforged.json
                await LoadRunesAsync(version);

                _loaded = true;
                Instance = this; // expose to static converters
            }
            catch
            {
                // Keep old behavior if the API call fails
            }
        }

        /// <summary>
        /// Loads the summoner spell list from DDragon and populates SpellIdToName.
        /// </summary>
        private async Task LoadSummonerSpellsAsync(string version)
        {
            try
            {
                var spellJson = await _http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/summoner.json");
                var jObj = JObject.Parse(spellJson);
                var data = jObj["data"] as JObject;
                if (data == null) return;

                var newMap = new Dictionary<int, string>();
                foreach (var prop in data.Properties())
                {
                    var spellObj = prop.Value;
                    var keyStr   = spellObj["key"]?.Value<string>() ?? string.Empty;
                    var ddName   = prop.Name; // e.g. "SummonerFlash"
                    if (int.TryParse(keyStr, out var numericId))
                        newMap[numericId] = ddName;
                }
                SpellIdToName = newMap;
            }
            catch
            {
                // Fall back to the static map defined in the converter if this fails
            }
        }

        /// <summary>
        /// Charge les icônes de runes depuis runesReforged.json (Data Dragon).
        /// Chaque rune possède un champ "icon" contenant le chemin relatif, ex:
        /// "perk-images/Styles/Precision/PressTheAttack/PressTheAttack.png"
        /// L'URL complète est : https://ddragon.leagueoflegends.com/cdn/img/{icon}
        /// </summary>
        private async Task LoadRunesAsync(string version)
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/runesReforged.json");
                var paths = JsonConvert.DeserializeObject<List<RunePathDto>>(json);
                if (paths == null) return;

                var newMap = new Dictionary<int, string>();
                foreach (var path in paths)
                {
                    // Icône du path lui-même (utilisée pour la rune secondaire)
                    if (!string.IsNullOrEmpty(path.icon))
                        newMap[path.id] = path.icon;

                    foreach (var slot in path.slots)
                    {
                        foreach (var rune in slot.runes)
                        {
                            if (!string.IsNullOrEmpty(rune.icon))
                                newMap[rune.id] = rune.icon;
                        }
                    }
                }
                RuneIdToIconPath = newMap;
            }
            catch
            {
                // Non critique : la rune restera invisible
            }
        }

        // DTOs internes pour désérialiser runesReforged.json
        private class RunePathDto
        {
            public int id { get; set; }
            public string key { get; set; } = string.Empty;
            public string icon { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public List<RuneSlotDto> slots { get; set; } = new();
        }
        private class RuneSlotDto
        {
            public List<RuneDto> runes { get; set; } = new();
        }
        private class RuneDto
        {
            public int id { get; set; }
            public string key { get; set; } = string.Empty;
            public string icon { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
        }
        public string GetName(int championId)
        {
            if (_idToName.TryGetValue(championId, out var name)) return name;
            return $"Champion {championId}";
        }

        /// <summary>
        /// Returns the DDragon key directly from the champion ID.
        /// This is the most reliable method — avoids any name normalization issues.
        /// </summary>
        public string GetDDragonKeyById(int championId)
        {
            if (_idToKey.TryGetValue(championId, out var key)) return key;
            return string.Empty;
        }

        public int GetId(string championName)
        {
            if (_nameToId.TryGetValue(championName, out var id)) return id;
            return 0;
        }

        /// <summary>
        /// Returns the DDragon key for a champion name.
        /// Accepts both display names ("Vel'Koz") and DDragon keys ("VelKoz").
        /// </summary>
        public string GetDDragonKey(string championName)
        {
            if (string.IsNullOrEmpty(championName)) return string.Empty;

            // Direct lookup (display name or DDragon key)
            if (_nameToKey.TryGetValue(championName, out var key)) return key;

            // Case-insensitive fallback
            foreach (var kvp in _nameToKey)
            {
                if (string.Equals(kvp.Key, championName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            // Last resort: static normalization
            return NormalizeChampionName(championName);
        }

        /// <summary>
        /// Normalizes a champion name for DDragon.
        /// NOTE: Prefer GetDDragonKey() or GetDDragonKeyById() when possible —
        /// they use the loaded data and are always correct.
        /// This static method is only a fallback for when the data isn't loaded yet.
        /// </summary>
        public static string NormalizeChampionName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            // Known special cases — must match the DDragon prop.Name exactly
            var special = name switch
            {
                // Display name  =>  DDragon key (prop.Name from champion.json)
                "Nunu & Willump"  => "Nunu",
                "Dr. Mundo"       => "DrMundo",
                "Wukong"          => "MonkeyKing",
                "Renata Glasc"    => "Renata",
                "Bel'Veth"        => "Belveth",
                "Cho'Gath"        => "Chogath",
                "Kai'Sa"          => "Kaisa",
                "Kha'Zix"         => "Khazix",
                "Kog'Maw"         => "KogMaw",
                "LeBlanc"         => "Leblanc",
                "Lee Sin"         => "LeeSin",
                "Master Yi"       => "MasterYi",
                "Miss Fortune"    => "MissFortune",
                "Rek'Sai"         => "RekSai",
                "Tahm Kench"      => "TahmKench",
                "Twisted Fate"    => "TwistedFate",
                "Vel'Koz"         => "VelKoz",
                "Xin Zhao"        => "XinZhao",
                "Aurelion Sol"    => "AurelionSol",
                "Jarvan IV"       => "JarvanIV",
                // Note: "Fiddlesticks" DDragon key IS "Fiddlesticks" (no capital S)
                // Do NOT add it here — general path handles it correctly
                _                 => null
            };

            if (special != null) return special;

            // General: strip special chars, keep PascalCase intact
            // Do NOT call ToLowerInvariant() — DDragon keys are PascalCase
            return name
                .Replace("'", "")
                .Replace(" ", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("&", "")
                .Replace(",", "")
                .Replace(":", "")
                .Replace("!", "")
                .Replace("?", "");
        }

        /// <summary>
        /// Generates a champion icon URL using the champion ID (most reliable).
        /// </summary>
        public string GetIconUrlById(int championId, string? version = null)
        {
            var versionToUse = version ?? _latestVersion;
            var key = GetDDragonKeyById(championId);
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return $"https://ddragon.leagueoflegends.com/cdn/{versionToUse}/img/champion/{key}.png";
        }

        /// <summary>
        /// Generates a champion icon URL using the champion name.
        /// </summary>
        public string GetIconUrl(string championName, string? version = null)
        {
            if (string.IsNullOrEmpty(championName)) return string.Empty;
            var versionToUse = version ?? _latestVersion;
            var key = GetDDragonKey(championName);
            if (string.IsNullOrEmpty(key))
                key = NormalizeChampionName(championName);
            return $"https://ddragon.leagueoflegends.com/cdn/{versionToUse}/img/champion/{key}.png";
        }

        public string GetProfileIconUrl(int iconId) =>
            $"https://ddragon.leagueoflegends.com/cdn/{_latestVersion}/img/profileicon/{iconId}.png";

        public string LatestVersion => _latestVersion;
    }
}
