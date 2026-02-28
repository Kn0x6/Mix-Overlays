using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MixOverlays.Models;
using MixOverlays.Services;

namespace MixOverlays.Converters
{
    // ─── Bool / Visibility ────────────────────────────────────────────────────
    // ─── Shared image cache (prevents stack overflow from repeated BitmapImage creation) ──
    internal static class ImageCache
    {
        public static readonly Dictionary<string, BitmapImage> Shared = new();
    }

    // ─── Safe BitmapImage factory (prevents stack overflow from repeated BitmapImage creation) ──
    internal static class SafeBitmapImageFactory
    {
        private static readonly Dictionary<string, BitmapImage> _cache = ImageCache.Shared;

        public static BitmapImage? Create(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (_cache.TryGetValue(url, out var cached)) return cached;

            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource     = new Uri(url, UriKind.Absolute);
                bi.CacheOption   = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.None;
                bi.EndInit();
                // Ne pas appeler Freeze() pour éviter les exceptions InvalidOperationException
                // bi.Freeze(); // thread-safe, libère des ressources WPF
                _cache[url] = bi;
                return bi;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SafeBitmapImageFactory] Erreur: {ex.Message}");
                return null;
            }
        }
    }


    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility v && v == Visibility.Visible;
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility v && v != Visibility.Visible;
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Win/Loss Color ───────────────────────────────────────────────────────

    public class WinLossBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is bool win)
                return win
                    ? new SolidColorBrush(Color.FromRgb(35, 134, 54))
                    : new SolidColorBrush(Color.FromRgb(218, 54, 51));
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class WinLossTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b ? (b ? "V" : "D") : "?";
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Rank tier color ──────────────────────────────────────────────────────

    public class RankTierBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var tier = value?.ToString()?.ToUpper() ?? string.Empty;
            return tier switch
            {
                "IRON"        => new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                "BRONZE"      => new SolidColorBrush(Color.FromRgb(205, 127, 50)),
                "SILVER"      => new SolidColorBrush(Color.FromRgb(192, 192, 192)),
                "GOLD"        => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                "PLATINUM"    => new SolidColorBrush(Color.FromRgb(0, 181, 216)),
                "EMERALD"     => new SolidColorBrush(Color.FromRgb(80, 200, 120)),
                "DIAMOND"     => new SolidColorBrush(Color.FromRgb(185, 242, 255)),
                "MASTER"      => new SolidColorBrush(Color.FromRgb(157, 77, 201)),
                "GRANDMASTER" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                "CHALLENGER"  => new SolidColorBrush(Color.FromRgb(240, 192, 64)),
                _             => new SolidColorBrush(Color.FromRgb(139, 148, 158))
            };
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── KDA Color ───────────────────────────────────────────────────────────

    public class KdaBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double kda)
            {
                if (kda >= 5.0) return new SolidColorBrush(Color.FromRgb(240, 192, 64));
                if (kda >= 3.0) return new SolidColorBrush(Color.FromRgb(80, 200, 120));
                if (kda >= 2.0) return new SolidColorBrush(Color.FromRgb(225, 225, 225));
                return new SolidColorBrush(Color.FromRgb(139, 148, 158));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Win Rate color ───────────────────────────────────────────────────────

    public class WinRateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double wr)
            {
                if (wr >= 60) return new SolidColorBrush(Color.FromRgb(80, 200, 120));
                if (wr >= 50) return new SolidColorBrush(Color.FromRgb(225, 225, 225));
                return new SolidColorBrush(Color.FromRgb(218, 54, 51));
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Number formatters ───────────────────────────────────────────────────

    public class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double d) return d.ToString("F2");
            return "0.00";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class PercentFormatConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is double d) return $"{d:F0}%";
            return "0%";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class MasteryPointsConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is long pts)
            {
                if (pts >= 1_000_000) return $"{pts / 1_000_000.0:F1}M";
                if (pts >= 1_000)     return $"{pts / 1_000.0:F0}K";
                return pts.ToString();
            }
            return "0";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class GameDurationConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is long secs)
            {
                var ts = TimeSpan.FromSeconds(secs);
                return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            }
            return "0:00";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class TimeAgoConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is long ms)
            {
                var ts = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ms);
                if (ts.TotalDays >= 365) return $"il y a {(int)(ts.TotalDays / 365)} an(s)";
                if (ts.TotalDays >= 30)  return $"il y a {(int)(ts.TotalDays / 30)} mois";
                if (ts.TotalDays >= 1)   return $"il y a {(int)ts.TotalDays} j";
                if (ts.TotalHours >= 1)  return $"il y a {(int)ts.TotalHours} h";
                return $"il y a {(int)ts.TotalMinutes} min";
            }
            return string.Empty;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── String hex to Brush ──────────────────────────────────────────────────

    public class HexStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            try
            {
                var hex   = value?.ToString() ?? "#888888";
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { return Brushes.Gray; }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── URL string → BitmapImage (safe, never throws) ───────────────────────

    public class UrlToImageSourceConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage> _cache
            = ImageCache.Shared;

        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            var url = value?.ToString();
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (_cache.TryGetValue(url, out var cached)) return cached;

            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource     = new Uri(url, UriKind.Absolute);
                bi.CacheOption   = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.None;
                bi.EndInit();
                // Ne pas appeler Freeze() pour éviter les exceptions InvalidOperationException
                // bi.Freeze(); // thread-safe, libère des ressources WPF
                _cache[url] = bi;
                return bi;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UrlToImageSourceConverter] Erreur: {ex.Message}");
                return null;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── URL string → BitmapImage with fallback (safe, never throws) ─────────

    public class UrlToImageSourceWithFallbackConverter : IValueConverter
    {
        private static readonly Dictionary<string, BitmapImage> _cache
            = ImageCache.Shared;

        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            var url = value?.ToString();
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (_cache.TryGetValue(url, out var cached)) return cached;

            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource     = new Uri(url, UriKind.Absolute);
                bi.CacheOption   = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.None;
                bi.DecodeFailed += (s, e) =>
                    System.Diagnostics.Debug.WriteLine($"[Image] Échec décodage: {url}");
                bi.EndInit();
                // Ne pas appeler Freeze() pour éviter les exceptions InvalidOperationException
                // bi.Freeze();
                _cache[url] = bi;
                return bi;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Image] Exception: {url} — {ex.Message}");
                return null;
            }
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Champion name → URL string ──────────────────────────────────────────
    // Utilise le ChampionName retourné par l'API (clé DDragon, ex: "VelKoz")
    // + lookup dans ChampionDataService.Instance pour les cas ambigus

    public class ChampionIconUrlConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var name = value?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var key     = ResolveKey(name);
            var version = VersionHolder.Latest;
            return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/champion/{key}.png";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();

        internal static string ResolveKey(string name)
        {
            // 1. Try the live loaded data (most reliable)
            var key = ChampionDataService.Instance?.GetDDragonKey(name);
            if (!string.IsNullOrEmpty(key)) return key;

            // 2. Fallback to static normalization
            return ChampionDataService.NormalizeChampionName(name);
        }
    }

    // ─── Champion name → BitmapImage ─────────────────────────────────────────

    public class ChampionNameToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            var name = value?.ToString();
            if (string.IsNullOrWhiteSpace(name)) return null;

            var key     = ChampionIconUrlConverter.ResolveKey(name);
            var version = VersionHolder.Latest;
            var url     = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/champion/{key}.png";
            return SafeBitmapImageFactory.Create(url);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Champion ID → URL string ─────────────────────────────────────────────
    // Plus fiable que le nom : utilise directement la map id -> clé DDragon

    public class ChampionIdToUrlConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is int id)
            {
                var key = ChampionDataService.Instance?.GetDDragonKeyById(id);
                if (string.IsNullOrEmpty(key)) return string.Empty;
                var version = VersionHolder.Latest;
                return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/champion/{key}.png";
            }
            return string.Empty;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Champion ID → BitmapImage ────────────────────────────────────────────

    public class ChampionIdToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int id) return null;

            var key = ChampionDataService.Instance?.GetDDragonKeyById(id);
            if (string.IsNullOrEmpty(key)) return null;

            var version = VersionHolder.Latest;
            var url     = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/champion/{key}.png";
            return SafeBitmapImageFactory.Create(url);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Summoner Spell ID → URL via Data Dragon ─────────────────────────────

    public class SummonerSpellIdToUrlConverter : IValueConverter
    {
        // Fallback statique — utilisé uniquement si le chargement DDragon dynamique échoue.
        // IDs corrects issus de summoner.json Riot officiel.
        private static readonly Dictionary<int, string> FallbackSpellNames = new()
        {
            [1]  = "SummonerBoost",      // Cleanse
            [3]  = "SummonerExhaust",
            [4]  = "SummonerFlash",
            [6]  = "SummonerHaste",      // Ghost
            [7]  = "SummonerHeal",
            [11] = "SummonerSmite",
            [12] = "SummonerTeleport",
            [13] = "SummonerMana",       // Clarity
            [14] = "SummonerDot",        // Ignite
            [21] = "SummonerBarrier",
            [30] = "SummonerPoroRecall",
            [31] = "SummonerPoroThrow",
            [32] = "SummonerSnowball",
        };

        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int spellId) return null;

            string? spellName = null;

            // Priorité 1 : map chargée dynamiquement depuis DDragon
            var dynamicMap = ChampionDataService.SpellIdToName;
            if (dynamicMap.Count > 0)
                dynamicMap.TryGetValue(spellId, out spellName);

            // Priorité 2 : fallback statique
            if (string.IsNullOrEmpty(spellName))
                FallbackSpellNames.TryGetValue(spellId, out spellName);

            if (string.IsNullOrEmpty(spellName))
            {
                System.Diagnostics.Debug.WriteLine($"[SummonerSpell] ID inconnu: {spellId}");
                return null;
            }

            var version = VersionHolder.Latest;
            return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{spellName}.png";
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Summoner Spell ID → BitmapImage ─────────────────────────────────────

    public class SummonerSpellIdToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int spellId) return null;

            // Protection contre les IDs 0
            if (spellId == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SummonerSpell] ID 0 ignoré");
                return null;
            }

            string? spellName = null;

            // Priorité 1 : map chargée dynamiquement depuis DDragon
            var dynamicMap = ChampionDataService.SpellIdToName;
            if (dynamicMap.Count > 0)
            {
                dynamicMap.TryGetValue(spellId, out spellName);
                if (!string.IsNullOrEmpty(spellName))
                    System.Diagnostics.Debug.WriteLine($"[SummonerSpell] ID {spellId} → {spellName} (dynamique)");
            }

            // Priorité 2 : fallback statique
            if (string.IsNullOrEmpty(spellName))
            {
                var fallback = new Dictionary<int, string>
                {
                    [1]  = "SummonerBoost",
                    [3]  = "SummonerExhaust",
                    [4]  = "SummonerFlash",
                    [6]  = "SummonerHaste",
                    [7]  = "SummonerHeal",
                    [11] = "SummonerSmite",
                    [12] = "SummonerTeleport",
                    [13] = "SummonerMana",
                    [14] = "SummonerDot",
                    [21] = "SummonerBarrier",
                    [30] = "SummonerPoroRecall",
                    [31] = "SummonerPoroThrow",
                    [32] = "SummonerSnowball",
                };
                fallback.TryGetValue(spellId, out spellName);
                if (!string.IsNullOrEmpty(spellName))
                    System.Diagnostics.Debug.WriteLine($"[SummonerSpell] ID {spellId} → {spellName} (fallback)");
            }

            if (string.IsNullOrEmpty(spellName))
            {
                System.Diagnostics.Debug.WriteLine($"[SummonerSpell] ID inconnu: {spellId}");
                return null;
            }

            var version = VersionHolder.Latest;
            var url     = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{spellName}.png";
            var image   = SafeBitmapImageFactory.Create(url);
            if (image == null)
                System.Diagnostics.Debug.WriteLine($"[SummonerSpell] Image échec pour {spellName}");
            return image;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Item ID → BitmapImage ────────────────────────────────────────────────

    public class ItemIdToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int id || id == 0) return null;
            var version = VersionHolder.Latest;
            var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/img/item/{id}.png";
            return SafeBitmapImageFactory.Create(url);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Rune (Perk) ID → URL string ─────────────────────────────────────────
    // Les icônes sont servies via Data Dragon : https://ddragon.leagueoflegends.com/cdn/img/{icon}
    // Le chemin "icon" est chargé dynamiquement depuis runesReforged.json par ChampionDataService.

    public class RuneIdToUrlConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int id || id == 0) return null;

            var map = ChampionDataService.RuneIdToIconPath;
            if (map.TryGetValue(id, out var iconPath) && !string.IsNullOrEmpty(iconPath))
                return $"https://ddragon.leagueoflegends.com/cdn/img/{iconPath}";

            return null;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Rune (Perk) ID → BitmapImage ────────────────────────────────────────

    public class RuneIdToImageConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is not int id || id == 0) return null;

            var map = ChampionDataService.RuneIdToIconPath;
            if (!map.TryGetValue(id, out var iconPath) || string.IsNullOrEmpty(iconPath))
                return null;

            var url = $"https://ddragon.leagueoflegends.com/cdn/img/{iconPath}";
            return SafeBitmapImageFactory.Create(url);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    // ─── Champion name → Splash Art BitmapImage ───────────────────────────────
    // Retourne l'image de splash art du champion (skin 0 = splash par défaut)
    // URL: https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{Key}_0.jpg

    public class ChampionNameToSplashConverter : IValueConverter
    {
        public object? Convert(object value, Type t, object p, CultureInfo c)
        {
            var name = value?.ToString();
            if (string.IsNullOrWhiteSpace(name)) return null;

            var key = ChampionIconUrlConverter.ResolveKey(name);
            if (string.IsNullOrEmpty(key)) return null;

            var url = $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{key}_0.jpg";
            return SafeBitmapImageFactory.Create(url);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
