using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MixOverlays.Models;
using Newtonsoft.Json.Linq;

namespace MixOverlays.Services
{
    public interface IChampionRecommendationProvider
    {
        string SourceName { get; }
        Task<ChampionRecommendation?> GetRecommendationAsync(int championId, string championName, string championKey);
    }

    public class ChampionRecommendationService
    {
        private readonly ChampionDataService _champions;
        private readonly List<IChampionRecommendationProvider> _providers;
        private readonly Dictionary<int, ChampionRecommendation> _cache = new();

        public ChampionRecommendationService(ChampionDataService champions)
        {
            _champions = champions;
            _providers = new List<IChampionRecommendationProvider>
            {
                new UggChampionRecommendationProvider(),
                new CuratedFallbackRecommendationProvider()
            };
        }

        public async Task<ChampionRecommendation?> GetRecommendationAsync(int championId)
        {
            if (championId <= 0) return null;

            if (_cache.TryGetValue(championId, out var cached) &&
                DateTime.Now - cached.LoadedAt < TimeSpan.FromMinutes(30))
                return cached;

            await _champions.EnsureLoadedAsync();
            var championName = _champions.GetName(championId);
            var championKey = _champions.GetDDragonKeyById(championId);

            if (string.IsNullOrWhiteSpace(championKey))
                championKey = ChampionDataService.NormalizeChampionName(championName);

            foreach (var provider in _providers)
            {
                try
                {
                    var recommendation = await provider.GetRecommendationAsync(championId, championName, championKey);
                    if (recommendation == null) continue;

                    recommendation.ChampionId = championId;
                    recommendation.ChampionName = championName;
                    recommendation.ChampionKey = championKey;
                    recommendation.LoadedAt = DateTime.Now;

                    App.Log(
                        $"[Recommendations] champion={championName} id={championId} source={recommendation.SourceName} fallback={recommendation.IsFallback} " +
                        $"runes=[{string.Join(',', recommendation.RuneIds)}] start=[{string.Join(',', recommendation.StartingItemIds)}] " +
                        $"boots=[{string.Join(',', recommendation.BootsItemIds)}] core=[{string.Join(',', recommendation.CoreItemIds)}] url={recommendation.SourceUrl}");

                    _cache[championId] = recommendation;
                    return recommendation;
                }
                catch (Exception ex)
                {
                    App.Log($"[Recommendations] Provider {provider.SourceName} erreur: {ex.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Provider communautaire en ligne. U.GG ne propose pas d'API publique stable ;
    /// cette intégration lit donc les données JSON embarquées dans la page et extrait
    /// les IDs de runes/items les plus probables. La logique est isolée pour pouvoir
    /// remplacer facilement la source si le site change son format.
    /// </summary>
    internal class UggChampionRecommendationProvider : IChampionRecommendationProvider
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly HashSet<int> KnownRuneIds = new()
        {
            8005, 8008, 8010, 8021, 8112, 8124, 8128, 9923,
            8214, 8229, 8230, 8437, 8439, 8465, 8351, 8360, 8369
        };

        public string SourceName => "U.GG";

        public async Task<ChampionRecommendation?> GetRecommendationAsync(int championId, string championName, string championKey)
        {
            var slug = ToUggSlug(championKey);
            var url = $"https://u.gg/lol/champions/{slug}/build";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 MixOverlays/1.0");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var html = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(html)) return null;

            var json = ExtractNextDataJson(html);
            var recommendation = !string.IsNullOrWhiteSpace(json)
                ? BuildFromJson(championId, championName, championKey, url, json)
                : null;

            if (recommendation == null) return null;
            if (!recommendation.HasRunes && !recommendation.HasCoreItems) return null;
            return recommendation;
        }

        private static string? ExtractNextDataJson(string html)
        {
            var match = Regex.Match(
                html,
                "<script[^>]+id=\\\"__NEXT_DATA__\\\"[^>]*>(?<json>.*?)</script>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return match.Success
                ? System.Net.WebUtility.HtmlDecode(match.Groups["json"].Value)
                : null;
        }

        private static ChampionRecommendation? BuildFromJson(int championId, string championName, string championKey, string url, string json)
        {
            try
            {
                var root = JToken.Parse(json);
                var runeIds = ExtractStructuredIds(root, IsRuneProperty, id => KnownRuneIds.Contains(id), 6);
                var itemIds = ExtractStructuredIds(root, IsBuildItemProperty, IsLikelyItemId, 9);

                var distinctRunes = runeIds.Distinct().Take(6).ToList();
                var distinctItems = itemIds.Distinct().Take(9).ToList();

                // Si on n'arrive pas à identifier un vrai bloc de build structuré,
                // ne pas utiliser une extraction globale de nombres de la page : elle peut
                // mélanger matchups, builds alternatifs ou données internes U.GG.
                if (distinctItems.Count < 3) return null;

                return new ChampionRecommendation
                {
                    ChampionId = championId,
                    ChampionName = championName,
                    ChampionKey = championKey,
                    RuneIds = distinctRunes,
                    StartingItemIds = distinctItems.Take(2).ToList(),
                    BootsItemIds = distinctItems.Where(IsBoots).Take(1).ToList(),
                    CoreItemIds = distinctItems.Where(id => !IsBoots(id)).Skip(2).Take(6).ToList(),
                    SourceName = "U.GG",
                    SourceUrl = url
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<int> ExtractStructuredIds(JToken root, Func<string, bool> propertyMatcher, Func<int, bool> idMatcher, int maxCount)
        {
            var best = new List<int>();

            foreach (var prop in EnumerateDescendantsAndSelf(root).OfType<JProperty>())
            {
                if (!propertyMatcher(prop.Name)) continue;

                var ids = EnumerateDescendantsAndSelf(prop.Value)
                    .Where(t => t.Type == JTokenType.Integer)
                    .Select(t => t.Value<int>())
                    .Where(idMatcher)
                    .Distinct()
                    .Take(maxCount)
                    .ToList();

                if (ids.Count > best.Count) best = ids;
                if (best.Count >= maxCount) break;
            }

            return best;
        }

        private static IEnumerable<JToken> EnumerateDescendantsAndSelf(JToken token)
        {
            yield return token;

            foreach (var child in token.Children())
            {
                foreach (var descendant in EnumerateDescendantsAndSelf(child))
                    yield return descendant;
            }
        }

        private static bool IsRuneProperty(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("rune") || lower.Contains("perk");
        }

        private static bool IsBuildItemProperty(string name)
        {
            var lower = name.ToLowerInvariant();
            return lower.Contains("recommended") ||
                   lower.Contains("coreitem") ||
                   lower.Contains("core_item") ||
                   lower.Contains("startingitem") ||
                   lower.Contains("starting_item") ||
                   lower.Contains("itembuild") ||
                   lower.Contains("item_build") ||
                   lower == "items";
        }

        private static bool IsLikelyItemId(int id) => id is >= 1001 and <= 9999 && !KnownRuneIds.Contains(id);
        private static bool IsBoots(int id) => id is 3006 or 3009 or 3020 or 3047 or 3111 or 3117 or 3158;

        private static string ToUggSlug(string championKey)
        {
            if (string.Equals(championKey, "MonkeyKing", StringComparison.OrdinalIgnoreCase)) return "wukong";
            return Regex.Replace(championKey, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
        }
    }

    /// <summary>
    /// Fallback local inspiré des pages communautaires courantes. Il garantit que le
    /// panneau reste utile si la source externe est temporairement indisponible.
    /// </summary>
    internal class CuratedFallbackRecommendationProvider : IChampionRecommendationProvider
    {
        public string SourceName => "Fallback local";

        private static readonly HashSet<string> Marksmen = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ashe", "Caitlyn", "Draven", "Ezreal", "Jhin", "Jinx", "Kaisa", "Kalista", "KogMaw",
            "Lucian", "MissFortune", "Nilah", "Samira", "Sivir", "Tristana", "Twitch", "Varus", "Vayne", "Xayah", "Zeri", "Smolder"
        };

        private static readonly HashSet<string> Tanks = new(StringComparer.OrdinalIgnoreCase)
        {
            "Alistar", "Amumu", "Braum", "Chogath", "DrMundo", "Galio", "Leona", "Malphite", "Maokai", "Nautilus",
            "Ornn", "Poppy", "Rammus", "Rell", "Sejuani", "Shen", "Sion", "TahmKench", "Taric", "Zac"
        };

        private static readonly HashSet<string> Mages = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ahri", "Anivia", "Annie", "AurelionSol", "Azir", "Brand", "Cassiopeia", "Hwei", "Karthus", "LeBlanc", "Lissandra",
            "Lux", "Malzahar", "Neeko", "Orianna", "Ryze", "Syndra", "Taliyah", "TwistedFate", "Veigar", "VelKoz", "Vex", "Viktor", "Xerath", "Ziggs", "Zoe"
        };

        private static readonly HashSet<string> ApBruisers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Mordekaiser", "Gwen", "Rumble", "Singed", "Sylas", "Diana", "Lillia", "Shyvana"
        };

        public Task<ChampionRecommendation?> GetRecommendationAsync(int championId, string championName, string championKey)
        {
            ChampionRecommendation recommendation;

            if (Marksmen.Contains(championKey))
            {
                recommendation = new ChampionRecommendation
                {
                    RuneIds = new List<int> { 8005, 8009, 9103, 8014, 8345, 8347 },
                    StartingItemIds = new List<int> { 1055, 2003 },
                    BootsItemIds = new List<int> { 3006 },
                    CoreItemIds = new List<int> { 3031, 6672, 3036 }
                };
            }
            else if (Tanks.Contains(championKey))
            {
                recommendation = new ChampionRecommendation
                {
                    RuneIds = new List<int> { 8437, 8446, 8429, 8451, 8345, 8347 },
                    StartingItemIds = new List<int> { 1054, 2003 },
                    BootsItemIds = new List<int> { 3047 },
                    CoreItemIds = new List<int> { 3068, 3075, 8020 }
                };
            }
            else if (ApBruisers.Contains(championKey))
            {
                recommendation = new ChampionRecommendation
                {
                    RuneIds = new List<int> { 8010, 9111, 9105, 8299, 8444, 8451 },
                    StartingItemIds = new List<int> { 1056, 2003 },
                    BootsItemIds = new List<int> { 3047 },
                    CoreItemIds = new List<int> { 4633, 3116, 6653 }
                };
            }
            else if (Mages.Contains(championKey))
            {
                recommendation = new ChampionRecommendation
                {
                    RuneIds = new List<int> { 8229, 8226, 8210, 8237, 8345, 8347 },
                    StartingItemIds = new List<int> { 1056, 2003 },
                    BootsItemIds = new List<int> { 3020 },
                    CoreItemIds = new List<int> { 6655, 3089, 3135 }
                };
            }
            else
            {
                recommendation = new ChampionRecommendation
                {
                    RuneIds = new List<int> { 8010, 9111, 9104, 8299, 8444, 8451 },
                    StartingItemIds = new List<int> { 1055, 2003 },
                    BootsItemIds = new List<int> { 3047 },
                    CoreItemIds = new List<int> { 6631, 3071, 6333 }
                };
            }

            recommendation.ChampionId = championId;
            recommendation.ChampionName = championName;
            recommendation.ChampionKey = championKey;
            recommendation.SourceName = "Fallback local";
            recommendation.SourceUrl = "https://u.gg/lol/champions";
            recommendation.IsFallback = true;

            return Task.FromResult<ChampionRecommendation?>(recommendation);
        }
    }
}