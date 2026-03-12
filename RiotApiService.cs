using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MixOverlays.Models;
using Newtonsoft.Json;

namespace MixOverlays.Services
{
    public class RiotApiService : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly ChampionDataService _champions;
        private readonly HttpClient _client;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client?.Dispose();
            _rateLimitGate?.Dispose();
        }

        // ─── Cache simple (clé → (valeur sérialisée, expiration)) ────────────
        private readonly ConcurrentDictionary<string, (string Json, DateTime Expires)> _cache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10); // 10min — les stats ne changent pas en live

        /// <summary>
        /// Nettoie les entrées expirées du cache. À appeler périodiquement.
        /// </summary>
        private void PurgeExpiredCache()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _cache.Keys.ToList())
                if (_cache.TryGetValue(key, out var v) && v.Expires <= now)
                    _cache.TryRemove(key, out _);
        }

        // ─── Token Bucket — respecte les limites Riot API dev key ────────────────
        // Riot dev key : 20 req/s + 100 req/2min → on vise ~15/s + 80/2min pour avoir de la marge
        private readonly SemaphoreSlim _rateLimitGate = new(1, 1); // accès séquentiel au bucket
        private int    _tokensPerSecond = 12;   // 12 req/s max (marge sur les 20/s)
        private int    _tokens          = 12;   // tokens disponibles actuellement
        private DateTime _lastRefill    = DateTime.UtcNow;

        // Compteur 2 minutes
        private readonly Queue<DateTime> _requestTimestamps = new(); // fenêtre glissante 2 min
        private const int MaxPer2Min = 80; // marge sur les 100/2min

        private string PlatformUrl => $"https://{_settings.Current.Region.ToLower()}.api.riotgames.com";
        private string RegionalUrl => $"https://{SettingsService.GetRegionalRoute(_settings.Current.Region).ToLower()}.api.riotgames.com";

        public RiotApiService(SettingsService settings, ChampionDataService? champions = null)
        {
            _settings  = settings;
            _champions = champions ?? ChampionDataService.Instance ?? new ChampionDataService();
            _client    = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _client.DefaultRequestHeaders.Add("X-Riot-Token", _settings.Current.RiotApiKey);
        }

        /// <summary>Met à jour la clé API sans recréer le HttpClient.</summary>
        public void RefreshApiKey()
        {
            _client.DefaultRequestHeaders.Remove("X-Riot-Token");
            _client.DefaultRequestHeaders.Add("X-Riot-Token", _settings.Current.RiotApiKey);
            _cache.Clear(); // invalide le cache si la clé change
        }

        // ─── Account API ───────────────────────────────────────────────────────

        public async Task<RiotAccount?> GetAccountByRiotIdAsync(string gameName, string tagLine)
        {
            var url = $"{RegionalUrl}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
            return await GetAsync<RiotAccount>(url);
        }

        public async Task<RiotAccount?> GetAccountByPuuidAsync(string puuid)
        {
            var url = $"{RegionalUrl}/riot/account/v1/accounts/by-puuid/{Uri.EscapeDataString(puuid)}";
            return await GetAsync<RiotAccount>(url);
        }

        // ─── Summoner API ──────────────────────────────────────────────────────

        public async Task<RiotSummoner?> GetSummonerByPuuidAsync(string puuid)
        {
            var url = $"{PlatformUrl}/lol/summoner/v4/summoners/by-puuid/{Uri.EscapeDataString(puuid)}";
            return await GetAsync<RiotSummoner>(url);
        }

        public async Task<RiotSummoner?> GetSummonerByIdAsync(string summonerId)
        {
            var url = $"{PlatformUrl}/lol/summoner/v4/summoners/{summonerId}";
            return await GetAsync<RiotSummoner>(url);
        }

        // ─── League API ────────────────────────────────────────────────────────

        public async Task<List<LeagueEntry>?> GetLeagueEntriesByPuuidAsync(string puuid)
        {
            var url = $"{PlatformUrl}/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}";
            return await GetAsync<List<LeagueEntry>>(url);
        }

        // ─── Champion Mastery API ──────────────────────────────────────────────

        public async Task<List<ChampionMastery>?> GetTopMasteriesByPuuidAsync(string puuid, int count = 5)
        {
            var url = $"{PlatformUrl}/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}/top?count={count}";
            return await GetAsync<List<ChampionMastery>>(url);
        }

        // ─── Match API v5 ──────────────────────────────────────────────────────

        public async Task<List<string>?> GetMatchIdsByPuuidAsync(string puuid, int count = 20, int? queueId = null, int startIndex = 0)
        {
            var url = $"{RegionalUrl}/lol/match/v5/matches/by-puuid/{puuid}/ids?count={count}&start={startIndex}";
            if (queueId.HasValue) url += $"&queue={queueId}";
            return await GetAsync<List<string>>(url);
        }

        public async Task<MatchDto?> GetMatchByIdAsync(string matchId)
        {
            var url = $"{RegionalUrl}/lol/match/v5/matches/{matchId}";
            return await GetAsync<MatchDto>(url);
        }

        public async Task<bool> IsPlayerInGameAsync(string puuid)
        {
            // Utilise directement GetActiveGameByPuuidAsync pour éviter un double appel HTTP
            var game = await GetActiveGameByPuuidAsync(puuid);
            return game != null;
        }

        public async Task<SpectatorGameInfo?> GetActiveGameByPuuidAsync(string puuid)
        {
            // Spectator v5 (PUUID) — supporté depuis 2024
            var urlV5 = $"{PlatformUrl}/lol/spectator/v5/active-games/by-summoner/{Uri.EscapeDataString(puuid)}";
            try
            {
                var resp = await _client.GetAsync(urlV5);
                System.Diagnostics.Debug.WriteLine($"[Spectator v5] {resp.StatusCode} pour {puuid[..8]}...");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<SpectatorGameInfo>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Spectator v5] Exception: {ex.Message}");
            }

            // Fallback : tenter v4 via summonerId
            // (nécessite de résoudre le summonerId depuis le puuid)
            try
            {
                var summoner = await GetSummonerByPuuidAsync(puuid);
                if (summoner != null && !string.IsNullOrEmpty(summoner.id))
                {
                    var urlV4 = $"{PlatformUrl}/lol/spectator/v4/active-games/by-summoner/{Uri.EscapeDataString(summoner.id)}";
                    var resp4 = await _client.GetAsync(urlV4);
                    System.Diagnostics.Debug.WriteLine($"[Spectator v4] {resp4.StatusCode} pour summonerId {summoner.id[..8]}...");
                    if (resp4.IsSuccessStatusCode)
                    {
                        var json4 = await resp4.Content.ReadAsStringAsync();
                        // v4 n'a pas de champ puuid dans participants — on adapte
                        var game4 = Newtonsoft.Json.JsonConvert.DeserializeObject<SpectatorGameInfoV4>(json4);
                        if (game4 != null)
                        {
                            return new SpectatorGameInfo
                            {
                                gameId        = game4.gameId,
                                gameMode      = game4.gameMode,
                                gameType      = game4.gameType,
                                gameStartTime = game4.gameStartTime,
                                participants  = game4.participants?.Select(p => new SpectatorParticipant
                                {
                                    puuid        = puuid,
                                    summonerName = p.summonerName,
                                    championId   = p.championId,
                                    teamId       = p.teamId,
                                    spell1Id     = p.spell1Id,
                                    spell2Id     = p.spell2Id
                                }).ToList()
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Spectator v4 fallback] Exception: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> TestPlatformStatusAsync()
        {
            var url = $"{PlatformUrl}/lol/status/v4/platform-data";
            try
            {
                var resp = await _client.GetAsync(url);
                LastHttpError = $"HTTP {(int)resp.StatusCode} {resp.StatusCode} → {_settings.Current.Region}";
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LastHttpError = ex.Message;
                return false;
            }
        }

        // ─── Aggregated Player Data ────────────────────────────────────────────

        /// <summary>
        /// Charge les données d'un joueur en parallélisant tous les appels indépendants.
        /// Charge uniquement les matchCount premières parties ; utilisez LoadMoreMatchesAsync pour la pagination.
        /// </summary>
        public async Task<PlayerData> LoadFullPlayerDataAsync(string puuid, string gameName, string tagLine, int matchCount = 20)
        {
            var player = new PlayerData
            {
                Puuid     = puuid,
                GameName  = gameName,
                TagLine   = tagLine,
                IsLoading = true
            };

            var errors = new System.Text.StringBuilder();

            try
            {
                // ── Étape 1 : appels parallèles indépendants ──────────────────
                var (summoner, leagueEntries, masteries, matchIds, activeGame) = await (
                    GetSummonerByPuuidAsync(puuid),
                    GetLeagueEntriesByPuuidAsync(puuid),
                    GetTopMasteriesByPuuidAsync(puuid, 5),
                    GetMatchIdsByPuuidAsync(puuid, 20),   // on récupère 20 IDs mais on charge 10
                    GetActiveGameByPuuidAsync(puuid)
                ).WhenAll5();

                // ── Summoner ──────────────────────────────────────────────────
                if (summoner != null)
                {
                    player.SummonerId    = summoner.id;
                    player.ProfileIconId = summoner.profileIconId;
                    player.SummonerLevel = summoner.summonerLevel;
                }
                else errors.Append("[summoner:null] ");

                // ── Ranked ────────────────────────────────────────────────────
                if (leagueEntries != null)
                {
                    foreach (var entry in leagueEntries)
                    {
                        if (entry.queueType == "RANKED_SOLO_5x5") player.SoloRank = entry;
                        else if (entry.queueType == "RANKED_FLEX_SR") player.FlexRank = entry;
                    }
                }
                else errors.Append("[ranked:null] ");

                // ── Masteries ─────────────────────────────────────────────────
                if (masteries != null)
                {
                    foreach (var m in masteries)
                    {
                        try   { m.ChampionName = _champions.GetName(m.championId); }
                        catch { m.ChampionName = "Inconnu"; }
                    }
                    player.TopMasteries = masteries;
                }
                else errors.Append("[masteries:null] ");

                // ── Match History (matchCount premières, 10 suivantes disponibles via MatchIdBuffer) ──
                if (matchIds != null && matchIds.Count > 0)
                {
                    // On garde les IDs en réserve pour la pagination
                    player.MatchIdBuffer = matchIds;

            var firstN = matchIds.Take(matchCount).ToList();
            player.RecentMatches = await LoadMatchSummariesAsync(puuid, firstN, errors);
            player.MatchesOffset = matchCount;
                }
                else errors.Append("[matches:null] ");

                player.IsLoaded = true;
                if (errors.Length > 0)
                    player.ErrorMessage = $"Données partielles : {errors}\n{LastHttpError}";

                // ── Live game ─────────────────────────────────────────────────
                await _champions.EnsureLoadedAsync();
                if (activeGame != null)
                {
                    player.IsInGame        = true;
                    player.ActiveGame      = activeGame;
                    player.LiveGameStartTime = activeGame.gameStartTime; // epoch ms
                    var self = activeGame.participants?.Find(p => p.puuid == puuid)
                            ?? activeGame.participants?.FirstOrDefault();
                    if (self != null)
                        player.CurrentChampionName = _champions.GetName(self.championId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadFullPlayerData] Erreur complète: {ex}");
                player.ErrorMessage = $"Erreur : {ex.Message}";
                player.IsLoaded = true;
            }
            finally
            {
                player.IsLoading = false;
            }

            return player;
        }

        /// <summary>
        /// Charge la page suivante de matchs (10 parties) pour un joueur déjà chargé.
        /// Retourne la liste des nouveaux MatchSummary ajoutés, ou une liste vide si plus rien.
        /// </summary>
        public async Task<List<MatchSummary>> LoadMoreMatchesAsync(PlayerData player)
        {
            if (player.MatchIdBuffer == null || player.MatchesOffset >= player.MatchIdBuffer.Count)
                return new List<MatchSummary>();

            var nextIds = player.MatchIdBuffer
                .Skip(player.MatchesOffset)
                .Take(10)
                .ToList();

            if (nextIds.Count == 0)
                return new List<MatchSummary>();

            var dummy = new System.Text.StringBuilder();
            var newMatches = await LoadMatchSummariesAsync(player.Puuid, nextIds, dummy);

            player.MatchesOffset += nextIds.Count;
            // Créer une nouvelle liste pour forcer la notification WPF (OnPropertyChanged ne détecte pas Add sur la même instance)
            var updatedMatches = new System.Collections.Generic.List<MatchSummary>(player.RecentMatches);
            updatedMatches.AddRange(newMatches);
            player.RecentMatches = updatedMatches;

            // Si on a épuisé le buffer de 20, on tente de charger 10 IDs de plus depuis l'API
            if (player.MatchesOffset >= player.MatchIdBuffer.Count)
            {
                var moreIds = await GetMatchIdsByPuuidAsync(player.Puuid, 10, startIndex: player.MatchIdBuffer.Count);
                if (moreIds != null && moreIds.Count > 0)
                {
                    player.MatchIdBuffer.AddRange(moreIds);
                    System.Diagnostics.Debug.WriteLine($"[LoadMoreMatches] {moreIds.Count} IDs supplémentaires ajoutés au buffer.");
                }
            }

            return newMatches;
        }

        /// <summary>Charge une liste de matchIds en parallèle (max 2 simultanés) et retourne les MatchSummary.</summary>
        private async Task<List<MatchSummary>> LoadMatchSummariesAsync(string puuid, List<string> matchIds, System.Text.StringBuilder errors)
        {
            var semaphore = new SemaphoreSlim(2, 2);
            var summaries = new ConcurrentBag<(int index, MatchSummary summary)>();

            var tasks = matchIds.Select(async (id, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var match = await GetMatchByIdAsync(id);
                    if (match?.info?.participants == null) return;

                    var participant = match.info.participants.Find(p => p.puuid == puuid);
                    if (participant == null) return;

                    // Opposant direct
                    MatchParticipant? opponent = null;
                    if (!string.IsNullOrEmpty(participant.teamPosition))
                    {
                        var myIndex        = match.info.participants.IndexOf(participant);
                        var myTeamId       = myIndex < 5 ? 100 : 200;
                        var enemyTeamStart = myTeamId == 100 ? 5 : 0;

                        opponent = match.info.participants
                            .Skip(enemyTeamStart).Take(5)
                            .FirstOrDefault(p => p.teamPosition == participant.teamPosition);
                    }

                    var summary = new MatchSummary
                    {
                        MatchId              = id,
                        Win                  = participant.win,
                        ChampionName         = participant.championName,
                        ChampionId           = participant.championId,
                        Kills                = participant.kills,
                        Deaths               = participant.deaths,
                        Assists              = participant.assists,
                        CS                   = participant.CS,
                        Position             = participant.teamPosition,
                        GameDuration         = ResolveGameDuration(match.info),
                        GameCreation         = match.info.gameCreation,
                        QueueId              = match.info.queueId,
                        Summoner1Id          = participant.summoner1Id,
                        Summoner2Id          = participant.summoner2Id,
                        PrimaryRuneId        = participant.PrimaryRune,
                        OpponentChampionId   = opponent?.championId   ?? 0,
                        OpponentChampionName = opponent?.championName ?? string.Empty,
                    };

summary.AllParticipants = match.info.participants
                        .Select(p => new MatchParticipantSummary
                        {
                            Puuid        = p.puuid,
                            GameName     = !string.IsNullOrEmpty(p.riotIdGameName) ? p.riotIdGameName : p.summonerName,
                            TagLine      = p.riotIdTagline,
                            ChampionName = p.championName,
                            ChampionId   = p.championId,
                            Kills        = p.kills,
                            Deaths       = p.deaths,
                            Assists      = p.assists,
                            CS           = p.CS,
                            Win          = p.win,
                            Position     = p.teamPosition,
                            TotalDamage  = p.totalDamageDealtToChampions,
                            GoldEarned   = p.goldEarned,
                            VisionScore  = p.visionScore,
                            GameDuration = ResolveGameDuration(match.info),
                            Items        = new[] { p.item0, p.item1, p.item2, p.item3, p.item4, p.item5, p.item6 },
                            Summoner1Id  = p.summoner1Id,
                            Summoner2Id  = p.summoner2Id
                        }).ToList();

                    summaries.Add((index, summary));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadMatchSummaries] Erreur match {id}: {ex.Message}");
                    errors.Append($"[match:{id}] ");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Restitue l'ordre original
            return summaries.OrderBy(x => x.index).Select(x => x.summary).ToList();
        }

        // ─── HTTP Helper ───────────────────────────────────────────────────────

        public string LastHttpError { get; set; } = string.Empty;

        /// <summary>
        /// Attend qu'un token soit disponible avant d'envoyer une requête.
        /// Implémente un token bucket : recharge 12 tokens/seconde, max 80/2min.
        /// </summary>
        private async Task WaitForTokenAsync()
        {
            while (true)
            {
                await _rateLimitGate.WaitAsync();
                try
                {
                    var now = DateTime.UtcNow;

                    // ── Recharge du bucket par seconde ──
                    var elapsed = (now - _lastRefill).TotalSeconds;
                    if (elapsed >= 1.0)
                    {
                        int refill = (int)(elapsed * _tokensPerSecond);
                        _tokens = Math.Min(_tokens + refill, _tokensPerSecond);
                        _lastRefill = now;
                    }

                    // ── Nettoyage de la fenêtre 2 minutes ──
                    while (_requestTimestamps.Count > 0 && (now - _requestTimestamps.Peek()).TotalSeconds > 120)
                        _requestTimestamps.Dequeue();

                    // ── Peut-on envoyer ? ──
                    if (_tokens > 0 && _requestTimestamps.Count < MaxPer2Min)
                    {
                        _tokens--;
                        _requestTimestamps.Enqueue(now);
                        return; // token obtenu, on peut envoyer
                    }
                }
                finally
                {
                    _rateLimitGate.Release();
                }

                // Pas de token disponible → attendre 200ms et réessayer
                await Task.Delay(200);
            }
        }

        private async Task<T?> GetAsync<T>(string url, bool useCache = true)
        {
            bool isSpectator = url.Contains("/spectator/");
            if (useCache && !isSpectator && _cache.TryGetValue(url, out var cached) && cached.Expires > DateTime.UtcNow)
            {
                try { return JsonConvert.DeserializeObject<T>(cached.Json); }
                catch { }
            }

            // Token bucket : attend qu'on puisse envoyer
            await WaitForTokenAsync();

            try
            {
                var resp = await _client.GetAsync(url);
                
                if (!resp.IsSuccessStatusCode)
                {
                    var uri     = new Uri(url);
                    var segment = uri.AbsolutePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s) && s.Length < 30) ?? uri.AbsolutePath;
                    LastHttpError = $"HTTP {(int)resp.StatusCode} {resp.StatusCode} sur /{segment}";
                    
                    // Si c'est une erreur 429, on attend un peu plus longtemps
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // Backoff exponentiel : vider le bucket + attendre
                        await _rateLimitGate.WaitAsync();
                        _tokens = 0; // forcer l'arrêt temporaire
                        _rateLimitGate.Release();

                        // Lire le Retry-After si présent
                        int retryAfter = 10;
                        if (resp.Headers.TryGetValues("Retry-After", out var vals) &&
                            int.TryParse(vals.FirstOrDefault(), out int ra))
                            retryAfter = ra;

                        App.Log($"[RateLimit] 429 reçu — attente {retryAfter}s avant reprise");
                        await Task.Delay(retryAfter * 1000);
                    }
                    
                    return default;
                }
                
                var json = await resp.Content.ReadAsStringAsync();
                if (useCache && !isSpectator)
                    _cache[url] = (json, DateTime.UtcNow.Add(CacheTtl));
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                LastHttpError = $"Exception : {ex.Message}";
                return default;
            }
        }
    /// <summary>
    /// Calcule la durée réelle en secondes depuis le MatchInfo.
    /// - Avant le patch 11.20 (gameCreation < 1634000000000), l'API renvoyait des ms
    /// - Si gameDuration == 0, fallback sur gameEndTimestamp - gameCreation
    /// </summary>
    internal static long ResolveGameDuration(MatchInfo info)
    {
        long duration = info.gameDuration;

        // Patch 11.20 = 2021-10-21 → epoch ms = 1634774400000
        // Avant cette date, gameDuration était en millisecondes
        if (info.gameCreation > 0 && info.gameCreation < 1_634_774_400_000L && duration > 3600)
            duration = duration / 1000;

        // Fallback si toujours 0 : calculer depuis les timestamps
        if (duration <= 0 && info.gameEndTimestamp > 0 && info.gameCreation > 0)
            duration = (info.gameEndTimestamp - info.gameCreation) / 1000;

        return duration;
    }
    }

    

    // ─── Helper : WhenAll pour 5 types différents ──────────────────────────────
    internal static class TaskExtensions
    {
        public static async Task<(T1, T2, T3, T4, T5)> WhenAll5<T1, T2, T3, T4, T5>(
            this (Task<T1> t1, Task<T2> t2, Task<T3> t3, Task<T4> t4, Task<T5> t5) tasks)
        {
            await Task.WhenAll(tasks.t1, tasks.t2, tasks.t3, tasks.t4, tasks.t5);
            return (tasks.t1.Result, tasks.t2.Result, tasks.t3.Result, tasks.t4.Result, tasks.t5.Result);
        }
    }
}
