using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MixOverlays.Models;
using Newtonsoft.Json;

namespace MixOverlays.Services
{
    /// <summary>
    /// Construit un historique Solo/Duo saisonnier réutilisé par les rôles et les champions.
    /// Le cache conserve uniquement les métriques utiles à l'interface, pas les détails
    /// complets du match ni les données des neuf autres participants.
    /// </summary>
    public sealed class RoleWinRateHistoryService
    {
        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MixOverlays", "role-winrate-history.json");

        private readonly SemaphoreSlim _analysisGate = new(1, 1);
        private readonly SemaphoreSlim _cacheGate = new(1, 1);
        private readonly Dictionary<string, RoleWinRatePlayerCache> _players;

        public RoleWinRateHistoryService()
        {
            _players = LoadCache();
        }

        /// <summary>
        /// Analyse les matchs Ranked Solo/Duo de la saison en cours. Les détails déjà
        /// complets dans le cache ou dans RecentMatches ne sont jamais redemandés à Riot.
        /// Toutes les requêtes passent par le limiteur central de RiotApiService.
        /// </summary>
        public async Task AnalyzeCurrentSeasonAsync(
            PlayerData player,
            RiotApiService riot,
            Func<RoleWinRateAnalysisUpdate, Task> publish,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(player.Puuid)) return;

            await _analysisGate.WaitAsync(cancellationToken);
            try
            {
                var seasonStart = new DateTimeOffset(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, TimeSpan.Zero)
                    .ToUnixTimeSeconds();
                var cache = await GetPlayerCacheAsync(player.Puuid, seasonStart);

                // Les 20 matchs déjà affichés sont immédiatement réutilisés et enregistrés.
                foreach (var match in player.RecentMatches.Where(m => m.QueueId == 420))
                {
                    if (string.IsNullOrWhiteSpace(match.MatchId)) continue;
                    cache.Matches[match.MatchId] = CreateRecord(match);
                }

                await SaveCacheAsync();
                await publish(CreateUpdate(cache.Matches.Values, totalGames: 0, isRunning: true, isComplete: false,
                    "Préparation de l'analyse saisonnière…"));

                // Match-V5 accepte le filtre queue=420 : les identifiants non Solo/Duo ne sont jamais parcourus.
                var matchIds = new List<string>();
                const int pageSize = 100;
                for (var startIndex = 0; ; startIndex += pageSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = await riot.GetMatchIdsByPuuidAsync(
                        player.Puuid, pageSize, queueId: 420, startIndex: startIndex, startTime: seasonStart);

                    if (page == null || page.Count == 0) break;
                    matchIds.AddRange(page);
                    if (page.Count < pageSize) break;
                }

                var orderedIds = matchIds.Distinct(StringComparer.Ordinal).ToList();
                var cachedForSeason = cache.Matches
                    .Where(pair => orderedIds.Contains(pair.Key, StringComparer.Ordinal))
                    .Select(pair => pair.Value)
                    .ToList();

                await publish(CreateUpdate(cachedForSeason, orderedIds.Count, isRunning: true, isComplete: false,
                    $"Analyse saisonnière : {cachedForSeason.Count}/{orderedIds.Count} parties"));

                var processedSinceSave = 0;
                for (var index = 0; index < orderedIds.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var matchId = orderedIds[index];
                    // Les entrées de cache créées avant les stats champion sont enrichies
                    // une seule fois. Les entrées complètes ne font jamais d'appel supplémentaire.
                    if (cache.Matches.TryGetValue(matchId, out var cached) && cached.HasChampionStats)
                        continue;

                    var match = await riot.GetMatchByIdAsync(matchId);
                    if (match?.info != null)
                    {
                        var participant = match.info.participants?.FirstOrDefault(p => p.puuid == player.Puuid);
                        if (participant != null)
                        {
                            // Même un rôle vide est mémorisé pour ne pas refaire la requête lors du prochain lancement.
                            cache.Matches[matchId] = CreateRecord(matchId, participant, match.info);
                        }
                    }

                    processedSinceSave++;
                    if (processedSinceSave >= 10 || index == orderedIds.Count - 1)
                    {
                        processedSinceSave = 0;
                        await SaveCacheAsync();
                        var records = orderedIds.Where(cache.Matches.ContainsKey).Select(id => cache.Matches[id]).ToList();
                        await publish(CreateUpdate(records, orderedIds.Count, isRunning: true, isComplete: false,
                            $"Analyse saisonnière : {records.Count}/{orderedIds.Count} parties"));
                    }
                }

                var finalRecords = orderedIds.Where(cache.Matches.ContainsKey).Select(id => cache.Matches[id]).ToList();
                await publish(CreateUpdate(finalRecords, orderedIds.Count, isRunning: false, isComplete: true,
                    $"Saison en cours · {finalRecords.Count} parties Solo/Duo analysées"));
            }
            catch (OperationCanceledException)
            {
                // L'état déjà publié reste exploitable et la prochaine analyse reprendra au cache.
            }
            catch (Exception ex)
            {
                App.Log($"[RoleWinRate] Analyse interrompue : {ex.Message}");
                await publish(new RoleWinRateAnalysisUpdate { IsRunning = false, Status = "Analyse des rôles interrompue." });
            }
            finally
            {
                _analysisGate.Release();
            }
        }

        private static RoleWinRateAnalysisUpdate CreateUpdate(
            IEnumerable<RoleWinRateMatchRecord> matches, int totalGames, bool isRunning, bool isComplete, string status) => new()
        {
            Matches = matches.ToList(),
            TotalGames = totalGames,
            IsRunning = isRunning,
            IsComplete = isComplete,
            Status = status
        };

        private static RoleWinRateMatchRecord CreateRecord(MatchSummary match) => new()
        {
            MatchId = match.MatchId,
            Position = match.Position,
            Win = match.Win,
            GameCreation = match.GameCreation,
            ChampionId = match.ChampionId,
            ChampionName = match.ChampionName,
            Kills = match.Kills,
            Deaths = match.Deaths,
            Assists = match.Assists,
            CS = match.CS,
            GameDuration = match.GameDuration
        };

        private static RoleWinRateMatchRecord CreateRecord(string matchId, MatchParticipant participant, MatchInfo info) => new()
        {
            MatchId = matchId,
            Position = participant.teamPosition,
            Win = participant.win,
            GameCreation = info.gameCreation,
            ChampionId = participant.championId,
            ChampionName = participant.championName,
            Kills = participant.kills,
            Deaths = participant.deaths,
            Assists = participant.assists,
            CS = participant.CS,
            GameDuration = RiotApiService.ResolveGameDuration(info)
        };

        private async Task<RoleWinRatePlayerCache> GetPlayerCacheAsync(string puuid, long seasonStart)
        {
            await _cacheGate.WaitAsync();
            try
            {
                if (!_players.TryGetValue(puuid, out var cache) || cache.SeasonStart != seasonStart)
                {
                    cache = new RoleWinRatePlayerCache { SeasonStart = seasonStart };
                    _players[puuid] = cache;
                }
                return cache;
            }
            finally { _cacheGate.Release(); }
        }

        private async Task SaveCacheAsync()
        {
            await _cacheGate.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                var json = JsonConvert.SerializeObject(_players, Formatting.None);
                await File.WriteAllTextAsync(CachePath, json);
            }
            catch (Exception ex)
            {
                App.Log($"[RoleWinRate] Cache non sauvegardé : {ex.Message}");
            }
            finally { _cacheGate.Release(); }
        }

        private static Dictionary<string, RoleWinRatePlayerCache> LoadCache()
        {
            try
            {
                if (File.Exists(CachePath))
                    return JsonConvert.DeserializeObject<Dictionary<string, RoleWinRatePlayerCache>>(File.ReadAllText(CachePath))
                        ?? new Dictionary<string, RoleWinRatePlayerCache>();
            }
            catch (Exception ex)
            {
                App.Log($"[RoleWinRate] Cache illisible, recréation : {ex.Message}");
            }
            return new Dictionary<string, RoleWinRatePlayerCache>();
        }

        private sealed class RoleWinRatePlayerCache
        {
            public long SeasonStart { get; set; }
            public Dictionary<string, RoleWinRateMatchRecord> Matches { get; set; } = new();
        }
    }
}