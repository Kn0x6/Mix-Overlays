using System.Collections.ObjectModel;
using System.Collections.Concurrent;
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
        // Limite le nombre de joueurs chargés en parallèle (évite le quota 80/2min Riot)
        private readonly SemaphoreSlim _playerLoadSem = new(3, 3);
        private string _lastChampSelectSignature = string.Empty;
        // Les données Port 2999 ne contiennent pas le rôle : conserver celui connu au champ select.
        private readonly ConcurrentDictionary<string, string> _liveRolesByPuuid = new();

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

        // ─── Recommandations Champion Select ──────────────────────────────────
        private int _lockedChampionId;
        public int LockedChampionId
        {
            get => _lockedChampionId;
            private set
            {
                if (SetField(ref _lockedChampionId, value))
                {
                    OnPropertyChanged(nameof(HasDetectedChampionForRecommendation));
                    OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
                }
            }
        }

        private string _lockedChampionName = string.Empty;
        public string LockedChampionName
        {
            get => _lockedChampionName;
            private set => SetField(ref _lockedChampionName, value);
        }

        private ChampionRecommendation? _championRecommendation;
        public ChampionRecommendation? ChampionRecommendation
        {
            get => _championRecommendation;
            private set
            {
                if (SetField(ref _championRecommendation, value))
                {
                    OnPropertyChanged(nameof(HasChampionRecommendation));
                    OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
                }
            }
        }

        private bool _isLoadingChampionRecommendation;
        public bool IsLoadingChampionRecommendation
        {
            get => _isLoadingChampionRecommendation;
            private set
            {
                if (SetField(ref _isLoadingChampionRecommendation, value))
                    OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
            }
        }

        private string _championRecommendationError = string.Empty;
        public string ChampionRecommendationError
        {
            get => _championRecommendationError;
            private set
            {
                if (SetField(ref _championRecommendationError, value))
                {
                    OnPropertyChanged(nameof(HasChampionRecommendationError));
                    OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
                }
            }
        }

        private int _lastRecommendationChampionId;
        private string _lastRecommendationRole = string.Empty;
        private int _recommendationRequestVersion;

        private string _lockedChampionRole = string.Empty;
        public string LockedChampionRole
        {
            get => _lockedChampionRole;
            private set
            {
                if (SetField(ref _lockedChampionRole, value))
                    OnPropertyChanged(nameof(LockedChampionRoleDisplay));
            }
        }

        public string LockedChampionRoleDisplay => LockedChampionRole switch
        {
            "top" => "Top",
            "jungle" => "Jungle",
            "middle" => "Mid",
            "bottom" => "Bot",
            "support" => "Support",
            _ => "Rôle non identifié"
        };

        private bool _isImportingRunes;
        public bool IsImportingRunes
        {
            get => _isImportingRunes;
            private set => SetField(ref _isImportingRunes, value);
        }

        private string _runeImportStatus = string.Empty;
        public string RuneImportStatus
        {
            get => _runeImportStatus;
            private set
            {
                if (SetField(ref _runeImportStatus, value))
                    OnPropertyChanged(nameof(HasRuneImportStatus));
            }
        }

        public bool HasRuneImportStatus => !string.IsNullOrWhiteSpace(RuneImportStatus);
        public RelayCommand ImportRunesCommand { get; private set; } = null!;

        public bool HasChampionRecommendation => ChampionRecommendation != null;
        public bool HasDetectedChampionForRecommendation => LockedChampionId > 0;
        public bool HasChampionRecommendationError => !string.IsNullOrWhiteSpace(ChampionRecommendationError);
        public bool IsWaitingForChampionLock => IsInChampSelect &&
            LockedChampionId <= 0 &&
            !IsLoadingChampionRecommendation &&
            !HasChampionRecommendation &&
            !HasChampionRecommendationError;
        public bool ShowChampionRecommendationPanel => IsInChampSelect;

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

            ImportRunesCommand = new RelayCommand(async _ => await ImportRunesAsync(), _ =>
                ChampionRecommendation?.IsCompleteRunePage == true && !IsImportingRunes);
        }

        // ─── Hook nettoyage équipes depuis Core (OnLcuStateChanged) ───────────
        partial void OnLcuStateChangedInGame(LcuState state)
        {
            if (state != LcuState.InChampSelect)
                ClearChampionRecommendation();

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
            _ = UpdateLockedChampionRecommendationAsync(session);

            // LCU republie la même session toutes les trois secondes. Réagir seulement
            // à un changement réel évite de reconstruire les cartes et de relancer les mêmes flux Riot.
            var sessionSignature = string.Join("|", session.myTeam
                .Select(m => $"M:{m.summonerId}:{m.puuid}:{m.teamId}:{m.championId}")
                .Concat(session.theirTeam.Select(m => $"T:{m.summonerId}:{m.puuid}:{m.teamId}:{m.championId}"))
                .OrderBy(value => value));
            if (string.Equals(sessionSignature, _lastChampSelectSignature, StringComparison.Ordinal))
            {
                App.Log("[InGame|ChampSelect] Session inchangée — rechargement joueurs ignoré.");
                return;
            }
            _lastChampSelectSignature = sessionSignature;

            bool isAram = !session.theirTeam.Any() && session.myTeam.Count > 5;
            App.Log($"[InGame|ChampSelect] my={session.myTeam.Count} their={session.theirTeam.Count} aram={isAram}");

            if (isAram)
            {
                int localTeamId = session.myTeam.FirstOrDefault(m => m.summonerId > 0)?.teamId ?? 100;
                var allMembers  = session.myTeam.Where(m => m.summonerId > 0).ToList();

                // ── Résolution PARALLÈLE de tous les membres ARAM ──
                var tasks   = allMembers.Select(m => ResolvePlayerFromLcuAsync(m.summonerId, m.puuid, m.teamId));
                var results = await Task.WhenAll(tasks);

                var allyData  = new List<PlayerData>();
                var enemyData = new List<PlayerData>();

                for (int i = 0; i < allMembers.Count; i++)
                {
                    var pd = results[i];
                    if (pd == null) continue;
                    pd.ChampionId = allMembers[i].championId;
                    if (allMembers[i].teamId == localTeamId) allyData.Add(pd);
                    else                                     enemyData.Add(pd);
                }

                Apply(allyData, enemyData);
            }
            else
            {
                var myMembers    = session.myTeam.Where(m => m.summonerId > 0).ToList();
                var theirMembers = session.isSpectating
                    ? new List<LcuTheirTeam>()
                    : session.theirTeam.Where(m => m.summonerId > 0).ToList();

                // ── Résolution PARALLÈLE des deux équipes simultanément ──
                var myTasks    = myMembers.Select(m => ResolvePlayerFromLcuAsync(m.summonerId, m.puuid, m.teamId));
                var theirTasks = theirMembers.Select(m => ResolvePlayerFromLcuAsync(m.summonerId, m.puuid, m.teamId));

                var allResults = await Task.WhenAll(myTasks.Concat(theirTasks));

                var allyData  = new List<PlayerData>();
                var enemyData = new List<PlayerData>();

                for (int i = 0; i < myMembers.Count; i++)
                {
                    var pd = allResults[i];
                    if (pd == null) continue;
                    pd.ChampionId = myMembers[i].championId;
                    pd.Position = myMembers[i].assignedPosition;
                    if (!string.IsNullOrWhiteSpace(pd.Puuid) && !string.IsNullOrWhiteSpace(pd.Position))
                        _liveRolesByPuuid[pd.Puuid] = pd.Position;
                    allyData.Add(pd);
                }
                for (int i = 0; i < theirMembers.Count; i++)
                {
                    var pd = allResults[myMembers.Count + i];
                    if (pd == null) continue;
                    pd.ChampionId = theirMembers[i].championId;
                    enemyData.Add(pd);
                }

                Apply(allyData, enemyData);
            }

            void Apply(List<PlayerData> allyData, List<PlayerData> enemyData)
            {
                App.Log($"[InGame|ChampSelect] ally={allyData.Count} enemy={enemyData.Count}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AllyTeam.Clear();
                    foreach (var pd in allyData)  AllyTeam.Add(new PlayerViewModel(pd));
                    EnemyTeam.Clear();
                    foreach (var pd in enemyData) EnemyTeam.Add(new PlayerViewModel(pd));
                });

                _ = Task.WhenAll(
                    allyData .Select(pd => LoadAndRefreshPlayerAsync(pd, AllyTeam))
                    .Concat(enemyData.Select(pd => LoadAndRefreshPlayerAsync(pd, EnemyTeam)))
                ).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() =>
                {
                    SortTeamByLane(AllyTeam);
                    SortTeamByLane(EnemyTeam);
                }));
            }
        }

        private async Task UpdateLockedChampionRecommendationAsync(LcuChampSelectSession session)
        {
            var lockedChampionId = DetectLocalLockedChampionId(session, out var detectionSource);
            var role = ResolveLocalRole(session);
            LogChampSelectRecommendationDiagnostics(session, lockedChampionId, detectionSource);

            if (lockedChampionId <= 0)
            {
                if (LockedChampionId > 0)
                    Application.Current.Dispatcher.Invoke(ClearChampionRecommendation);
                else
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnPropertyChanged(nameof(IsWaitingForChampionLock));
                        OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
                    });
                return;
            }

            if (lockedChampionId == _lastRecommendationChampionId && role == _lastRecommendationRole &&
                (ChampionRecommendation != null || IsLoadingChampionRecommendation))
                return;

            _lastRecommendationChampionId = lockedChampionId;
            _lastRecommendationRole = role;
            var requestVersion = ++_recommendationRequestVersion;

            await _champions.EnsureLoadedAsync();
            var championName = _champions.GetName(lockedChampionId);

            Application.Current.Dispatcher.Invoke(() =>
            {
                LockedChampionId = lockedChampionId;
                LockedChampionName = championName;
                LockedChampionRole = role;
                ChampionRecommendation = null;
                ChampionRecommendationError = string.Empty;
                RuneImportStatus = string.Empty;
                IsLoadingChampionRecommendation = true;
                OnPropertyChanged(nameof(IsWaitingForChampionLock));
            });

            try
            {
                var recommendation = await _recommendations.GetRecommendationAsync(lockedChampionId, role);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (requestVersion != _recommendationRequestVersion) return;
                    ChampionRecommendation = recommendation;
                    ChampionRecommendationError = recommendation == null
                        ? "Aucune recommandation disponible pour ce champion."
                        : string.Empty;
                    OnPropertyChanged(nameof(IsWaitingForChampionLock));
                });
            }
            catch (System.Exception ex)
            {
                App.Log($"[Recommendations] Erreur chargement champion {lockedChampionId}: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (requestVersion != _recommendationRequestVersion) return;
                    ChampionRecommendation = null;
                    ChampionRecommendationError = "Impossible de charger les recommandations en ligne.";
                    OnPropertyChanged(nameof(IsWaitingForChampionLock));
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (requestVersion != _recommendationRequestVersion) return;
                    IsLoadingChampionRecommendation = false;
                    OnPropertyChanged(nameof(IsWaitingForChampionLock));
                });
            }
        }

        private static int DetectLocalLockedChampionId(LcuChampSelectSession session, out string detectionSource)
        {
            detectionSource = "none";

            if (session.localPlayerCellId >= 0 && session.actions != null)
            {
                var localPickActions = session.actions
                    .SelectMany(group => group ?? new List<LcuChampSelectAction>())
                    .Where(action =>
                        action.actorCellId == session.localPlayerCellId &&
                        action.championId > 0 &&
                        string.Equals(action.type, "pick", System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var completedPick = localPickActions
                    .Where(action => action.completed)
                    .Select(action => action.championId)
                    .LastOrDefault();

                if (completedPick > 0)
                {
                    detectionSource = "local pick action completed";
                    return completedPick;
                }

            }

            // Fallback LCU : une fois le lock validé, certains clients exposent seulement
            // championId sur le membre local de myTeam.
            if (session.localPlayerCellId >= 0)
            {
                var myCellChampion = session.myTeam
                    .Where(member => member.cellId == session.localPlayerCellId && member.championId > 0)
                    .Select(member => member.championId)
                    .FirstOrDefault();

                if (myCellChampion > 0)
                {
                    detectionSource = "myTeam localPlayerCellId";
                    return myCellChampion;
                }
            }

            return 0;
        }

        private static string ResolveLocalRole(LcuChampSelectSession session)
        {
            var rawRole = session.myTeam
                .FirstOrDefault(member => member.cellId == session.localPlayerCellId)
                ?.assignedPosition;

            return rawRole?.Trim().ToUpperInvariant() switch
            {
                "TOP" => "top",
                "JUNGLE" => "jungle",
                "MIDDLE" or "MID" => "middle",
                "BOTTOM" or "BOT" => "bottom",
                "UTILITY" or "SUPPORT" => "support",
                _ => "middle"
            };
        }

        private static void LogChampSelectRecommendationDiagnostics(LcuChampSelectSession session, int detectedChampionId, string detectionSource)
        {
            try
            {
                var myTeam = string.Join(" | ", session.myTeam.Select(m =>
                    $"cell={m.cellId},sid={m.summonerId},champ={m.championId},team={m.teamId}"));

                var actions = session.actions == null
                    ? "actions=null"
                    : string.Join(" | ", session.actions
                        .SelectMany(group => group ?? new List<LcuChampSelectAction>())
                        .Where(a => string.Equals(a.type, "pick", System.StringComparison.OrdinalIgnoreCase) || a.championId > 0)
                        .Select(a => $"actor={a.actorCellId},type={a.type},champ={a.championId},done={a.completed}"));

                var localActions = session.actions == null
                    ? "actions=null"
                    : string.Join(" | ", session.actions
                        .SelectMany(group => group ?? new List<LcuChampSelectAction>())
                        .Where(a => a.actorCellId == session.localPlayerCellId)
                        .Select(a => $"type={a.type},champ={a.championId},done={a.completed}"));

                var localTeamMember = session.myTeam
                    .FirstOrDefault(m => m.cellId == session.localPlayerCellId);

                App.Log($"[Recommendations|ChampSelect] currentChampionId={session.currentChampionId} localCell={session.localPlayerCellId} detected={detectedChampionId} source='{detectionSource}' localTeamChampion={localTeamMember?.championId ?? 0} localActions=[{localActions}] myTeam=[{myTeam}] actions=[{actions}]");
            }
            catch (System.Exception ex)
            {
                App.Log($"[Recommendations|ChampSelect] diagnostic error: {ex.Message}");
            }
        }

        private void ClearChampionRecommendation()
        {
            _recommendationRequestVersion++;
            LockedChampionId = 0;
            LockedChampionName = string.Empty;
            LockedChampionRole = string.Empty;
            ChampionRecommendation = null;
            ChampionRecommendationError = string.Empty;
            RuneImportStatus = string.Empty;
            IsImportingRunes = false;
            IsLoadingChampionRecommendation = false;
            _lastRecommendationChampionId = 0;
            _lastRecommendationRole = string.Empty;
            OnPropertyChanged(nameof(IsWaitingForChampionLock));
        }

        private async Task ImportRunesAsync()
        {
            var recommendation = ChampionRecommendation;
            if (recommendation == null || !recommendation.IsCompleteRunePage || IsImportingRunes)
                return;

            IsImportingRunes = true;
            RuneImportStatus = string.Empty;
            try
            {
                var result = await _lcu.UpsertRunePageAsync(recommendation);
                RuneImportStatus = result.Message;
            }
            finally
            {
                IsImportingRunes = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Chargement En Jeu
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadInGameDataAsync(LcuGameData gameData)
        {
            App.Log($"[InGame|InGame] t1={gameData.teamOne.Count} t2={gameData.teamTwo.Count}");

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
                App.Log("[InGame|InGame] équipes vides, ajout joueur courant");
                myTeam = new List<LcuTeamMember>
                {
                    new() { puuid = meSummoner.puuid, summonerName = meSummoner.displayName, summonerId = meSummoner.summonerId }
                };
            }

            // Mode custom/pratique : si playerChampionSelections est vide mais que le joueur est en jeu
            if (gameData.playerChampionSelections == null || !gameData.playerChampionSelections.Any())
            {
                App.Log("[InGame|InGame] playerChampionSelections vide, fallback sur LCU session");
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
                // ── Récupérer champion + spells depuis playerChampionSelections ──
                var champSel = gameData.playerChampionSelections
                    ?.FirstOrDefault(c => c.puuid == m.puuid);

                var championId   = champSel?.championId ?? 0;
                var championName = m.championName;
                int spell1       = m.spell1Id;
                int spell2       = m.spell2Id;

                // playerChampionSelections a priorité (plus fiable, contient championId)
                if (champSel != null)
                {
                    if (champSel.championId > 0 && string.IsNullOrEmpty(championName))
                        championName = ChampionIdToName(champSel.championId);
                    if (champSel.spell1Id > 0) spell1 = champSel.spell1Id;
                    if (champSel.spell2Id > 0) spell2 = champSel.spell2Id;
                }

                return await ResolveLivePlayerDataAsync(
                    m.puuid,
                    m.summonerName,
                    teamId,
                    championId,
                    championName,
                    spell1,
                    spell2,
                    livePosition: !string.IsNullOrWhiteSpace(m.position)
                        ? m.position
                        : _liveRolesByPuuid.TryGetValue(m.puuid, out var rememberedRole) ? rememberedRole : string.Empty);
            }


            var aRes = await Task.WhenAll(myTeam   .Select(m => Resolve(m, 100)));
            var eRes = await Task.WhenAll(theirTeam.Select(m => Resolve(m, 200)));

            var allyData  = aRes.Where(p => p != null).Select(p => p!).ToList();
            var enemyData = eRes.Where(p => p != null).Select(p => p!).ToList();

            App.Log($"[InGame|InGame] ally={allyData.Count} enemy={enemyData.Count}");

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

            // ── Tri face-à-face par lane une fois toutes les données chargées ──
            Application.Current.Dispatcher.Invoke(() =>
            {
                SortTeamByLane(AllyTeam);
                SortTeamByLane(EnemyTeam);
            });
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
        /// Résout les infos d'un joueur live en <see cref="PlayerData"/>.
        /// Cette méthode centralise la logique utilisée par la page Live session et l'overlay in-game :
        /// priorité au PUUID + Account API, fallback RiotId GameName#TagLine, puis fallback nom brut LCU/2999.
        /// </summary>
        private async Task<PlayerData?> ResolveLivePlayerDataAsync(
            string? puuid,
            string? fallbackName,
            int teamId,
            int championId = 0,
            string? championName = null,
            int spell1Id = 0,
            int spell2Id = 0,
            SpectatorGameInfo? activeGame = null,
            long liveGameStartTime = 0,
            int liveRuneId = 0,
            string? livePosition = null)
        {
            string resolvedPuuid    = puuid ?? string.Empty;
            string resolvedGameName = fallbackName ?? string.Empty;
            string resolvedTagLine  = string.Empty;

            if (!string.IsNullOrWhiteSpace(resolvedPuuid))
            {
                try
                {
                    var account = await _riot.GetAccountByPuuidAsync(resolvedPuuid);
                    if (account != null)
                    {
                        resolvedPuuid    = account.puuid;
                        resolvedGameName = account.gameName;
                        resolvedTagLine  = account.tagLine;
                    }
                }
                catch { /* non critique : fallback sur le nom fourni par LCU/2999 */ }
            }

            SplitRiotId(ref resolvedGameName, ref resolvedTagLine);

            if (string.IsNullOrWhiteSpace(resolvedPuuid) &&
                !string.IsNullOrWhiteSpace(resolvedGameName) &&
                !string.IsNullOrWhiteSpace(resolvedTagLine))
            {
                try
                {
                    var account = await _riot.GetAccountByRiotIdAsync(resolvedGameName, resolvedTagLine);
                    if (account != null)
                    {
                        resolvedPuuid    = account.puuid;
                        resolvedGameName = account.gameName;
                        resolvedTagLine  = account.tagLine;
                    }
                }
                catch { /* non critique : affichage possible avec le nom seul */ }
            }

            if (string.IsNullOrWhiteSpace(resolvedGameName) && string.IsNullOrWhiteSpace(resolvedPuuid))
                return null;

            var resolvedChampionName = championName ?? string.Empty;
            if (championId > 0 && string.IsNullOrWhiteSpace(resolvedChampionName))
            {
                await _champions.EnsureLoadedAsync();
                resolvedChampionName = _champions.GetName(championId);
            }

            return new PlayerData
            {
                Puuid               = resolvedPuuid,
                GameName            = resolvedGameName,
                TagLine             = resolvedTagLine,
                TeamId              = teamId,
                ChampionId          = championId,
                ChampionName        = resolvedChampionName,
                Position            = livePosition ?? string.Empty,
                CurrentChampionName = resolvedChampionName,
                LiveSpell1Id        = spell1Id,
                LiveSpell2Id        = spell2Id,
                ActiveGame          = activeGame,
                LiveGameStartTime   = liveGameStartTime,
                LiveRuneId          = liveRuneId,
                IsInGame            = true,
                IsLoading           = true,
                IsLoaded            = false
            };
        }

        private static void SplitRiotId(ref string gameName, ref string tagLine)
        {
            if (string.IsNullOrWhiteSpace(gameName) || !gameName.Contains('#')) return;

            var parts = gameName.Split('#', 2);
            gameName = parts[0];
            if (string.IsNullOrWhiteSpace(tagLine) && parts.Length > 1)
                tagLine = parts[1];
        }


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

        /// <summary>
        /// Charge les données d'un joueur en priorisant le rang (2 appels API) pour un affichage rapide,
        /// évitant ainsi de saturer le quota Riot avec 9 appels par joueur en parallèle.
        /// </summary>
        private async Task LoadAndRefreshPlayerAsync(
            PlayerData pd,
            ObservableCollection<PlayerViewModel> collection)
        {
            var tag = $"[RANK|{pd.GameName}]";
            PlayerData? rankData = null;

            if (string.IsNullOrEmpty(_settings.Current.RiotApiKey))
            {
                App.Log($"{tag} ❌ SKIP — ApiKey vide");
                return;
            }
            if (string.IsNullOrEmpty(pd.Puuid))
            {
                App.Log($"{tag} ❌ SKIP — PUUID vide");
                return;
            }

            await _playerLoadSem.WaitAsync();
            try
            {

            // ── ÉTAPE 1 : Rang seul (2 appels) — rapide et prioritaire ────────
            try
            {
                rankData = await _riot.LoadRankPlayerDataAsync(pd.Puuid, pd.GameName, pd.TagLine);

                // Conserver les données live déjà résolues pour l'overlay / Live session.
                rankData.TeamId              = pd.TeamId;
                rankData.ChampionId          = pd.ChampionId;
                rankData.ChampionName        = pd.ChampionId > 0 ? _champions.GetName(pd.ChampionId) : pd.ChampionName;
                rankData.Position            = pd.Position;
                rankData.CurrentChampionName = pd.CurrentChampionName;
                rankData.LiveSpell1Id        = pd.LiveSpell1Id;
                rankData.LiveSpell2Id        = pd.LiveSpell2Id;
                rankData.ActiveGame          = pd.ActiveGame;
                rankData.LiveGameStartTime   = pd.LiveGameStartTime;
                rankData.LiveRuneId          = pd.LiveRuneId;
                rankData.IsInGame            = pd.IsInGame;

                App.Log($"{tag} ✅ Rang chargé : {rankData.SoloRank?.tier ?? "Non classé"}");

                // Mise à jour immédiate du VM avec le rang
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var vm = collection.FirstOrDefault(v => v.Data.Puuid == pd.Puuid)
                           ?? collection.FirstOrDefault(v => v.Data.GameName == pd.GameName);
                    vm?.UpdateData(rankData);
                });
            }
            catch (Exception ex)
            {
                App.Log($"{tag} ❌ Erreur LoadRankOnly : {ex.Message}");
            }

            // ── ÉTAPE 2 : maîtrise du champion sélectionné ────────────────────
            try
            {
                if (rankData is not { ChampionId: > 0 })
                {
                    App.Log($"[MasteryDiag|Load] Impossible de charger la maîtrise : joueur={pd.GameName}#{pd.TagLine}, championId={rankData?.ChampionId ?? 0}");
                    return;
                }

                App.Log($"[MasteryDiag|Load] Début : joueur={rankData.GameName}#{rankData.TagLine}, puuidApi={ShortPuuid(rankData.Puuid)}, puuidCarte={ShortPuuid(pd.Puuid)}, championId={rankData.ChampionId}, champion={rankData.ChampionName}");

                var mastery = await _riot.GetChampionMasteryAsync(rankData.Puuid, rankData.ChampionId);
                if (mastery != null)
                {
                    mastery.ChampionName = _champions.GetName(mastery.championId);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var vm = collection.FirstOrDefault(v => v.Data.Puuid == rankData.Puuid)
                              ?? collection.FirstOrDefault(v => v.Data.Puuid == pd.Puuid)
                              ?? collection.FirstOrDefault(v =>
                                  v.Data.GameName == rankData.GameName &&
                                  v.Data.TagLine == rankData.TagLine);
                        if (vm != null)
                        {
                            vm.Data.TopMasteries = new List<ChampionMastery> { mastery };
                            vm.UpdateData(vm.Data);
                            App.Log($"[MasteryDiag|UI] Carte mise à jour : carte={vm.GameName}#{vm.TagLine}, carteChampionId={vm.Data.ChampionId}, masteryChampionId={mastery.championId}, pointsReçus={mastery.championPoints}, pointsAffichés={vm.CurrentChampionMasteryPoints}");
                        }
                        else
                        {
                            App.Log($"[MasteryDiag|UI] Carte introuvable : puuidApi={ShortPuuid(rankData.Puuid)}, puuidInitial={ShortPuuid(pd.Puuid)}, riotId={rankData.GameName}#{rankData.TagLine}");
                        }
                    });
                }
                else
                {
                    App.Log($"[MasteryDiag|Load] Riot ne retourne aucune maîtrise : championId={rankData.ChampionId}, erreur='{_riot.LastHttpError}'");
                }
            }
            catch (Exception ex)
            {
                App.Log($"{tag} ⚠️ Maîtrise non chargée : {ex.Message}");
            }
            }
            finally
            {
                _playerLoadSem.Release();
            }
        }

        private static string ShortPuuid(string? puuid) =>
            string.IsNullOrWhiteSpace(puuid) ? "<vide>" :
            puuid.Length <= 12 ? puuid : $"{puuid[..8]}…{puuid[^4..]}";

        // ══════════════════════════════════════════════════════════════════════
        //  HELPER — Tri par lane (TOP → JUNGLE → MID → ADC → SUPPORT)
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Réordonne une ObservableCollection de PlayerViewModel dans l'ordre standard
        /// des lanes : TOP → JUNGLE → MID → ADC/BOT → SUPPORT.
        /// Utilise Move() pour éviter un rebuild complet (moins de flickering UI).
        /// </summary>
        private static void SortTeamByLane(ObservableCollection<PlayerViewModel> team)
        {
            var sorted = team.OrderBy(vm => LaneOrderIndex(vm.PrimaryLane)).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = team.IndexOf(sorted[i]);
                if (currentIndex != i)
                    team.Move(currentIndex, i);
            }
        }

        private static int LaneOrderIndex(string? laneDisplay) => laneDisplay switch
        {
            var s when s?.Contains("Top")     == true => 0,
            var s when s?.Contains("Jungle")  == true => 1,
            var s when s?.Contains("Mid")     == true => 2,
            var s when s?.Contains("Bot")     == true => 3,
            var s when s?.Contains("Support") == true => 4,
            _ => 5  // Lane inconnue → en dernier
        };

    }
}
