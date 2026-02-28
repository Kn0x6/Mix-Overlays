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
    //  MainViewModel — Section HORS JEU
    //
    //  Responsabilités :
    //    • Mon Compte       → chargement automatique du compte LCU connecté
    //    • Recherche        → recherche manuelle de joueurs + historique
    //    • Détail de partie → ouverture d'une partie depuis l'historique
    //    • Live Game Detail → aperçu de la partie en cours d'un joueur recherché
    //    • Clé API          → test de validité
    //
    //  Dépendances :
    //    → _riot, _settings, _lcu, _champions (déclarés dans MainViewModel.cs)
    // ════════════════════════════════════════════════════════════════════════════
    public partial class MainViewModel
    {
        // ─── Mon Compte ───────────────────────────────────────────────────────
        private PlayerViewModel? _myAccount;
        public PlayerViewModel? MyAccount
        {
            get => _myAccount;
            set { SetField(ref _myAccount, value); OnPropertyChanged(nameof(HasMyAccount)); }
        }

        public bool HasMyAccount => _myAccount != null;

        private bool _isLoadingMyAccount;
        public bool IsLoadingMyAccount
        {
            get => _isLoadingMyAccount;
            set => SetField(ref _isLoadingMyAccount, value);
        }

        private string _myAccountError = string.Empty;
        public string MyAccountError
        {
            get => _myAccountError;
            set { SetField(ref _myAccountError, value); OnPropertyChanged(nameof(HasMyAccountError)); }
        }
        public bool HasMyAccountError => !string.IsNullOrEmpty(_myAccountError);

        // ─── Recherche ────────────────────────────────────────────────────────
        private string _searchInput = string.Empty;
        public string SearchInput
        {
            get => _searchInput;
            set => SetField(ref _searchInput, value);
        }

        private PlayerViewModel? _searchedPlayer;
        public PlayerViewModel? SearchedPlayer
        {
            get => _searchedPlayer;
            set { SetField(ref _searchedPlayer, value); OnPropertyChanged(nameof(HasSearchResult)); }
        }
        public bool HasSearchResult => _searchedPlayer != null;

        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set => SetField(ref _isSearching, value);
        }

        // ─── Historique des recherches ─────────────────────────────────────────
        private ObservableCollection<PlayerViewModel> _searchHistory = new();
        public ObservableCollection<PlayerViewModel> SearchHistory
        {
            get => _searchHistory;
            set => SetField(ref _searchHistory, value);
        }

        public bool HasSearchHistory => _searchHistory.Count > 0;

        private bool _hasSearchedAtLeastOnce = false;
        public bool HasSearchedAtLeastOnce
        {
            get => _hasSearchedAtLeastOnce;
            set => SetField(ref _hasSearchedAtLeastOnce, value);
        }

        public bool ShowSearchHistory => HasSearchedAtLeastOnce && HasSearchHistory;

        private PlayerViewModel? _selectedHistoryPlayer;
        public PlayerViewModel? SelectedHistoryPlayer
        {
            get => _selectedHistoryPlayer;
            set
            {
                if (SetField(ref _selectedHistoryPlayer, value) && value != null)
                {
                    SearchedPlayer = value;
                    SearchInput    = value.Data.DisplayName;
                }
            }
        }

        // ─── Face-to-face ──────────────────────────────────────────────────────
        private bool _isFaceToFaceExpanded = false;
        public bool IsFaceToFaceExpanded
        {
            get => _isFaceToFaceExpanded;
            set => SetField(ref _isFaceToFaceExpanded, value);
        }

        private List<MatchParticipantPair> _faceToFaceData = new();
        public List<MatchParticipantPair> FaceToFaceData
        {
            get => _faceToFaceData;
            set => SetField(ref _faceToFaceData, value);
        }

        // ─── Détail de partie ──────────────────────────────────────────────────
        private MatchDetailViewModel? _selectedMatch;
        public MatchDetailViewModel? SelectedMatch
        {
            get => _selectedMatch;
            set { SetField(ref _selectedMatch, value); OnPropertyChanged(nameof(HasSelectedMatch)); }
        }
        public bool HasSelectedMatch => _selectedMatch != null;

        // ─── Test clé API ──────────────────────────────────────────────────────
        private string? _apiTestMessage;
        public string? ApiTestMessage
        {
            get => _apiTestMessage;
            set => SetField(ref _apiTestMessage, value);
        }

        private bool _apiTestSuccess;
        public bool ApiTestSuccess
        {
            get => _apiTestSuccess;
            set => SetField(ref _apiTestSuccess, value);
        }

        // ─── Live Game Detail (depuis la page Recherche) ───────────────────────
        private LiveGameDetailViewModel? _liveGameDetail;
        public LiveGameDetailViewModel? LiveGameDetail
        {
            get => _liveGameDetail;
            set { SetField(ref _liveGameDetail, value); OnPropertyChanged(nameof(HasLiveGameDetail)); }
        }
        public bool HasLiveGameDetail => _liveGameDetail != null;

        // ─── Commandes Hors-Jeu ────────────────────────────────────────────────
        public RelayCommand SearchPlayerCommand     { get; private set; } = null!;
        public RelayCommand ClearSearchCommand      { get; private set; } = null!;
        public RelayCommand RefreshMyAccountCommand { get; private set; } = null!;
        public RelayCommand OpenMatchDetailCommand  { get; private set; } = null!;
        public RelayCommand CloseMatchDetailCommand { get; private set; } = null!;
        public RelayCommand TestApiKeyCommand       { get; private set; } = null!;
        public RelayCommand ViewLiveGameCommand     { get; private set; } = null!;
        public RelayCommand CloseLiveGameCommand    { get; private set; } = null!;
        public RelayCommand LoadMoreMatchesCommand  { get; private set; } = null!;
        public RelayCommand ToggleFaceToFaceCommand { get; private set; } = null!;

        // ─── Initialisation (appelée depuis le constructeur Core) ──────────────
        partial void InitializeOutOfGameCommands()
        {
            SearchPlayerCommand = new RelayCommand(
                async _ => await SearchPlayerAsync(),
                _ => !string.IsNullOrWhiteSpace(SearchInput) && !IsSearching);

            ClearSearchCommand = new RelayCommand(_ =>
            {
                SearchedPlayer = null;
                SearchInput    = string.Empty;
            });

            RefreshMyAccountCommand = new RelayCommand(
                async _ => await LoadMyAccountAsync(),
                _ => !IsLoadingMyAccount && IsConnected);

            OpenMatchDetailCommand = new RelayCommand(async p =>
            {
                if (p is MatchSummary ms)
                    await OpenMatchDetailAsync(ms.MatchId);
            });

            CloseMatchDetailCommand = new RelayCommand(_ => SelectedMatch = null);

            TestApiKeyCommand = new RelayCommand(async _ => await TestApiKeyAsync());

            ViewLiveGameCommand = new RelayCommand(async p =>
            {
                if (p is PlayerViewModel pvm && pvm.ActiveGame != null)
                    await LoadLiveGameDetailAsync(pvm.ActiveGame, pvm.Data.Puuid);
            });

            CloseLiveGameCommand = new RelayCommand(_ => LiveGameDetail = null);

            LoadMoreMatchesCommand = new RelayCommand(async p =>
            {
                if (p is PlayerViewModel pvm)
                    await LoadMoreMatchesAsync(pvm);
            });

            ToggleFaceToFaceCommand = new RelayCommand(async p =>
            {
                if (p is MatchSummary ms) await ToggleFaceToFaceAsync(ms);
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Mon Compte
        // ══════════════════════════════════════════════════════════════════════

        public async Task LoadMyAccountAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.Current.RiotApiKey))
                {
                    MyAccountError = "⚠ Clé API Riot manquante. Configurez-la dans Paramètres.";
                    return;
                }

                IsLoadingMyAccount = true;
                MyAccountError = string.Empty;

                var lcuMe = await _lcu.GetCurrentSummonerAsync();
                if (lcuMe == null)
                {
                    MyAccountError = "Impossible de récupérer le compte connecté.";
                    return;
                }

                string gameName = lcuMe.gameName.Length > 0 ? lcuMe.gameName : lcuMe.displayName;
                string tagLine  = lcuMe.tagLine;

                if (string.IsNullOrEmpty(tagLine))
                    tagLine = _settings.Current.Region;

                var account = await _riot.GetAccountByRiotIdAsync(gameName, tagLine);
                if (account == null)
                {
                    if (!string.IsNullOrEmpty(lcuMe.displayName) && lcuMe.displayName != gameName)
                        account = await _riot.GetAccountByRiotIdAsync(lcuMe.displayName, _settings.Current.Region);

                    if (account == null)
                    {
                        MyAccountError = $"Joueur '{gameName}#{tagLine}' introuvable via l'API.";
                        return;
                    }
                }

                var placeholder = new PlayerData
                {
                    Puuid     = account.puuid,
                    GameName  = account.gameName,
                    TagLine   = account.tagLine,
                    IsLoading = true
                };
                MyAccount     = new PlayerViewModel(placeholder);
                StatusMessage = $"Chargement de {account.gameName}#{account.tagLine}...";

                var fullData = await _riot.LoadFullPlayerDataAsync(account.puuid, account.gameName, account.tagLine);

                if (fullData != null)
                {
                    foreach (var m in fullData.TopMasteries)
                    {
                        try { m.ChampionName = _champions.GetName(m.championId); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[OutOfGame] Erreur champion mastery: {ex.Message}");
                            m.ChampionName = "Inconnu";
                        }
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            MyAccount?.UpdateData(fullData);
                            StatusMessage = $"Données chargées pour {account.gameName}#{account.tagLine}";
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[OutOfGame] Erreur mise à jour UI: {ex.Message}");
                            MyAccountError = $"Erreur d'affichage: {ex.Message}";
                        }
                    });
                }
                else
                {
                    MyAccountError = "Données du joueur non récupérées.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutOfGame] LoadMyAccount erreur complète: {ex}");
                MyAccountError = $"Erreur : {ex.Message}";
            }
            finally
            {
                IsLoadingMyAccount = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Recherche
        // ══════════════════════════════════════════════════════════════════════

        private async Task SearchPlayerAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchInput)) return;
            if (string.IsNullOrWhiteSpace(_settings.Current.RiotApiKey))
            {
                StatusMessage = "⚠ Clé API Riot manquante. Configurez-la dans Paramètres.";
                return;
            }

            IsSearching    = true;
            SearchedPlayer = null;
            StatusMessage  = $"Recherche de {SearchInput}...";

            try
            {
                string gameName, tagLine;
                if (SearchInput.Contains('#'))
                {
                    var parts = SearchInput.Split('#', 2);
                    gameName = parts[0].Trim();
                    tagLine  = parts[1].Trim();
                }
                else
                {
                    gameName = SearchInput.Trim();
                    tagLine  = _settings.Current.Region;
                }

                var account = await _riot.GetAccountByRiotIdAsync(gameName, tagLine);
                if (account == null)
                {
                    StatusMessage = $"Joueur '{SearchInput}' introuvable.";
                    return;
                }

                var placeholder = new PlayerData
                {
                    Puuid     = account.puuid,
                    GameName  = account.gameName,
                    TagLine   = account.tagLine,
                    IsLoading = true
                };
                SearchedPlayer = new PlayerViewModel(placeholder);
                StatusMessage  = $"Chargement de {account.gameName}#{account.tagLine}...";

                var fullData = await _riot.LoadFullPlayerDataAsync(account.puuid, account.gameName, account.tagLine);
                foreach (var m in fullData.TopMasteries) m.ChampionName = _champions.GetName(m.championId);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    SearchedPlayer.UpdateData(fullData);
                    StatusMessage = $"Données chargées pour {account.gameName}#{account.tagLine}";

                    HasSearchedAtLeastOnce = true;
                    OnPropertyChanged(nameof(ShowSearchHistory));

                    var existing = SearchHistory.FirstOrDefault(p => p.Data.Puuid == account.puuid);
                    if (existing != null)
                        SearchHistory.Remove(existing);

                    SearchHistory.Insert(0, SearchedPlayer);

                    while (SearchHistory.Count > 10)
                        SearchHistory.RemoveAt(SearchHistory.Count - 1);

                    OnPropertyChanged(nameof(HasSearchHistory));
                    OnPropertyChanged(nameof(ShowSearchHistory));
                });
            }
            catch (Exception ex) { StatusMessage = $"Erreur : {ex.Message}"; }
            finally { IsSearching = false; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Pagination des matchs
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadMoreMatchesAsync(PlayerViewModel pvm)
        {
            if (pvm.IsLoadingMoreMatches || !pvm.HasMoreMatches) return;

            try
            {
                pvm.IsLoadingMoreMatches = true;
                var newMatches = await _riot.LoadMoreMatchesAsync(pvm.Data);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    pvm.RefreshMatchesAndPagination();
                });

                System.Diagnostics.Debug.WriteLine($"[OutOfGame] {newMatches.Count} parties chargées.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutOfGame] LoadMoreMatches erreur: {ex.Message}");
            }
            finally
            {
                pvm.IsLoadingMoreMatches = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Détail de partie
        // ══════════════════════════════════════════════════════════════════════

        private async Task OpenMatchDetailAsync(string matchId)
        {
            if (string.IsNullOrEmpty(matchId)) return;

            MatchSummary? cached = MyAccount?.RecentMatches?.FirstOrDefault(m => m.MatchId == matchId)
                                ?? SearchedPlayer?.RecentMatches?.FirstOrDefault(m => m.MatchId == matchId);

            var detail = new MatchDetailViewModel
            {
                MatchId   = matchId,
                IsLoading = cached == null || cached.AllParticipants.Count == 0
            };
            SelectedMatch = detail;

            if (cached != null && cached.AllParticipants.Count > 0)
            {
                BuildMatchDetail(detail, cached);
                _ = LoadParticipantRanksAsync(detail);
            }
            else
            {
                try
                {
                    var match = await _riot.GetMatchByIdAsync(matchId);
                    if (match == null)
                    {
                        detail.ErrorMessage = "Partie introuvable.";
                        detail.IsLoading    = false;
                        return;
                    }

                    var tempSummary = new MatchSummary
                    {
                        MatchId         = matchId,
                        GameDuration    = match.info.gameDuration,
                        GameCreation    = match.info.gameCreation,
                        QueueId         = match.info.queueId,
                        AllParticipants = match.info.participants.Select(p => new MatchParticipantSummary
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
                            Items        = new[] { p.item0, p.item1, p.item2, p.item3, p.item4, p.item5, p.item6 },
                            Summoner1Id  = p.summoner1Id,
                            Summoner2Id  = p.summoner2Id
                        }).ToList()
                    };

                    BuildMatchDetail(detail, tempSummary);
                    _ = LoadParticipantRanksAsync(detail);
                }
                catch (Exception ex) { detail.ErrorMessage = ex.Message; }
                finally { detail.IsLoading = false; }
            }
        }

        private static void BuildMatchDetail(MatchDetailViewModel detail, MatchSummary summary)
        {
            detail.GameDuration = summary.GameDuration;
            detail.GameCreation = summary.GameCreation;
            detail.QueueId      = summary.QueueId;

            var participants = summary.AllParticipants.Select(p => new MatchParticipantViewModel
            {
                Puuid        = p.Puuid,
                GameName     = p.GameName,
                TagLine      = p.TagLine,
                ChampionName = p.ChampionName,
                ChampionId   = p.ChampionId,
                Kills        = p.Kills,
                Deaths       = p.Deaths,
                Assists      = p.Assists,
                CS           = p.CS,
                Win          = p.Win,
                Position     = p.Position,
                TotalDamage  = p.TotalDamage,
                GoldEarned   = p.GoldEarned,
                VisionScore  = p.VisionScore,
                Items        = p.Items,
                Summoner1Id  = p.Summoner1Id,
                Summoner2Id  = p.Summoner2Id,
                Tier         = p.Tier,
                Rank         = p.Rank,
                LeaguePoints = p.LeaguePoints,
            }).ToList();

            var blueTeam = participants.Where(p => p.Win).ToList();
            var redTeam  = participants.Where(p => !p.Win).ToList();

            detail.BlueTeam.Clear();
            foreach (var player in blueTeam) detail.BlueTeam.Add(player);

            detail.RedTeam.Clear();
            foreach (var player in redTeam) detail.RedTeam.Add(player);

            detail.Participants = new ObservableCollection<MatchParticipantViewModel>(participants);
            detail.PlayerRows   = BuildPlayerRows(blueTeam, redTeam);
            detail.IsLoading    = false;
        }

        private static List<MatchPlayerRow> BuildPlayerRows(
            List<MatchParticipantViewModel> blueTeam,
            List<MatchParticipantViewModel> redTeam)
        {
            var laneOrder = new[] { "TOP", "JUNGLE", "MID", "BOTTOM", "UTILITY" };
            var rows      = new List<MatchPlayerRow>();
            var usedBlue  = new HashSet<string>();
            var usedRed   = new HashSet<string>();

            foreach (var lane in laneOrder)
            {
                var blue = blueTeam.FirstOrDefault(p =>
                    !usedBlue.Contains(p.Puuid) &&
                    string.Equals(p.Position, lane, StringComparison.OrdinalIgnoreCase));

                var red = redTeam.FirstOrDefault(p =>
                    !usedRed.Contains(p.Puuid) &&
                    string.Equals(p.Position, lane, StringComparison.OrdinalIgnoreCase));

                if (blue == null && red == null) continue;

                if (blue != null) usedBlue.Add(blue.Puuid);
                if (red  != null) usedRed.Add(red.Puuid);

                rows.Add(new MatchPlayerRow { BluePlayer = blue, RedPlayer = red });
            }

            foreach (var p in blueTeam.Where(p => !usedBlue.Contains(p.Puuid)))
                rows.Add(new MatchPlayerRow { BluePlayer = p });
            foreach (var p in redTeam.Where(p => !usedRed.Contains(p.Puuid)))
                rows.Add(new MatchPlayerRow { RedPlayer = p });

            return rows;
        }

        private async Task LoadParticipantRanksAsync(MatchDetailViewModel detail)
        {
            var semaphore = new System.Threading.SemaphoreSlim(4, 4);

            var tasks = detail.Participants.Select(async participant =>
            {
                if (string.IsNullOrEmpty(participant.Puuid)) return;
                await semaphore.WaitAsync();
                try
                {
                    var entries = await _riot.GetLeagueEntriesByPuuidAsync(participant.Puuid);
                    var solo    = entries?.FirstOrDefault(e => e.queueType == "RANKED_SOLO_5x5");
                    if (solo != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            participant.Tier         = solo.tier;
                            participant.Rank         = solo.rank;
                            participant.LeaguePoints = solo.leaguePoints;
                        });
                    }
                }
                catch { /* rang non critique : restera "Unranked" */ }
                finally { semaphore.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);

            Application.Current.Dispatcher.Invoke(() =>
            {
                detail.PlayerRows = BuildPlayerRows(
                    detail.BlueTeam.ToList(),
                    detail.RedTeam.ToList());
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Live Game Detail (depuis Recherche)
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadLiveGameDetailAsync(SpectatorGameInfo game, string focusPuuid)
        {
            if (string.IsNullOrEmpty(_settings.Current.RiotApiKey)) return;

            var detail = new LiveGameDetailViewModel { IsLoading = true };
            Application.Current.Dispatcher.Invoke(() => LiveGameDetail = detail);

            if (game.participants == null || game.participants.Count == 0)
            {
                detail.IsLoading = false;
                return;
            }

            var grp   = game.participants.GroupBy(p => p.teamId).ToList();
            var side1 = grp.FirstOrDefault()?.ToList()         ?? new List<SpectatorParticipant>();
            var side2 = grp.Skip(1).FirstOrDefault()?.ToList() ?? new List<SpectatorParticipant>();

            bool focusInSide1     = side1.Any(p => p.puuid == focusPuuid);
            var allyParticipants  = focusInSide1 ? side1 : side2;
            var enemyParticipants = focusInSide1 ? side2 : side1;

            async Task LoadSideAsync(List<SpectatorParticipant> participants, ObservableCollection<PlayerViewModel> collection, int teamId)
            {
                foreach (var p in participants)
                {
                    string puuid    = p.puuid;
                    string champName = _champions.GetName(p.championId);
                    string gameName = p.summonerName;
                    string tagLine  = string.Empty;

                    if (!string.IsNullOrEmpty(puuid))
                    {
                        var account = await _riot.GetAccountByPuuidAsync(puuid);
                        if (account != null) { gameName = account.gameName; tagLine = account.tagLine; }
                    }

                    var pd = new PlayerData
                    {
                        Puuid        = puuid,
                        GameName     = gameName,
                        TagLine      = tagLine,
                        ChampionId   = p.championId,
                        ChampionName = champName,
                        TeamId       = teamId,
                        LiveSpell1Id = p.spell1Id,
                        LiveSpell2Id = p.spell2Id,
                        IsLoading    = true
                    };

                    Application.Current.Dispatcher.Invoke(() => collection.Add(new PlayerViewModel(pd)));

                    if (!string.IsNullOrEmpty(puuid))
                    {
                        var fullData = await _riot.LoadFullPlayerDataAsync(puuid, gameName, tagLine);
                        fullData.ChampionId   = p.championId;
                        fullData.ChampionName = champName;
                        fullData.TeamId       = teamId;
                        fullData.LiveSpell1Id = p.spell1Id;
                        fullData.LiveSpell2Id = p.spell2Id;
                        foreach (var m in fullData.TopMasteries) m.ChampionName = _champions.GetName(m.championId);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var vm = collection.FirstOrDefault(v => v.Data.Puuid == puuid);
                            vm?.UpdateData(fullData);
                        });
                    }
                }
            }

            Application.Current.Dispatcher.Invoke(() => detail.IsLoading = false);

            await Task.WhenAll(
                LoadSideAsync(allyParticipants,  detail.AllyTeam,  100),
                LoadSideAsync(enemyParticipants, detail.EnemyTeam, 200)
            );
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Test clé API
        // ══════════════════════════════════════════════════════════════════════

        private async Task TestApiKeyAsync()
        {
            ApiTestMessage = "⏳ Test en cours...";
            ApiTestSuccess = true;

            if (string.IsNullOrWhiteSpace(_settings.Current.RiotApiKey))
            {
                ApiTestMessage = "✗ Aucune clé API saisie";
                ApiTestSuccess = false;
                return;
            }

            _riot.RefreshApiKey();

            try
            {
                var me = await _lcu.GetCurrentSummonerAsync();
                if (me != null && !string.IsNullOrEmpty(me.puuid))
                {
                    var account = await _riot.GetAccountByPuuidAsync(me.puuid);
                    if (account != null)
                    {
                        ApiTestMessage = $"✓ Clé valide — {account.gameName}#{account.tagLine}";
                        ApiTestSuccess = true;
                        return;
                    }
                    ApiTestMessage = $"✗ Clé invalide ou expirée — {_riot.LastHttpError}";
                    ApiTestSuccess = false;
                    return;
                }

                var status = await _riot.TestPlatformStatusAsync();
                ApiTestMessage = status
                    ? $"✓ Clé valide sur {_settings.Current.Region}"
                    : $"✗ Échec — {_riot.LastHttpError}";
                ApiTestSuccess = status;
            }
            catch (Exception ex)
            {
                ApiTestMessage = $"✗ Exception : {ex.Message}";
                ApiTestSuccess = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MÉTHODES — Face-to-face
        // ══════════════════════════════════════════════════════════════════════

        private Task ToggleFaceToFaceAsync(MatchSummary match)
        {
            if (match == null) return Task.CompletedTask;

            IsFaceToFaceExpanded = !IsFaceToFaceExpanded;

            if (!IsFaceToFaceExpanded || match.AllParticipants.Count == 0)
            {
                FaceToFaceData = new List<MatchParticipantPair>();
                return Task.CompletedTask;
            }

            var pairs = new List<MatchParticipantPair>();
            var team1 = match.AllParticipants.Where(p => p.Win).ToList();
            var team2 = match.AllParticipants.Where(p => !p.Win).ToList();
            var count = Math.Max(team1.Count, team2.Count);
            for (int i = 0; i < count; i++)
            {
                pairs.Add(new MatchParticipantPair
                {
                    Ally  = i < team1.Count ? team1[i] : null,
                    Enemy = i < team2.Count ? team2[i] : null
                });
            }
            FaceToFaceData = pairs;
            return Task.CompletedTask;
        }
    }
}
