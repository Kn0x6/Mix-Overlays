using System;
using System.IO;
using MixOverlays.Models;
using Newtonsoft.Json;

namespace MixOverlays.Services
{
    public class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MixOverlays", "settings.json");

        public AppSettings Current { get; private set; }

        public SettingsService()
        {
            Current = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public static readonly string[] Regions = { "EUW1", "EUN1", "NA1", "KR", "JP1", "BR1", "LA1", "LA2", "OC1", "TR1", "RU" };

        public static string GetRegionalRoute(string platform) => platform.ToUpper() switch
        {
            "EUW1" or "EUN1" or "TR1" or "RU" => "EUROPE",
            "NA1" or "BR1" or "LA1" or "LA2"  => "AMERICAS",
            "KR"  or "JP1"                      => "ASIA",
            "OC1"                               => "SEA",
            _                                   => "EUROPE"
        };
    }
}
