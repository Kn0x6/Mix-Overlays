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
    /// <summary>Contrôle la lecture/écriture du cache pour une requête Riot ponctuelle.</summary>
    public enum RiotCacheMode { Default, Bypass, Refresh }

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

        // ─── Cache HTTP (JSON brut pour ne pas partager des DTO mutables) ───────
        // Chaque endpoint a sa durée de vie : un match terminé ne change jamais,
        // tandis que le classement et la partie en cours doivent rester frais.
        private sealed record CachedResponse(string? Json, DateTime Expires);
        private sealed record CachePolicy(TimeSpan Ttl, TimeSpan? NegativeTtl = null);
        private sealed record FetchResult(string? Json, bool IsSuccess, bool IsNotFound = false);

        private readonly ConcurrentDictionary<string, CachedResponse> _cache = new();
        private readonly ConcurrentDictionary<string, Lazy<Task<FetchResult>>> _inflight = new();
        private const int MaxCacheEntries = 500;
        private int _cacheAccessCount;

        /// <summary>
        /// Nettoie les entrées expirées du cache. À appeler périodiquement.
        /// </summary>
        private void PurgeExpiredCache()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _cache.Keys.ToList())
                if (_cache.TryGetValue(key, out var v) && v.Expires <= now)
                    _cache.TryRemove(key, out _);

            // Évite qu'une longue session conserve indéfiniment des anciennes pages.
            var overflow = _cache.Count - MaxCacheEntries;
            if (overflow > 0)
            {
                foreach (var key in _cache.OrderBy(x => x.Value.Expires).Take(overflow).Select(x => x.Key))
                    _cache.TryRemove(key, out _);
            }
        }

        private static CachePolicy GetCachePolicy(string url)
        {
            if (url.Contains("/riot/account/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromHours(12));
            if (url.Contains("/lol/summoner/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromHours(1));
            if (url.Contains("/lol/league/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromMinutes(2));
            if (url.Contains("/champion-mastery/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(2));
            if (url.Contains("/matches/by-puuid/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromSeconds(30));
            if (url.Contains("/lol/match/v5/matches/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromDays(7));
            if (url.Contains("/spectator/", StringComparison.Ordinal))
                return new CachePolicy(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4));

            return new CachePolicy(TimeSpan.FromMinutes(5));
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

        /// <summary>
        /// Invalide uniquement les données qui peuvent évoluer à la suite d'une partie,
        /// sans toucher aux profils, comptes ou détails de matchs déjà immuables.
        /// </summary>
        public void InvalidatePlayerDynamicData(string puuid)
        {
            if (string.IsNullOrWhiteSpace(puuid)) return;

            foreach (var key in _cache.Keys)
            {
                if (!key.Contains(Uri.EscapeDataString(puuid), StringComparison.Ordinal)) continue;

                if (key.Contains("/lol/league/", StringComparison.Ordinal) ||
                    key.Contains("/champion-mastery/", StringComparison.Ordinal) ||
                    key.Contains("/matches/by-puuid/", StringComparison.Ordinal) ||
                    key.Contains("/spectator/", StringComparison.Ordinal))
                {
                    _cache.TryRemove(key, out _);
                }
            }

            App.Log($"[RiotCache] INVALIDATE dynamic player data");
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

        /// <summary>
        /// Retourne la maîtrise d'un champion précis. Contrairement au top des maîtrises,
        /// cet endpoint fonctionne aussi lorsque le champion n'est pas parmi les cinq plus joués.
        /// </summary>
        public async Task<ChampionMastery?> GetChampionMasteryAsync(string puuid, int championId)
        {
            if (string.IsNullOrWhiteSpace(puuid) || championId <= 0)
            {
                App.Log($"[MasteryDiag|API] Requête ignorée : puuidVide={string.IsNullOrWhiteSpace(puuid)}, championId={championId}");
                return null;
            }

            var url = $"{PlatformUrl}/lol/champion-mastery/v4/champion-masteries/by-puuid/{Uri.EscapeDataString(puuid)}/by-champion/{championId}";
            App.Log($"[MasteryDiag|API] Demande Riot : region={_settings.Current.Region}, puuid={ShortId(puuid)}, championId={championId}");

            var mastery = await GetAsync<ChampionMastery>(url);
            App.Log(mastery == null
                ? $"[MasteryDiag|API] Aucune maîtrise retournée : championId={championId}, erreur='{LastHttpError}'"
                : $"[MasteryDiag|API] Réponse Riot : championId={mastery.championId}, points={mastery.championPoints}, niveau={mastery.championLevel}");
            return mastery;
        }

        private static string ShortId(string value) =>
            value.Length <= 12 ? value : $"{value[..8]}…{value[^4..]}";

        // ─── Match API v5 ──────────────────────────────────────────────────────

        public async Task<List<string>?> GetMatchIdsByPuuidAsync(
            string puuid,
            int count = 20,
            int? queueId = null,
            int startIndex = 0,
            RiotCacheMode cacheMode = RiotCacheMode.Default)
        {
            var url = $"{RegionalUrl}/lol/match/v5/matches/by-puuid/{puuid}/ids?count={count}&start={startIndex}";
            if (queueId.HasValue) url += $"&queue={queueId}";
            return await GetAsync<List<string>>(url, cacheMode);
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
                var game = await GetAsync<SpectatorGameInfo>(urlV5);
                if (game != null) return game;
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
                    var game4 = await GetAsync<SpectatorGameInfoV4>(urlV4);
                    if (game4 != null)
                    {
                        // v4 n'a pas de champ puuid dans participants — on adapte
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Spectator v4 fallback] Exception: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> TestPlatformStatusAsync()
        {
            var url = $"{PlatformUrl}/lol/status/v4/platform-data";
            var response = await FetchAsync(url);
            return response.IsSuccess;
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
            ClearLastHttpError();

            try
            {
                // ── Étape 1 : appels parallèles indépendants ──────────────────
                var (summoner, leagueEntries, masteries, matchIds, activeGame) = await (
                    GetSummonerByPuuidAsync(puuid),
                    GetLeagueEntriesByPuuidAsync(puuid),
                    GetTopMasteriesByPuuidAsync(puuid, 5),
                    GetMatchIdsByPuuidAsync(puuid, Math.Max(matchCount * 2, 10)),
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
                {
                    player.ErrorMessage = LastHttpWasUnauthorized
                        ? "Clé Riot API invalide ou expirée. Mets-la à jour dans Paramètres pour actualiser les données."
                        : $"Données partielles : {errors}\n{LastHttpError}";
                }

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
        /// Charge UNIQUEMENT le summoner + le rang d'un joueur (2 appels API).
        /// Utilisé pendant le champ select pour afficher le rang rapidement,
        /// sans bloquer sur les masteries et l'historique de parties.
        /// </summary>
        public async Task<(RiotSummoner? summoner, List<LeagueEntry>? entries)> LoadRankOnlyAsync(string puuid)
        {
            var summonerTask = GetSummonerByPuuidAsync(puuid);
            var rankTask     = GetLeagueEntriesByPuuidAsync(puuid);
            await Task.WhenAll(summonerTask, rankTask);
            return (summonerTask.Result, rankTask.Result);
        }

        /// <summary>
        /// Charge uniquement les données nécessaires à l'affichage du rang dans un PlayerData.
        /// Utilise le même mapping Ranked que LoadFullPlayerDataAsync, sans charger masteries,
        /// historique de matchs ou partie active.
        /// </summary>
        public async Task<PlayerData> LoadRankPlayerDataAsync(string puuid, string gameName, string tagLine)
        {
            string resolvedPuuid    = puuid;
            string resolvedGameName = gameName;
            string resolvedTagLine  = tagLine;

            if (!string.IsNullOrWhiteSpace(resolvedGameName) && resolvedGameName.Contains('#'))
            {
                var parts = resolvedGameName.Split('#', 2);
                resolvedGameName = parts[0];
                if (string.IsNullOrWhiteSpace(resolvedTagLine) && parts.Length > 1)
                    resolvedTagLine = parts[1];
            }

            var player = new PlayerData
            {
                Puuid     = resolvedPuuid,
                GameName  = resolvedGameName,
                TagLine   = resolvedTagLine,
                IsLoading = true
            };

            try
            {
                // Même flux que le test CLI fonctionnel : RiotId → PUUID officiel Riot → League API.
                // Cela évite d'utiliser un PUUID LCU/2999 incomplet ou non normalisé pour l'overlay.
                if (!string.IsNullOrWhiteSpace(resolvedGameName) &&
                    !string.IsNullOrWhiteSpace(resolvedTagLine))
                {
                    var account = await GetAccountByRiotIdAsync(resolvedGameName, resolvedTagLine);
                    if (account != null && !string.IsNullOrWhiteSpace(account.puuid))
                    {
                        resolvedPuuid    = account.puuid;
                        resolvedGameName = account.gameName;
                        resolvedTagLine  = account.tagLine;

                        player.Puuid    = resolvedPuuid;
                        player.GameName = resolvedGameName;
                        player.TagLine  = resolvedTagLine;
                    }
                }

                var summonerTask = GetSummonerByPuuidAsync(resolvedPuuid);
                var rankTask     = GetLeagueEntriesByPuuidAsync(resolvedPuuid);
                await Task.WhenAll(summonerTask, rankTask);

                var summoner      = summonerTask.Result;
                var leagueEntries = rankTask.Result;

                if (summoner != null)
                {
                    player.SummonerId    = summoner.id;
                    player.ProfileIconId = summoner.profileIconId;
                    player.SummonerLevel = summoner.summonerLevel;
                }

                if (leagueEntries != null)
                {
                    foreach (var entry in leagueEntries)
                    {
                        if (entry.queueType == "RANKED_SOLO_5x5") player.SoloRank = entry;
                        else if (entry.queueType == "RANKED_FLEX_SR") player.FlexRank = entry;
                    }
                }

                player.IsLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadRankPlayerData] Erreur: {ex}");
                player.ErrorMessage = $"Erreur rang : {ex.Message}";
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
        public bool LastHttpWasUnauthorized { get; private set; }

        public void ClearLastHttpError()
        {
            LastHttpError = string.Empty;
            LastHttpWasUnauthorized = false;
        }

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

        private async Task<T?> GetAsync<T>(string url, RiotCacheMode cacheMode = RiotCacheMode.Default)
        {
            if (Interlocked.Increment(ref _cacheAccessCount) % 50 == 0)
                PurgeExpiredCache();

            var now = DateTime.UtcNow;
            if (cacheMode == RiotCacheMode.Default &&
                _cache.TryGetValue(url, out var cached) && cached.Expires > now)
            {
                App.Log($"[RiotCache] HIT {GetLogPath(url)}");
                if (cached.Json == null) return default; // cache négatif (404 court)

                try { return JsonConvert.DeserializeObject<T>(cached.Json); }
                catch { _cache.TryRemove(url, out _); }
            }

            App.Log($"[RiotCache] {(cacheMode == RiotCacheMode.Default ? "MISS" : cacheMode.ToString().ToUpperInvariant())} {GetLogPath(url)}");
            var request = new Lazy<Task<FetchResult>>(() => FetchAsync(url), LazyThreadSafetyMode.ExecutionAndPublication);
            var pending = _inflight.GetOrAdd(url, request);
            if (!ReferenceEquals(pending, request))
                App.Log($"[RiotCache] COALESCED {GetLogPath(url)}");

            FetchResult result;
            try
            {
                result = await pending.Value;
            }
            finally
            {
                _inflight.TryRemove(url, out _);
            }

            var policy = GetCachePolicy(url);
            if (result.IsSuccess && result.Json != null)
                _cache[url] = new CachedResponse(result.Json, DateTime.UtcNow.Add(policy.Ttl));
            else if (result.IsNotFound && policy.NegativeTtl is { } negativeTtl)
                _cache[url] = new CachedResponse(null, DateTime.UtcNow.Add(negativeTtl));

            if (!result.IsSuccess || result.Json == null) return default;
            try { return JsonConvert.DeserializeObject<T>(result.Json); }
            catch (Exception ex)
            {
                LastHttpError = $"Désérialisation : {ex.Message}";
                return default;
            }
        }

        /// <summary>Exécute un appel réseau Riot sous token bucket, sans politique de cache.</summary>
        private async Task<FetchResult> FetchAsync(string url)
        {
            await WaitForTokenAsync();
            try
            {
                using var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return new FetchResult(await response.Content.ReadAsStringAsync(), true);

                var uri = new Uri(url);
                var segment = uri.AbsolutePath.Split('/').LastOrDefault(s => s.Length is > 0 and < 30) ?? uri.AbsolutePath;
                var message = $"HTTP {(int)response.StatusCode} {response.StatusCode} sur /{segment}";

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    LastHttpWasUnauthorized = true;
                    LastHttpError = "HTTP 401 Unauthorized — clé Riot API invalide ou expirée. Mets-la à jour dans Paramètres.";
                }
                else if (!LastHttpWasUnauthorized)
                {
                    LastHttpError = message;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    await _rateLimitGate.WaitAsync();
                    try { _tokens = 0; }
                    finally { _rateLimitGate.Release(); }
                    App.Log($"[RateLimit] 429 reçu — attente {retryAfter.TotalSeconds:F0}s avant reprise");
                    await Task.Delay(retryAfter);
                }

                return new FetchResult(null, false, response.StatusCode == System.Net.HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LastHttpError = $"Exception : {ex.Message}";
                return new FetchResult(null, false);
            }
        }

        private static string GetLogPath(string url) => new Uri(url).AbsolutePath;
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
