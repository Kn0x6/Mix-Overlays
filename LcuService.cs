using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using MixOverlays.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MixOverlays.Services
{
    public enum LcuState { Disconnected, Connecting, Connected, InChampSelect, InGame }

    public class LcuConnectionEventArgs : EventArgs
    {
        public LcuState State { get; init; }
    }

    public sealed class RunePageImportResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public class LcuService : IDisposable
    {
        // ─── Events ────────────────────────────────────────────────────────────
        public event EventHandler<LcuConnectionEventArgs>?  StateChanged;
        public event EventHandler<LcuChampSelectSession>?   ChampSelectSessionUpdated;
        public event EventHandler<LcuGameflowSession>?      GameflowSessionUpdated;
        public event EventHandler<LcuGameData>?             InGameSessionUpdated;

        public LcuState CurrentState { get; private set; } = LcuState.Disconnected;
        public bool IsConnected => CurrentState != LcuState.Disconnected;
        public string LastDiagnostic { get; private set; } = string.Empty;

        private HttpClient?  _client;
        private string?      _baseUrl;
        private readonly CancellationTokenSource _pollCancellation = new();
        private readonly Task _pollingTask;
        private string       _lastGameflowPhase = string.Empty;
        private bool         _inGameDataLoaded  = false;
        private int          _disposed;

        private static readonly TimeSpan DisconnectedPollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ConnectedPollInterval    = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan InGamePollInterval       = TimeSpan.FromSeconds(10);

        private static readonly JsonSerializerSettings _champSelectJsonSettings = new()
        {
            Error = (_, args) =>
            {
                App.Log($"[LCU] [ChampSelect|Deserialize] warning path='{args.ErrorContext.Path}' error='{args.ErrorContext.Error.Message}'");
                args.ErrorContext.Handled = true;
            }
        };

        private static readonly HttpClient _liveClient = new(
            new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        { Timeout = TimeSpan.FromSeconds(4) };

        private static void L(string msg) => App.Log($"[LCU] {msg}");

        public LcuService()
        {
            L("LcuService créé — démarrage du polling séquentiel");
            _pollingTask = PollLoopAsync(_pollCancellation.Token);
        }

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected)
                        await TryConnect(cancellationToken);
                    else
                        await PollGameflow(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    L($"Poll exception: {ex.Message}");
                }

                try
                {
                    await Task.Delay(GetPollInterval(), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            L("Polling arrêté.");
        }

        private TimeSpan GetPollInterval() => CurrentState == LcuState.InGame && _inGameDataLoaded
            ? InGamePollInterval
            : IsConnected
                ? ConnectedPollInterval
                : DisconnectedPollInterval;

        private async Task TryConnect(CancellationToken cancellationToken)
        {
            SetState(LcuState.Connecting);
            var lockfileData = FindLockfile();
            if (lockfileData == null)
            {
                LastDiagnostic = "Lockfile introuvable";
                L("Lockfile introuvable — LoL non lancé?");
                SetState(LcuState.Disconnected);
                return;
            }

            var (port, password) = lockfileData.Value;
            L($"Lockfile trouvé → port={port}");
            LastDiagnostic = $"Lockfile trouvé → port {port}";
            _baseUrl = $"https://127.0.0.1:{port}";

            var handler = new HttpClientHandler
            { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{password}"));
            _client = new HttpClient(handler) { BaseAddress = new Uri(_baseUrl) };
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            _client.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                using var resp = await _client.GetAsync("/lol-summoner/v1/current-summoner", cancellationToken);
                L($"Test connexion LCU → HTTP {(int)resp.StatusCode}");
                if (resp.IsSuccessStatusCode)
                {
                    SetState(LcuState.Connected);
                    await PollGameflow(cancellationToken);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) { L($"TryConnect exception: {ex.Message}"); }

            _client?.Dispose();
            _client = null;
            SetState(LcuState.Disconnected);
        }

        private async Task PollGameflow(CancellationToken cancellationToken)
        {
            if (_client == null) return;
            try
            {
                using var phaseResp = await _client.GetAsync("/lol-gameflow/v1/gameflow-phase", cancellationToken);
                if (!phaseResp.IsSuccessStatusCode)
                {
                    L($"gameflow-phase HTTP {(int)phaseResp.StatusCode} → Disconnected");
                    SetState(LcuState.Disconnected);
                    _client.Dispose();
                    _client = null;
                    _lastGameflowPhase = string.Empty;
                    _inGameDataLoaded = false;
                    return;
                }

                var phase = (await phaseResp.Content.ReadAsStringAsync()).Trim('"');
                bool phaseChanged = phase != _lastGameflowPhase;

                if (phaseChanged)
                    L($"Phase: {_lastGameflowPhase} → {phase}");

                if (phaseChanged) _lastGameflowPhase = phase;

                switch (phase)
                {
                    case "ChampSelect":
                        if (phaseChanged)
                        {
                            SetState(LcuState.InChampSelect);
                            _inGameDataLoaded = false;
                            GameflowSessionUpdated?.Invoke(this, new LcuGameflowSession { phase = phase });
                        }
                        await PollChampSelect();
                        break;

                    case "InProgress":
                    case "Reconnect":
                        if (phaseChanged)
                        {
                            SetState(LcuState.InGame);
                            GameflowSessionUpdated?.Invoke(this, new LcuGameflowSession { phase = phase });
                        }
                        if (!_inGameDataLoaded)
                            await FetchInGameDataAsync();
                        else
                        {
                            // L'intervalle passe réellement à 10 secondes via GetPollInterval().
                        }
                        break;

                    case "None":
                    case "Lobby":
                    case "Matchmaking":
                    case "ReadyCheck":
                    case "EndOfGame":
                    case "PreEndOfGame":
                    case "WaitingForStats":
                        if (phaseChanged)
                        {
                            SetState(LcuState.Connected);
                            _inGameDataLoaded = false;
                            GameflowSessionUpdated?.Invoke(this, new LcuGameflowSession { phase = phase });
                        }
                        break;

                    default:
                        L($"Phase inconnue: '{phase}'");
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                L($"PollGameflow exception: {ex.Message}");
                SetState(LcuState.Disconnected);
                _client?.Dispose();
                _client = null;
                _lastGameflowPhase = string.Empty;
                _inGameDataLoaded  = false;
            }
        }

        // ─── Chargement données en partie ──────────────────────────────────────
        private async Task FetchInGameDataAsync()
        {
            L("=== FetchInGameData START ===");

            // Charger les deux sources en parallèle
            L("Chargement port 2999 + LCU session en parallèle...");
            var port2999Task = TryPort2999Async();
            var lcuSessionTask = FetchLcuSessionDataAsync();
            await Task.WhenAll(port2999Task, lcuSessionTask);

            var liveData   = port2999Task.Result;
            var lcuSession = lcuSessionTask.Result;

            // Merger : port 2999 a championName/team, LCU session a summonerId/puuid
            // On préfère port 2999 pour les équipes mais on enrichit avec les summonerIds du LCU
            if (liveData != null && (liveData.teamOne.Any() || liveData.teamTwo.Any()))
            {
                L($"Port 2999 SUCCÈS → t1={liveData.teamOne.Count} t2={liveData.teamTwo.Count}");

                // Enrichir avec summonerIds depuis LCU pour permettre la résolution de noms
                if (lcuSession != null)
                {
                    var allLcu = lcuSession.teamOne.Concat(lcuSession.teamTwo).ToList();
                    foreach (var m in liveData.teamOne.Concat(liveData.teamTwo))
                    {
                        if (!string.IsNullOrEmpty(m.puuid)) continue; // PUUID déjà connu
                        // Chercher par summonerName (riotId)
                        var match = allLcu.FirstOrDefault(l =>
                            !string.IsNullOrEmpty(l.puuid) && string.IsNullOrEmpty(m.puuid));
                        // On ne peut pas matcher sans PUUID — utiliser l'ordre de la liste
                    }
                    // Fallback simple : copier les PUUIDs du LCU dans l'ordre si pas de PUUID port2999
                    EnrichWithLcuPuuids(liveData.teamOne, lcuSession.teamOne);
                    EnrichWithLcuPuuids(liveData.teamTwo, lcuSession.teamTwo);
                    L($"Après enrichissement → t1puuid={liveData.teamOne.Count(m => m.puuid.Length > 5)} t2puuid={liveData.teamTwo.Count(m => m.puuid.Length > 5)}");
                }

                // Résoudre les noms manquants via LCU
                await ResolveMissingNamesAsync(liveData.teamOne);
                await ResolveMissingNamesAsync(liveData.teamTwo);

                _inGameDataLoaded = true;
                InGameSessionUpdated?.Invoke(this, liveData);
                return;
            }
            L("Port 2999 ECHEC ou vide");

            // Niveau 2 : LCU session seule
            if (lcuSession != null && (lcuSession.teamOne.Any() || lcuSession.teamTwo.Any()))
            {
                L($"LCU session SUCCÈS → t1={lcuSession.teamOne.Count} t2={lcuSession.teamTwo.Count}");
                await ResolveMissingNamesAsync(lcuSession.teamOne);
                await ResolveMissingNamesAsync(lcuSession.teamTwo);
                _inGameDataLoaded = true;
                InGameSessionUpdated?.Invoke(this, lcuSession);
                return;
            }

            // Niveau 3 : ChampSelect fallback
            if (_client != null)
            {
                L("Tentative ChampSelect fallback...");
                var cs = await BuildGameDataFromChampSelectAsync();
                if (cs != null && (cs.teamOne.Any() || cs.teamTwo.Any()))
                {
                    L($"ChampSelect SUCCÈS → t1={cs.teamOne.Count} t2={cs.teamTwo.Count}");
                    _inGameDataLoaded = true;
                    InGameSessionUpdated?.Invoke(this, cs);
                    return;
                }
            }

            L("=== TOUS LES NIVEAUX ONT ECHOUÉ — réessai au prochain poll ===");
        }

        private static void EnrichWithLcuPuuids(List<LcuTeamMember> target, List<LcuTeamMember> source)
        {
            // Mapper dans l'ordre : si le port 2999 n'a pas de PUUID, prendre celui du LCU
            for (int i = 0; i < Math.Min(target.Count, source.Count); i++)
            {
                if (string.IsNullOrEmpty(target[i].puuid) && !string.IsNullOrEmpty(source[i].puuid))
                {
                    target[i].puuid      = source[i].puuid;
                    target[i].summonerId = source[i].summonerId;
                }
            }
        }

        private async Task ResolveMissingNamesAsync(List<LcuTeamMember> members)
        {
            foreach (var m in members)
            {
                // summonerName déjà renseigné (ex: riotId depuis port 2999)
                if (!string.IsNullOrEmpty(m.summonerName)) continue;

                // Résoudre via LCU : préférer le summonerId (plus fiable pour les autres joueurs)
                LcuSummoner? s = null;
                if (m.summonerId > 0)
                    s = await GetAsync<LcuSummoner>($"/lol-summoner/v1/summoners/{m.summonerId}");
                if (s == null && !string.IsNullOrEmpty(m.puuid))
                    s = await GetAsync<LcuSummoner>($"/lol-summoner/v2/summoners/by-puuid/{m.puuid}");

                if (s != null)
                {
                    var name = !string.IsNullOrEmpty(s.gameName)    ? s.gameName
                             : !string.IsNullOrEmpty(s.displayName) ? s.displayName
                             : string.Empty;
                    if (!string.IsNullOrEmpty(name))
                        m.summonerName = string.IsNullOrEmpty(s.tagLine) ? name : $"{name}#{s.tagLine}";
                    L("Nom de joueur manquant résolu via LCU.");
                }
            }
        }

        private async Task<LcuGameData?> FetchLcuSessionDataAsync()
        {
            if (_client == null) return null;
            try
            {
                using var resp = await _client.GetAsync("/lol-gameflow/v1/session");
                L($"LCU session HTTP {(int)resp.StatusCode}");
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();

                var gd = ExtractGameData(json);
                if (gd == null) return null;
                L($"LCU session → t1={gd.teamOne.Count} t2={gd.teamTwo.Count} pcs={gd.playerChampionSelections.Count}");

                await ResolveMissingPuuidsAsync(gd.teamOne);
                await ResolveMissingPuuidsAsync(gd.teamTwo);
                return gd;
            }
            catch (Exception ex) { L($"FetchLcuSessionData ex: {ex.Message}"); return null; }
        }

        // ─── Port 2999 ─────────────────────────────────────────────────────────
        private async Task<LcuGameData?> TryPort2999Async()
        {
            try
            {
                using var resp = await _liveClient.GetAsync("https://127.0.0.1:2999/liveclientdata/playerlist");
                L($"Port2999 /playerlist HTTP {(int)resp.StatusCode}");
                if (!resp.IsSuccessStatusCode) return null;

                var json    = await resp.Content.ReadAsStringAsync();
                var players = JsonConvert.DeserializeObject<List<Port2999Player>>(json);
                L($"Port2999 players count={players?.Count ?? -1}");
                if (players == null || players.Count == 0) return null;

                string myTeam = "ORDER";
                try
                {
                    using var aResp = await _liveClient.GetAsync("https://127.0.0.1:2999/liveclientdata/activeplayer");
                    L($"Port2999 /activeplayer HTTP {(int)aResp.StatusCode}");
                    if (aResp.IsSuccessStatusCode)
                    {
                        var aJson  = await aResp.Content.ReadAsStringAsync();
                        var aObj   = JObject.Parse(aJson);
                        var myName = aObj["summonerName"]?.ToString()
                                  ?? aObj["riotId"]?.ToString()
                                  ?? string.Empty;
                        var me = players.FirstOrDefault(p =>
                            string.Equals(p.summonerName, myName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.riotId,       myName, StringComparison.OrdinalIgnoreCase));
                        if (me != null) { myTeam = me.team ?? "ORDER"; L($"Mon équipe = {myTeam}"); }
                        else L("Joueur actif non trouvé dans la liste live.");
                    }
                }
                catch (Exception ex) { L($"Port2999 activeplayer ex: {ex.Message}"); }

                var allied = players.Where(p => string.Equals(p.team, myTeam,  StringComparison.OrdinalIgnoreCase)).ToList();
                var enemy  = players.Where(p => !string.Equals(p.team, myTeam, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!allied.Any() && !enemy.Any())
                {
                    L("team ORDER/CHAOS vide → split manuel par moitié");
                    allied = players.Take(players.Count / 2 + players.Count % 2).ToList();
                    enemy  = players.Skip(allied.Count).ToList();
                }

                L($"Port2999 allied={allied.Count} enemy={enemy.Count}");

                // Résoudre les PUUIDs manquants via LCU summoner lookup par nom
                async Task ResolvePuuid(Port2999Player p)
                {
                    if (!string.IsNullOrEmpty(p.puuid)) return;
                    try
                    {
                        // Essai 1 : lookup par gameName+tagLine via LCU
                        var riotId = p.riotId ?? string.Empty;
                        if (riotId.Contains('#'))
                        {
                            var parts = riotId.Split('#', 2);
                            var summ  = await GetAsync<LcuSummoner>($"/lol-summoner/v1/summoners?name={Uri.EscapeDataString(parts[0])}");
                            if (summ?.puuid is { Length: > 5 } pu1) { p.puuid = pu1; return; }
                        }
                        // Essai 2 : summoner par displayName
                        var name = p.summonerName ?? string.Empty;
                        if (!string.IsNullOrEmpty(name))
                        {
                            var summ = await GetAsync<LcuSummoner>($"/lol-summoner/v1/summoners?name={Uri.EscapeDataString(name)}");
                            if (summ?.puuid is { Length: > 5 } pu2) p.puuid = pu2;
                        }
                    }
                    catch (Exception ex) { L($"ResolvePuuid ex: {ex.Message}"); }
                }

                await Task.WhenAll(allied.Concat(enemy).Select(ResolvePuuid));

                List<LcuTeamMember> ToTeam(List<Port2999Player> list) =>
                    list.Select(p =>
                    {
                        var raw1 = p.summonerSpells?.summonerSpellOne?.rawDisplayName;
                        var raw2 = p.summonerSpells?.summonerSpellTwo?.rawDisplayName;
                        return new LcuTeamMember
                        {
                            puuid        = p.puuid        ?? string.Empty,
                            summonerName = p.riotId       ?? p.summonerName ?? string.Empty,
                            summonerId   = 0,
                            championName = p.championName ?? string.Empty,
                            spell1Id     = SpellNameToId(raw1),
                            spell2Id     = SpellNameToId(raw2),
                        };
                    }).ToList();

                return new LcuGameData { teamOne = ToTeam(allied), teamTwo = ToTeam(enemy) };
            }
            catch (Exception ex) { L($"TryPort2999 exception: {ex.Message}"); return null; }
        }

        // ─── Conversion nom de sort → ID ──────────────────────────────────────
        private static int SpellNameToId(string? rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return 0;

            // Port 2999 renvoie "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName"
            // On extrait "SummonerFlash" du milieu
            var name = rawName;
            if (name.StartsWith("GeneratedTip_SummonerSpell_", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Replace("GeneratedTip_SummonerSpell_", "")
                            .Replace("_DisplayName", "");
            }

            // Amélioration : ajout de variants et normalisation
            name = name.ToLower().Trim();

            return name switch
            {
                "summonerflash"        => 4,
                "summonerteleport"     => 12,
                // Après 10 minutes, Riot peut renvoyer la Téléportation améliorée
                // sous un nom différent via le Live Client Data API. On garde l'ID
                // 12 pour afficher l'icône de Téléportation au lieu de rien.
                "summonerteleportupgrade" => 12,
                "summonerteleportunleashed" => 12,
                "summonerdot"          => 14,
                "summonerexhaust"      => 3,
                "summonerhaste"        => 6,
                "summonerheal"         => 7,
                "summonersmite"        => 11,
                "summonerbarrier"      => 21,
                "summonerclairvoyance" => 2,
                "summonermana"         => 13,
                "summonersnowball"     => 32,
                "summonerboost"        => 1,  // Cleanse
                "summonerpororecall"   => 30,
                "summonerporothrow"    => 31,
                _                      => 0
            };
        }

        // ─── Extraction LCU ────────────────────────────────────────────────────
        private static LcuGameData? ExtractGameData(string json)
        {
            try
            {
                // Désérialisation tolérante : "queue" est un objet JSON, pas une string
                // On ignore les erreurs de type pour ne pas crasher
                var settings = new JsonSerializerSettings
                {
                    Error = (_, args) => { args.ErrorContext.Handled = true; }
                };
                var session = JsonConvert.DeserializeObject<LcuGameflowSession>(json, settings);
                var gd = session?.gameData;

                App.Log($"[LCU] DeserializeObject → gd={gd != null} t1={gd?.teamOne.Count ?? -1} t2={gd?.teamTwo.Count ?? -1} pcs={gd?.playerChampionSelections.Count ?? -1}");

                if (gd == null) { App.Log("[LCU] gameData null"); return null; }

                // Cas normal (partie matchée) : teamOne/teamTwo présents
                if (gd.teamOne.Any() || gd.teamTwo.Any())
                    return gd;

                // Cas partie custom/pratique : seul playerChampionSelections est renseigné
                // On met tous les joueurs dans teamOne
                if (gd.playerChampionSelections.Any())
                {
                    App.Log($"[LCU] teamOne/teamTwo vides → fallback playerChampionSelections ({gd.playerChampionSelections.Count} joueurs)");
                    gd.teamOne = gd.playerChampionSelections
                        .Select(p => new LcuTeamMember { puuid = p.puuid, summonerId = 0 })
                        .ToList();
                    return gd;
                }

                // Tentative via JObject si tout est vide (noms de champs alternatifs)
                var root = JObject.Parse(json);
                var gdObj = (root["gameData"] ?? root["GameData"] ?? root) as JObject;
                if (gdObj == null) { App.Log("[LCU] JObject gameData null"); return null; }

                var t1 = gdObj["teamOne"] ?? gdObj["TeamOne"] ?? gdObj["team1"] ?? gdObj["Team1"];
                var t2 = gdObj["teamTwo"] ?? gdObj["TeamTwo"] ?? gdObj["team2"] ?? gdObj["Team2"];
                App.Log($"[LCU] JObject t1 count={(t1 as JArray)?.Count ?? -1}  t2 count={(t2 as JArray)?.Count ?? -1}");

                static List<LcuTeamMember> Parse(JToken? arr)
                {
                    if (arr == null) return new();
                    var list = new List<LcuTeamMember>();
                    foreach (var x in arr)
                    {
                        long.TryParse((x["summonerId"] ?? x["SummonerId"])?.ToString() ?? "0", out var sid);
                        list.Add(new LcuTeamMember
                        {
                            puuid        = (x["puuid"]       ?? x["Puuid"])?.ToObject<string>()       ?? "",
                            summonerName = (x["summonerName"] ?? x["SummonerName"] ?? x["displayName"])?.ToObject<string>() ?? "",
                            summonerId   = sid
                        });
                    }
                    return list;
                }
                return new LcuGameData { teamOne = Parse(t1), teamTwo = Parse(t2) };
            }
            catch (Exception ex) { App.Log($"[LCU] ExtractGameData ex: {ex.Message}"); return null; }
        }

        private async Task ResolveMissingPuuidsAsync(List<LcuTeamMember> members)
        {
            foreach (var m in members)
            {
                if (!string.IsNullOrEmpty(m.puuid)) continue;
                if (m.summonerId <= 0) continue;
                try
                {
                    var s = await GetAsync<LcuSummoner>($"/lol-summoner/v1/summoners/{m.summonerId}");
                    if (s?.puuid is { Length: > 0 } p)
                        m.puuid = p;
                }
                catch { }
            }
        }

        private async Task<LcuGameData?> BuildGameDataFromChampSelectAsync()
        {
            try
            {
                if (_client == null) return null;
                using var resp = await _client.GetAsync("/lol-champ-select/v1/session");
                L($"ChampSelect session HTTP {(int)resp.StatusCode}");
                if (!resp.IsSuccessStatusCode) return null;
                var json = await resp.Content.ReadAsStringAsync();
                var s = JsonConvert.DeserializeObject<LcuChampSelectSession>(json, _champSelectJsonSettings);
                if (s == null) { L("ChampSelect deserialize null"); return null; }
                L($"ChampSelect myTeam={s.myTeam.Count} theirTeam={s.theirTeam.Count}");
                var gd = new LcuGameData();
                if (!s.theirTeam.Any() && s.myTeam.Count > 5)
                {
                    int id1 = s.myTeam.First().teamId;
                    gd.teamOne = s.myTeam.Where(m => m.teamId == id1) .Select(m => new LcuTeamMember { summonerId = m.summonerId, puuid = m.puuid }).ToList();
                    gd.teamTwo = s.myTeam.Where(m => m.teamId != id1) .Select(m => new LcuTeamMember { summonerId = m.summonerId, puuid = m.puuid }).ToList();
                }
                else
                {
                    gd.teamOne = s.myTeam   .Select(m => new LcuTeamMember { summonerId = m.summonerId, puuid = m.puuid }).ToList();
                    gd.teamTwo = s.theirTeam.Select(m => new LcuTeamMember { summonerId = m.summonerId, puuid = m.puuid }).ToList();
                }
                L($"BuildFromCS → t1={gd.teamOne.Count} t2={gd.teamTwo.Count}");
                return gd;
            }
            catch (Exception ex) { L($"BuildFromCS ex: {ex.Message}"); return null; }
        }

        private async Task PollChampSelect()
        {
            if (_client == null) return;
            try
            {
                var resp = await _client.GetAsync("/lol-champ-select/v1/session");
                L($"[ChampSelect|Poll] session HTTP {(int)resp.StatusCode}");
                if (!resp.IsSuccessStatusCode) return;
                var json    = await resp.Content.ReadAsStringAsync();
                var session = JsonConvert.DeserializeObject<LcuChampSelectSession>(json, _champSelectJsonSettings);
                if (session == null)
                {
                    L("[ChampSelect|Poll] session deserialize null");
                    return;
                }

                var sessionChampions = string.Join(" | ", session.myTeam.Select(m =>
                    $"cell={m.cellId},sid={m.summonerId},champ={m.championId},team={m.teamId}"));
                var sessionActions = session.actions == null
                    ? "actions=null"
                    : string.Join(" | ", session.actions
                        .SelectMany(group => group ?? new List<LcuChampSelectAction>())
                        .Where(a => a.championId > 0 || string.Equals(a.type, "pick", StringComparison.OrdinalIgnoreCase))
                        .Select(a => $"actor={a.actorCellId},type={a.type},champ={a.championId},done={a.completed}"));

                L($"[ChampSelect|Poll] localCell={session.localPlayerCellId} my={session.myTeam.Count} their={session.theirTeam.Count} myTeam=[{sessionChampions}] actions=[{sessionActions}]");

                try
                {
                    var currentResp = await _client.GetAsync("/lol-champ-select/v1/current-champion");
                    L($"[ChampSelect|CurrentChampion] HTTP {(int)currentResp.StatusCode}");
                    if (currentResp.IsSuccessStatusCode)
                    {
                        var currentBody = (await currentResp.Content.ReadAsStringAsync()).Trim();
                        L($"[ChampSelect|CurrentChampion] body='{currentBody}'");
                        if (int.TryParse(currentBody.Trim('"'), out var currentChampionId))
                        {
                            session.currentChampionId = currentChampionId;
                            L($"[ChampSelect|CurrentChampion] parsed currentChampionId={session.currentChampionId}");
                        }
                        else
                        {
                            L($"[ChampSelect|CurrentChampion] parse failed for body='{currentBody}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    L($"current-champion ex: {ex.Message}");
                }

                L($"[ChampSelect|Poll] invoke update currentChampionId={session.currentChampionId}");

                ChampSelectSessionUpdated?.Invoke(this, session);
            }
            catch (Exception ex)
            {
                L($"[ChampSelect|Poll] exception: {ex.Message}");
            }
        }

        // ─── Public API ────────────────────────────────────────────────────────
        public HttpClient? GetHttpClient() => _client;

        public async Task<T?> GetAsync<T>(string endpoint)
        {
            if (_client == null) return default;
            try
            {
                var resp = await _client.GetAsync(endpoint);
                if (!resp.IsSuccessStatusCode) return default;
                var json = await resp.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch { return default; }
        }

        /// <summary>
        /// Crée ou actualise une page dédiée à MixOverlays, puis l'active dans le client LoL.
        /// Les pages sont identifiées par leur nom afin de ne pas en créer une à chaque clic.
        /// </summary>
        public async Task<RunePageImportResult> UpsertRunePageAsync(ChampionRecommendation recommendation)
        {
            if (_client == null)
                return new RunePageImportResult { Message = "Client League of Legends non connecté." };
            if (!recommendation.IsCompleteRunePage || recommendation.SelectedPerkIds.Count != 9)
                return new RunePageImportResult { Message = "La recommandation ne contient pas une page de runes complète." };

            var pageName = $"MixOverlays - {recommendation.ChampionName} {FormatRole(recommendation.Role)}";
            var page = new JObject
            {
                ["name"] = pageName,
                ["primaryStyleId"] = recommendation.PrimaryStyleId,
                ["subStyleId"] = recommendation.SecondaryStyleId,
                ["selectedPerkIds"] = new JArray(recommendation.SelectedPerkIds),
                ["current"] = true
            };

            try
            {
                using var pagesResponse = await _client.GetAsync("/lol-perks/v1/pages");
                if (!pagesResponse.IsSuccessStatusCode)
                    return new RunePageImportResult { Message = "Impossible de lire les pages de runes du client." };

                var pagesJson = await pagesResponse.Content.ReadAsStringAsync();
                var pages = JArray.Parse(pagesJson);
                var existing = pages.OfType<JObject>().FirstOrDefault(p =>
                    string.Equals(p.Value<string>("name"), pageName, StringComparison.Ordinal));

                HttpResponseMessage response;
                if (existing?.Value<long?>("id") is long pageId)
                {
                    page["id"] = pageId;
                    page["isDeletable"] = true;
                    response = await _client.PutAsync(
                        $"/lol-perks/v1/pages/{pageId}",
                        new StringContent(page.ToString(Formatting.None), Encoding.UTF8, "application/json"));
                }
                else
                {
                    response = await _client.PostAsync(
                        "/lol-perks/v1/pages",
                        new StringContent(page.ToString(Formatting.None), Encoding.UTF8, "application/json"));
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        L($"[Runes] import HTTP {(int)response.StatusCode}: {body}");
                        return new RunePageImportResult
                        {
                            Message = response.StatusCode == System.Net.HttpStatusCode.BadRequest
                                ? "Le client a refusé la page de runes (vérifiez vos pages disponibles)."
                                : "Impossible d'enregistrer la page de runes dans le client."
                        };
                    }
                }

                L($"[Runes] page importée : '{pageName}', primary={recommendation.PrimaryStyleId}, secondary={recommendation.SecondaryStyleId}");
                return new RunePageImportResult { Success = true, Message = "Runes copiées et activées dans League of Legends." };
            }
            catch (Exception ex)
            {
                L($"[Runes] import exception: {ex.Message}");
                return new RunePageImportResult { Message = "Erreur de communication avec le client League of Legends." };
            }
        }

        private static string FormatRole(string? role) => role?.ToLowerInvariant() switch
        {
            "top" => "Top",
            "jungle" => "Jungle",
            "middle" => "Mid",
            "bottom" => "Bot",
            "support" => "Support",
            _ => ""
        };

        public async Task<LcuSummoner?> GetCurrentSummonerAsync() =>
            await GetAsync<LcuSummoner>("/lol-summoner/v1/current-summoner");
        public async Task<LcuSummoner?> GetSummonerByIdAsync(long summonerId) =>
            await GetAsync<LcuSummoner>($"/lol-summoner/v1/summoners/{summonerId}");
        public async Task<LcuSummoner?> GetSummonerByPuuidAsync(string puuid) =>
            await GetAsync<LcuSummoner>($"/lol-summoner/v2/summoners/by-puuid/{puuid}");

        // ─── Lockfile ──────────────────────────────────────────────────────────
        private static (int port, string password)? FindLockfile()
        {
            var result = TryFromProcessArgs();
            if (result != null) return result;
            result = TryFromProcessDirectory();
            if (result != null) return result;
            var installPath = GetLolInstallPath();
            if (installPath != null) { var lf = Path.Combine(installPath, "lockfile"); if (File.Exists(lf)) return ParseLockfile(lf); }
            string[] commonPaths =
            {
                @"C:\Riot Games\League of Legends", @"C:\Program Files\Riot Games\League of Legends",
                @"C:\Program Files (x86)\Riot Games\League of Legends", @"D:\Riot Games\League of Legends",
                @"D:\Program Files\Riot Games\League of Legends", @"E:\Riot Games\League of Legends",
                @"C:\Games\League of Legends", @"D:\Games\League of Legends",
            };
            foreach (var path in commonPaths) { var lf = Path.Combine(path, "lockfile"); if (File.Exists(lf)) return ParseLockfile(lf); }
            return null;
        }

        private static (int port, string password)? TryFromProcessArgs()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("LeagueClientUx"))
                {
                    var cmdLine = GetCommandLine(proc);
                    if (string.IsNullOrEmpty(cmdLine)) continue;
                    int port = 0; string password = string.Empty;
                    foreach (var arg in cmdLine.Split(' '))
                    {
                        if (arg.StartsWith("--app-port="))   int.TryParse(arg.Split('=')[1].Trim('"'), out port);
                        if (arg.StartsWith("--remoting-auth-token=")) password = arg.Split('=')[1].Trim('"');
                    }
                    if (port > 0 && !string.IsNullOrEmpty(password)) return (port, password);
                }
            }
            catch { }
            return null;
        }

        private static (int port, string password)? TryFromProcessDirectory()
        {
            try
            {
                foreach (var name in new[] { "LeagueClientUx", "LeagueClient" })
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                            if (dir == null) continue;
                            var lf = Path.Combine(dir, "lockfile");
                            if (File.Exists(lf)) return ParseLockfile(lf);
                        }
                        catch { }
                    }
            }
            catch { }
            return null;
        }

        private static string GetCommandLine(Process process)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                    return obj["CommandLine"]?.ToString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        private static (int port, string password)? ParseLockfile(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();
                var parts   = content.Split(':');
                if (parts.Length >= 5) return (int.Parse(parts[2].Trim()), parts[3].Trim());
            }
            catch { }
            return null;
        }

        private static string? GetLolInstallPath()
        {
            string[] regKeys = { @"SOFTWARE\WOW6432Node\Riot Games, Inc\League of Legends",
                                  @"SOFTWARE\Riot Games, Inc\League of Legends",
                                  @"SOFTWARE\WOW6432Node\Riot Games\League of Legends" };
            foreach (var key in regKeys)
            {
                try
                {
                    using var regKey = Registry.LocalMachine.OpenSubKey(key);
                    var loc = regKey?.GetValue("Location") as string ?? regKey?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(loc)) return loc;
                }
                catch { }
            }
            return null;
        }

        private void SetState(LcuState state)
        {
            if (CurrentState == state) return;
            L($"SetState: {CurrentState} → {state}");
            CurrentState = state;
            StateChanged?.Invoke(this, new LcuConnectionEventArgs { State = state });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _pollCancellation.Cancel();
            // Ne pas bloquer le thread UI : un callback LCU peut être en attente du Dispatcher.
            _ = _pollingTask.ContinueWith(
                task =>
                {
                    _client?.Dispose();
                    _client = null;
                    if (task.IsFaulted)
                        L($"Erreur pendant l'arrêt du polling: {task.Exception?.GetBaseException().Message}");
                    _pollCancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
