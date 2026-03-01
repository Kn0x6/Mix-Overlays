using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MixOverlays.Models;
using MixOverlays.Services;

namespace MixOverlays.ViewModels
{
    // ════════════════════════════════════════════════════════════════════════════
    //  MainViewModel — Section EN JEU
    //
    //  Responsabilités :
    //    • Écoute des événements LCU (ChampSelect + InGame)
    //    • Chargement des joueurs alliés / ennemis en temps réel
    //    • Gestion de l'OverlayWindow (affichage / masquage)
    //    • Résolution des PUUIDs / noms via LCU + API Riot
    //
    //  Dépendances :
    //    → _riot, _settings, _lcu, _champions (déclarés dans MainViewModel.cs)
    // ════════════════════════════════════════════════════════════════════════════
    public partial class MainViewModel
    {
        // ─── Équipes en cours ─────────────────────────────────────────────────
        private ObservableCollection<PlayerViewModel> _allyTeam = new();
        public ObservableCollection<PlayerViewModel> AllyTeam
        {
            get => _allyTeam;
            set => SetField(ref _allyTeam, value);
        }

        private ObservableCollection<PlayerViewModel> _enemyTeam = new();
        public ObservableCollection<PlayerViewModel> EnemyTeam
        {
            get => _enemyTeam;
            set => SetField(ref _enemyTeam, value);
        }

        // IsInLiveSession : vrai dès qu'on a au moins un joueur chargé
        public bool IsInLiveSession => AllyTeam.Count > 0 || EnemyTeam.Count > 0;

        // ─── Initialisation (appelée depuis le constructeur Core) ──────────────
        partial void InitializeInGame()
        {
            // S'abonner aux événements LCU spécifiques à la partie
            _lcu.ChampSelectSessionUpdated += OnChampSelectUpdated;
            _lcu.InGameSessionUpdated      += OnInGameSessionUpdated;

            // Écouter les changements sur les équipes pour mettre à jour IsInLiveSession
            _allyTeam.CollectionChanged  += (_, __) => OnPropertyChanged(nameof(IsInLiveSession));
            _enemyTeam.CollectionChanged += (_, __) => OnPropertyChanged(nameof(IsInLiveSession));
        }

        // ─── Hook nettoyage équipes depuis Core (OnLcuStateChanged) ───────────
        partial void OnLcuStateChangedInGame(LcuState state)
        {
            if (state == LcuState.Connected || state == LcuState.Disconnected)
            {
                AllyTeam.Clear();
                EnemyTeam.Clear();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ÉVÉNEMENTS LCU
        // ══════════════════════════════════════════════════════════════════════

        private void OnChampSelectUpdated(object? sender, LcuChampSelectSession session)
        {
            _ = LoadChampSelectDataAsync(session);
        }

        private void OnInGameSessionUpdated(object? sender, LcuGameData gameData)
        {
            _ = LoadInGameDataAsync(gameData);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Chargement Champ Select
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadChampSelectDataAsync(LcuChampSelectSession session)
        {
            var allyData  = new List<PlayerData>();
            var enemyData = new List<PlayerData>();

            // ARAM : tous dans myTeam avec des teamIds différents, theirTeam vide
            bool isAram = !session.theirTeam.Any() && session.myTeam.Count > 5;
            System.Diagnostics.Debug.WriteLine($"[InGame|ChampSelect] my={session.myTeam.Count} their={session.theirTeam.Count} aram={isAram}");

            if (isAram)
            {
                int localTeamId = session.myTeam.FirstOrDefault(m => m.summonerId > 0)?.teamId ?? 100;
                foreach (var member in session.myTeam.Where(m => m.summonerId > 0))
                {
                    var pd = await ResolvePlayerFromLcuAsync(member.summonerId, member.puuid, member.teamId);
                    if (pd == null) continue;
                    pd.ChampionId = member.championId;
                    if (member.teamId == localTeamId) allyData.Add(pd);
                    else                              enemyData.Add(pd);
                }
            }
            else
            {
                foreach (var member in session.myTeam.Where(m => m.summonerId > 0))
                {
                    var pd = await ResolvePlayerFromLcuAsync(member.summonerId, member.puuid, member.teamId);
                    if (pd != null) { pd.ChampionId = member.championId; allyData.Add(pd); }
                }
                if (!session.isSpectating)
                {
                    foreach (var member in session.theirTeam.Where(m => m.summonerId > 0))
                    {
                        var pd = await ResolvePlayerFromLcuAsync(member.summonerId, member.puuid, member.teamId);
                        if (pd != null) { pd.ChampionId = member.championId; enemyData.Add(pd); }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[InGame|ChampSelect] ally={allyData.Count} enemy={enemyData.Count}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllyTeam.Clear();
                foreach (var pd in allyData)  AllyTeam.Add(new PlayerViewModel(pd));
                EnemyTeam.Clear();
                foreach (var pd in enemyData) EnemyTeam.Add(new PlayerViewModel(pd));
            });

            await Task.WhenAll(
                allyData .Select(pd => LoadAndRefreshPlayerAsync(pd, AllyTeam))
                .Concat(enemyData.Select(pd => LoadAndRefreshPlayerAsync(pd, EnemyTeam)))
            );
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Chargement En Jeu
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadInGameDataAsync(LcuGameData gameData)
        {
            System.Diagnostics.Debug.WriteLine($"[InGame|InGame] t1={gameData.teamOne.Count} t2={gameData.teamTwo.Count}");

            // Résolution inline des PUUIDs manquants (filet de sécurité)
            async Task ResolvePuuids(List<LcuTeamMember> members)
            {
                foreach (var m in members.Where(m => string.IsNullOrEmpty(m.puuid) && m.summonerId > 0))
                {
                    var s = await _lcu.GetSummonerByIdAsync(m.summonerId);
                    if (s?.puuid is { Length: > 0 } p) m.puuid = p;
                }
            }
            await ResolvePuuids(gameData.teamOne);
            await ResolvePuuids(gameData.teamTwo);

            var meSummoner = await _lcu.GetCurrentSummonerAsync();
            string myPuuid = meSummoner?.puuid ?? string.Empty;

            List<LcuTeamMember> myTeam;
            List<LcuTeamMember> theirTeam;

            if (!string.IsNullOrEmpty(myPuuid))
            {
                bool inOne = gameData.teamOne.Any(m => m.puuid == myPuuid);
                myTeam    = inOne ? gameData.teamOne : gameData.teamTwo;
                theirTeam = inOne ? gameData.teamTwo : gameData.teamOne;
            }
            else
            {
                myTeam    = gameData.teamOne;
                theirTeam = gameData.teamTwo;
            }

            // Mode entraînement : aucun membre → ajouter le joueur courant seul
            if (!myTeam.Any() && !theirTeam.Any() && meSummoner != null)
            {
                System.Diagnostics.Debug.WriteLine("[InGame|InGame] équipes vides, ajout joueur courant");
                myTeam = new List<LcuTeamMember>
                {
                    new() { puuid = meSummoner.puuid, summonerName = meSummoner.displayName, summonerId = meSummoner.summonerId }
                };
            }

            // Mode custom/pratique : si playerChampionSelections est vide mais que le joueur est en jeu
            if (gameData.playerChampionSelections == null || !gameData.playerChampionSelections.Any())
            {
                System.Diagnostics.Debug.WriteLine("[InGame|InGame] playerChampionSelections vide, fallback sur LCU session");
                // Utiliser les données de la session LCU pour remplir les spells
                foreach (var member in myTeam.Concat(theirTeam))
                {
                    if (member.summonerId > 0)
                    {
                        var lcuSummoner = await _lcu.GetSummonerByIdAsync(member.summonerId);
                        if (lcuSummoner != null)
                        {
                            // Essayer de récupérer les spells via l'API LCU
                            try
                            {
                                var spells = await _lcu.GetAsync<Port2999Spells>($"/lol-summoner/v1/summoners/{member.summonerId}/summoner-spells");
                                if (spells?.summonerSpellOne != null)
                                    member.spell1Id = SpellNameToId(spells.summonerSpellOne.rawDisplayName);
                                if (spells?.summonerSpellTwo != null)
                                    member.spell2Id = SpellNameToId(spells.summonerSpellTwo.rawDisplayName);
                            }
                            catch { }
                        }
                    }
                }
            }

            async Task<PlayerData?> Resolve(LcuTeamMember m, int teamId)
            {
                if (string.IsNullOrEmpty(m.puuid)) return null;
                var account  = await _riot.GetAccountByPuuidAsync(m.puuid);
                var gameName = account?.gameName ?? m.summonerName;
                var tagLine  = account?.tagLine  ?? string.Empty;

                // FIX : séparer GameName#TagLine si combinés
                if (!string.IsNullOrEmpty(gameName) && gameName.Contains('#'))
                {
                    var parts = gameName.Split('#', 2);
                    gameName = parts[0];
                    if (string.IsNullOrEmpty(tagLine) && parts.Length > 1)
                        tagLine = parts[1];
                }

                if (string.IsNullOrEmpty(gameName)) return null;

                // ── Récupérer champion + spells depuis playerChampionSelections ──
                var champSel = gameData.playerChampionSelections
                    ?.FirstOrDefault(c => c.puuid == m.puuid);

                var championName = m.championName;
                int spell1 = m.spell1Id;
                int spell2 = m.spell2Id;

                // playerChampionSelections a priorité (plus fiable, contient championId)
                if (champSel != null)
                {
                    if (champSel.championId > 0 && string.IsNullOrEmpty(championName))
                        championName = ChampionIdToName(champSel.championId);
                    if (champSel.spell1Id > 0) spell1 = champSel.spell1Id;
                    if (champSel.spell2Id > 0) spell2 = champSel.spell2Id;
                }

                // DEBUG: Log resolution details
                System.Diagnostics.Debug.WriteLine($"[DEBUG Resolve] puuid={m.puuid?[..Math.Min(8, m.puuid?.Length ?? 0)]}... champName={championName} spell1={spell1} spell2={spell2} champSel={(champSel != null ? "FOUND" : "NULL")}");

                return new PlayerData
                {
                    Puuid             = m.puuid ?? string.Empty,
                    GameName          = gameName,
                    TagLine           = tagLine,
                    TeamId            = teamId,
                    IsLoading         = true,
                    ChampionId        = champSel?.championId ?? 0,
                    ChampionName      = championName,
                    CurrentChampionName = championName,
                    LiveSpell1Id      = spell1,
                    LiveSpell2Id      = spell2,
                    IsInGame          = true
                };
            }


            var aRes = await Task.WhenAll(myTeam   .Select(m => Resolve(m, 100)));
            var eRes = await Task.WhenAll(theirTeam.Select(m => Resolve(m, 200)));

            var allyData  = aRes.Where(p => p != null).Select(p => p!).ToList();
            var enemyData = eRes.Where(p => p != null).Select(p => p!).ToList();

            System.Diagnostics.Debug.WriteLine($"[InGame|InGame] ally={allyData.Count} enemy={enemyData.Count}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                AllyTeam.Clear();
                foreach (var pd in allyData)  AllyTeam.Add(new PlayerViewModel(pd));
                EnemyTeam.Clear();
                foreach (var pd in enemyData) EnemyTeam.Add(new PlayerViewModel(pd));
            });

            await Task.WhenAll(
                allyData .Select(pd => LoadAndRefreshPlayerAsync(pd, AllyTeam))
                .Concat(enemyData.Select(pd => LoadAndRefreshPlayerAsync(pd, EnemyTeam)))
            );
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Helpers de résolution / chargement
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Résout les infos d'un membre LCU (PUUID + RiotId) en <see cref="PlayerData"/>.
        /// Valide systématiquement le PUUID via Riot API et fallback par RiotId si nécessaire.
        /// </summary>
        private async Task<PlayerData?> ResolvePlayerFromLcuAsync(long summonerId, string puuid, int teamId)
        {
            string resolvedPuuid    = puuid;
            string resolvedGameName = string.Empty;
            string resolvedTagLine  = string.Empty;

            // Étape 1 : obtenir le PUUID via summonerId si vide
            if (string.IsNullOrEmpty(resolvedPuuid) && summonerId > 0)
            {
                var lcuSummoner = await _lcu.GetSummonerByIdAsync(summonerId);
                if (lcuSummoner != null) resolvedPuuid = lcuSummoner.puuid;
            }

            // Étape 2 : valider le PUUID via Riot API (Account endpoint)
            bool puuidValid = false;
            if (!string.IsNullOrEmpty(resolvedPuuid))
            {
                var account = await _riot.GetAccountByPuuidAsync(resolvedPuuid);
                if (account != null)
                {
                    puuidValid = true;
                    resolvedPuuid    = account.puuid; // normaliser
                    resolvedGameName = account.gameName;
                    resolvedTagLine  = account.tagLine;
                    App.Log($"[ResolvePlayer] PUUID validé via Account API → {resolvedGameName}#{resolvedTagLine}");
                }
            }

            // Étape 3 : fallback par summonerName si PUUID invalide
            if (!puuidValid && summonerId > 0)
            {
                var lcuSummoner = await _lcu.GetSummonerByIdAsync(summonerId);
                if (lcuSummoner != null)
                {
                    resolvedGameName = !string.IsNullOrEmpty(lcuSummoner.gameName) ? lcuSummoner.gameName : lcuSummoner.displayName;
                    resolvedTagLine  = lcuSummoner.tagLine;
                    
                    // Tentative de résolution par RiotId
                    if (!string.IsNullOrEmpty(resolvedGameName))
                    {
                        var accountByRiotId = await _riot.GetAccountByRiotIdAsync(resolvedGameName, resolvedTagLine);
                        if (accountByRiotId != null)
                        {
                            resolvedPuuid    = accountByRiotId.puuid;
                            resolvedGameName = accountByRiotId.gameName;
                            resolvedTagLine  = accountByRiotId.tagLine;
                            App.Log($"[ResolvePlayer] Résolu par RiotId → PUUID={resolvedPuuid?[..Math.Min(8, resolvedPuuid?.Length ?? 0)]}...");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(resolvedPuuid)) return null;

            // ── FIX : séparer GameName#TagLine si le LCU les combine ──
            if (resolvedGameName.Contains('#'))
            {
                var parts = resolvedGameName.Split('#', 2);
                resolvedGameName = parts[0];
                if (string.IsNullOrEmpty(resolvedTagLine) && parts.Length > 1)
                    resolvedTagLine = parts[1];
            }

            return new PlayerData
            {
                Puuid     = resolvedPuuid,
                GameName  = resolvedGameName,
                TagLine   = resolvedTagLine,
                TeamId    = teamId,
                IsLoading = true
            };
        }


        /// <summary>
        /// Charge les données complètes d'un joueur et met à jour le ViewModel correspondant.
        /// </summary>
        /// <summary>
        /// Convertit un championId en nom via le dictionnaire DDragon chargé.
        /// Retourne string.Empty si non trouvé.
        /// </summary>
        private static string ChampionIdToName(int championId)
        {
            // Si tu as déjà un dictionnaire statique id→name (ex: ChampionStore, DataDragonService)
            // utilise-le ici. Sinon fallback :
            return ChampionDataService.Instance?.GetName(championId) ?? string.Empty;
        }

        /// <summary>
        /// Convertit un nom de sort (rawDisplayName) en ID de sort.
        /// Copie de la méthode de LcuService pour éviter les dépendances circulaires.
        /// </summary>
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

        /// <summary>
        /// Charge les données complètes d'un joueur et met à jour le ViewModel correspondant.
        /// </summary>
        private async Task LoadAndRefreshPlayerAsync(PlayerData pd, ObservableCollection<PlayerViewModel> collection)
        {
            System.Diagnostics.Debug.WriteLine($"[LOAD] Début pour {pd.GameName}#{pd.TagLine} | PUUID={pd.Puuid?.Substring(0, Math.Min(8, pd.Puuid?.Length ?? 0))}... | ApiKey={!string.IsNullOrEmpty(_settings.Current.RiotApiKey)}");

            if (string.IsNullOrEmpty(_settings.Current.RiotApiKey))
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD] SKIP - ApiKey vide");
                return;
            }

            // ── FIX : Valider le PUUID LCU en le convertissant en PUUID Riot ──
            string? riotPuuid = pd.Puuid;
            string  gameName  = pd.GameName;
            string  tagLine   = pd.TagLine;

            if (!string.IsNullOrEmpty(riotPuuid))
            {
                // Tenter de valider le PUUID via l'Account API
                var account = await _riot.GetAccountByPuuidAsync(riotPuuid);
                if (account != null)
                {
                    // PUUID valide côté Riot — on met à jour gameName/tagLine si besoin
                    riotPuuid = account.puuid; // normaliser
                    if (!string.IsNullOrEmpty(account.gameName)) gameName = account.gameName;
                    if (!string.IsNullOrEmpty(account.tagLine)) tagLine = account.tagLine;
                    System.Diagnostics.Debug.WriteLine($"[LOAD] PUUID validé via Account API → {gameName}#{tagLine}");
                }
                else
                {
                    // PUUID LCU invalide côté Riot → fallback par RiotId
                    System.Diagnostics.Debug.WriteLine($"[LOAD] PUUID invalide côté Riot, fallback par RiotId: {gameName}#{tagLine}");
                    riotPuuid = null;
                }
            }

            // Fallback : résoudre par GameName#TagLine si le PUUID est invalide
            if (string.IsNullOrEmpty(riotPuuid) && !string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(tagLine))
            {
                var accountByRiotId = await _riot.GetAccountByRiotIdAsync(gameName, tagLine);
                if (accountByRiotId != null)
                {
                    riotPuuid = accountByRiotId.puuid;
                    gameName = accountByRiotId.gameName;
                    tagLine = accountByRiotId.tagLine;
                    System.Diagnostics.Debug.WriteLine($"[LOAD] Résolu par RiotId → PUUID={riotPuuid?.Substring(0, 8)}...");
                }
            }

            if (string.IsNullOrEmpty(riotPuuid))
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD] SKIP - Impossible de résoudre le PUUID pour {gameName}#{tagLine}");
                return;
            }

            // Mettre à jour le PUUID dans le PlayerData pour que le VM match
            string originalPuuid = pd.Puuid;
            pd.Puuid = riotPuuid!;
            pd.GameName = gameName;
            pd.TagLine = tagLine;

            // Mettre à jour le PUUID dans le ViewModel si changé
            if (originalPuuid != riotPuuid)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var vm = collection.FirstOrDefault(v => v.Data.Puuid == originalPuuid);
                    if (vm != null) vm.Data.Puuid = riotPuuid!;
                });
            }

            var fullData = await _riot.LoadFullPlayerDataAsync(riotPuuid!, gameName, tagLine);
            System.Diagnostics.Debug.WriteLine($"[LOAD] fullData pour {gameName}: SoloRank={fullData.SoloRank?.tier ?? "null"}, Icon={fullData.ProfileIconId}, Level={fullData.SummonerLevel}, Error={fullData.ErrorMessage ?? "none"}");

fullData.TeamId       = pd.TeamId;
fullData.ChampionId   = pd.ChampionId;
fullData.ChampionName = pd.ChampionId > 0
    ? _champions.GetName(pd.ChampionId)
    : pd.ChampionName;
fullData.LiveSpell1Id = pd.LiveSpell1Id;
fullData.LiveSpell2Id = pd.LiveSpell2Id;
foreach (var m in fullData.TopMasteries) m.ChampionName = _champions.GetName(m.championId);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = collection.FirstOrDefault(v => v.Data.Puuid == riotPuuid);
                System.Diagnostics.Debug.WriteLine($"[LOAD] VM trouvé={vm != null} pour {gameName} | Collection count={collection.Count}");
                vm?.UpdateData(fullData);
            });
        }

    }
}
