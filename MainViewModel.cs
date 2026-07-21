using System;
using System.Threading;
using System.Windows;
using MixOverlays.Models;
using MixOverlays.Services;

namespace MixOverlays.ViewModels
{
    // ════════════════════════════════════════════════════════════════════════════
    //  MainViewModel — Core (services partagés, état LCU, constructeur)
    //  Les deux autres parties sont dans :
    //    • MainViewModel.OutOfGame.cs  → Mon Compte, Recherche, Historique, Settings
    //    • MainViewModel.InGame.cs     → Live Session, Champ Select, Overlay
    // ════════════════════════════════════════════════════════════════════════════
    public partial class MainViewModel : BaseViewModel, IDisposable
    {
        // ─── Services partagés ────────────────────────────────────────────────
        //  Accessibles par les deux fichiers partiels via les champs protégés.
        private readonly LcuService          _lcu;
        private readonly RiotApiService      _riot;
        private readonly SettingsService     _settings;
        private readonly ChampionDataService _champions = new();
        private readonly ChampionRecommendationService _recommendations;
        private readonly LpTrackerService    _lpTracker = new();
        private int _postGameHistoryRefreshInProgress;
        private string _lastPostGameHistoryRefreshMatchId = string.Empty;
        private bool _disposed;

        // ─── Déclarations des méthodes partielles ─────────────────────────────
        //  Chaque fichier partiel implémente sa propre initialisation.
        partial void InitializeOutOfGameCommands();
        partial void InitializeInGame();

        // ─── État LCU / connexion ─────────────────────────────────────────────
        private string _statusMessage = "Connexion au client LoL...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        private LcuState _clientState = LcuState.Disconnected;
        public LcuState ClientState
        {
            get => _clientState;
            set
            {
                SetField(ref _clientState, value);
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsInChampSelect));
                OnPropertyChanged(nameof(IsInGame));
                OnPropertyChanged(nameof(IsLiveSessionAvailable));
                OnPropertyChanged(nameof(ShowChampionRecommendationPanel));
                OnPropertyChanged(nameof(IsWaitingForChampionLock));
                OnPropertyChanged(nameof(ClientStateDisplay));
                OnPropertyChanged(nameof(StatusDotColor));
            }
        }

        public bool IsConnected     => _clientState != LcuState.Disconnected;
        public bool IsInChampSelect => _clientState == LcuState.InChampSelect;
        public bool IsInGame        => _clientState == LcuState.InGame;
        public bool IsLiveSessionAvailable => IsInChampSelect || IsInGame;

        public string ClientStateDisplay => _clientState switch
        {
            LcuState.Disconnected  => "Client non détecté",
            LcuState.Connecting    => "Connexion...",
            LcuState.Connected     => "Client connecté",
            LcuState.InChampSelect => "Sélection de champion",
            LcuState.InGame        => "En partie",
            _                      => "Inconnu"
        };

        public string StatusDotColor => _clientState switch
        {
            LcuState.Disconnected  => "#EF4444",
            LcuState.Connecting    => "#F59E0B",
            LcuState.Connected     => "#10B981",
            LcuState.InChampSelect => "#3B82F6",
            LcuState.InGame        => "#8B5CF6",
            _                      => "#6B7280"
        };

        // ─── Settings (proxy partagé) ─────────────────────────────────────────
        public AppSettings Settings => _settings.Current;

        // ─── Commande Paramètres (partagée) ──────────────────────────────────
        public RelayCommand SaveSettingsCommand { get; private set; } = null!;
        public event EventHandler? SettingsSaved;

        // ─── Constructeur ─────────────────────────────────────────────────────
        public MainViewModel()
        {
            _settings = App.SettingsService ?? new SettingsService();
            _riot     = App.RiotApiService  ?? new RiotApiService(_settings, _champions);
            _lcu      = App.LcuService      ?? new LcuService();
            _recommendations = new ChampionRecommendationService(_champions);

            App.SettingsService ??= _settings;
            App.RiotApiService  ??= _riot;
            App.LcuService      ??= _lcu;

            // Commande paramètres (partagée entre les deux sections)
            SaveSettingsCommand = new RelayCommand(_ =>
            {
                _settings.Current.RegionalRoute = SettingsService.GetRegionalRoute(_settings.Current.Region);
                _settings.Save();
                _riot.RefreshApiKey();
                StatusMessage = "Paramètres sauvegardés.";
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            });

            // ── Initialisation des deux sections ──
            InitializeOutOfGameCommands();   // → MainViewModel.OutOfGame.cs
            InitializeInGame();              // → MainViewModel.InGame.cs

            // ── Événements LCU (état de connexion) ──
            _lcu.StateChanged += OnLcuStateChanged;

            // ── Événements LCU pour le tracker LP ──
            _lcu.StateChanged += OnLcuStateChangedForLpTracker;

            _lcu.GameflowSessionUpdated += OnGameflowSessionUpdated;

            _lpTracker.HistoryUpdated += OnLpHistoryUpdated;

            // Charger l'historique existant au démarrage
            App.Log($"[VM] Démarrage — historique LP chargé : {_lpTracker.LpHistory.Count} snapshots");
            if (MyAccount != null && _lpTracker.IsActivePlayer(MyAccount.Data.Puuid))
                MyAccount.SetLpHistory(_lpTracker.LpHistory);
            else
                App.Log("[VM] ⚠ MyAccount NULL au démarrage — SetLpHistory ignoré");

            _ = _champions.EnsureLoadedAsync();

        }

        private void OnLcuStateChangedForLpTracker(object? sender, LcuConnectionEventArgs e)
        {
            var client = _lcu.GetHttpClient();
            if (client != null)
                _lpTracker.SetLcuClient(client);
        }

        private async void OnGameflowSessionUpdated(object? sender, LcuGameflowSession session)
        {
            try
            {
                await _lpTracker.OnGameflowPhaseChanged(session.phase);

                if (IsPostGamePhase(session.phase))
                    await RefreshMyAccountMatchHistoryAfterGameAsync(session.phase);
            }
            catch (Exception ex)
            {
                App.Log($"[VM] Erreur traitement gameflow '{session.phase}': {ex.Message}");
            }
        }

        private void OnLpHistoryUpdated(object? sender, EventArgs e)
        {
            App.Log($"[VM] Historique LP actualisé — {_lpTracker.LpHistory.Count} snapshots");
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (MyAccount != null && _lpTracker.IsActivePlayer(MyAccount.Data.Puuid))
                    MyAccount.SetLpHistory(_lpTracker.LpHistory);
            });
        }

        // ─── Changement d'état LCU (core : gère la connexion et le compte) ───
        private void OnLcuStateChanged(object? sender, LcuConnectionEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ClientState = e.State;

                if (e.State == LcuState.Disconnected && !string.IsNullOrEmpty(_lcu.LastDiagnostic))
                    StatusMessage = _lcu.LastDiagnostic;
                else
                    StatusMessage = ClientStateDisplay;

                // Nettoyage des équipes géré par InGame
                OnLcuStateChangedInGame(e.State);

                // Chargement du compte géré par OutOfGame
                if (e.State == LcuState.Connected && MyAccount == null)
                    _ = LoadMyAccountAsync();

                // NOUVEAU : si déconnecté, charger depuis le cache
                if (e.State == LcuState.Disconnected && MyAccount == null)
                    _ = LoadMyAccountFromCacheAsync();
            });
        }

        // ─── Hook partiel pour InGame ─────────────────────────────────────────
        //  Appelé par OnLcuStateChanged pour que InGame nettoie ses équipes.
        partial void OnLcuStateChangedInGame(LcuState state);

        private static bool IsPostGamePhase(string? phase) => phase is
            "PreEndOfGame" or
            "WaitingForStats" or
            "EndOfGame";

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lcu.StateChanged -= OnLcuStateChanged;
            _lcu.StateChanged -= OnLcuStateChangedForLpTracker;
            _lcu.GameflowSessionUpdated -= OnGameflowSessionUpdated;
            _lcu.ChampSelectSessionUpdated -= OnChampSelectUpdated;
            _lcu.InGameSessionUpdated -= OnInGameSessionUpdated;
            _lpTracker.HistoryUpdated -= OnLpHistoryUpdated;
            _playerLoadSem.Dispose();
        }

    }


    public class MatchDetailViewModel : BaseViewModel
    {
        public string MatchId { get; set; } = string.Empty;

        private long _gameDuration;
        public long GameDuration
        {
            get => _gameDuration;
            set { if (SetField(ref _gameDuration, value)) OnPropertyChanged(nameof(DurationDisplay)); }
        }

        private long _gameCreation;
        public long GameCreation
        {
            get => _gameCreation;
            set => SetField(ref _gameCreation, value);
        }

        private int _queueId;
        public int QueueId
        {
            get => _queueId;
            set { if (SetField(ref _queueId, value)) OnPropertyChanged(nameof(QueueName)); }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }

        public string? ErrorMessage { get; set; }
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> Participants { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> BlueTeam     { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> RedTeam      { get; set; } = new();
        public System.Collections.Generic.IReadOnlyList<MatchParticipantViewModel> WinningTeam => BlueTeam;
        public System.Collections.Generic.IReadOnlyList<MatchParticipantViewModel> LosingTeam => RedTeam;

        private int _winningTeamKills;
        public int WinningTeamKills { get => _winningTeamKills; set => SetField(ref _winningTeamKills, value); }
        private int _losingTeamKills;
        public int LosingTeamKills { get => _losingTeamKills; set => SetField(ref _losingTeamKills, value); }
        private int _winningTeamGold;
        public int WinningTeamGold { get => _winningTeamGold; set => SetField(ref _winningTeamGold, value); }
        private int _losingTeamGold;
        public int LosingTeamGold { get => _losingTeamGold; set => SetField(ref _losingTeamGold, value); }
        private int _winningTeamDamage;
        public int WinningTeamDamage { get => _winningTeamDamage; set => SetField(ref _winningTeamDamage, value); }
        private int _losingTeamDamage;
        public int LosingTeamDamage { get => _losingTeamDamage; set => SetField(ref _losingTeamDamage, value); }
        private double _winningDamagePercent = 50;
        public double WinningDamagePercent
        {
            get => _winningDamagePercent;
            set
            {
                if (SetField(ref _winningDamagePercent, value))
                {
                    OnPropertyChanged(nameof(LosingDamagePercent));
                    OnPropertyChanged(nameof(WinningDamagePercentDisplay));
                    OnPropertyChanged(nameof(LosingDamagePercentDisplay));
                }
            }
        }
        public double LosingDamagePercent => 100 - WinningDamagePercent;
        public string WinningDamagePercentDisplay => $"{WinningDamagePercent:F0}%";
        public string LosingDamagePercentDisplay => $"{LosingDamagePercent:F0}%";
        private int _winningDragons;
        public int WinningDragons { get => _winningDragons; set => SetField(ref _winningDragons, value); }
        private int _losingDragons;
        public int LosingDragons { get => _losingDragons; set => SetField(ref _losingDragons, value); }
        private int _winningBarons;
        public int WinningBarons { get => _winningBarons; set => SetField(ref _winningBarons, value); }
        private int _losingBarons;
        public int LosingBarons { get => _losingBarons; set => SetField(ref _losingBarons, value); }
        private int _winningTowers;
        public int WinningTowers { get => _winningTowers; set => SetField(ref _winningTowers, value); }
        private int _losingTowers;
        public int LosingTowers { get => _losingTowers; set => SetField(ref _losingTowers, value); }
        private int _winningInhibitors;
        public int WinningInhibitors { get => _winningInhibitors; set => SetField(ref _winningInhibitors, value); }
        private int _losingInhibitors;
        public int LosingInhibitors { get => _losingInhibitors; set => SetField(ref _losingInhibitors, value); }
        private System.Collections.Generic.IReadOnlyList<int> _winningBans = Array.Empty<int>();
        public System.Collections.Generic.IReadOnlyList<int> WinningBans { get => _winningBans; set => SetField(ref _winningBans, value); }
        private System.Collections.Generic.IReadOnlyList<int> _losingBans = Array.Empty<int>();
        public System.Collections.Generic.IReadOnlyList<int> LosingBans { get => _losingBans; set => SetField(ref _losingBans, value); }
        private MatchParticipantViewModel? _mvpPlayer;
        public MatchParticipantViewModel? MvpPlayer { get => _mvpPlayer; set => SetField(ref _mvpPlayer, value); }

        private System.Collections.Generic.List<MatchPlayerRow> _playerRows = new();
        public System.Collections.Generic.List<MatchPlayerRow> PlayerRows
        {
            get => _playerRows;
            set => SetField(ref _playerRows, value);
        }

        public string QueueName => QueueHelper.GetQueueName(QueueId);

        public string DurationDisplay
        {
            get
            {
                var ts = System.TimeSpan.FromSeconds(GameDuration);
                return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            }
        }
    }

    public class MatchPlayerRow
    {
        public MatchParticipantViewModel? BluePlayer { get; set; }
        public MatchParticipantViewModel? RedPlayer  { get; set; }
    }

    public class MatchParticipantViewModel
    {
        public string Puuid        { get; set; } = string.Empty;
        public string GameName     { get; set; } = string.Empty;
        public string TagLine      { get; set; } = string.Empty;
        public string DisplayName  => string.IsNullOrEmpty(TagLine) ? GameName : $"{GameName}#{TagLine}";
        public string ChampionName { get; set; } = string.Empty;
        public int    ChampionId   { get; set; }
        public int    Kills        { get; set; }
        public int    Deaths       { get; set; }
        public int    Assists      { get; set; }
        public int    CS           { get; set; }
        public bool   Win          { get; set; }
        public int    TeamId       { get; set; }
        public string Position     { get; set; } = string.Empty;
        public int    TotalDamage  { get; set; }
        public int    GoldEarned   { get; set; }
        public int    VisionScore  { get; set; }
        public int[]  Items        { get; set; } = new int[7];
        public int    Summoner1Id  { get; set; }
        public int    Summoner2Id  { get; set; }
public double KDA          => Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;
public string KdaLine      => $"{Kills} / {Deaths} / {Assists}";
public string CSPerMinDisplay => GameDuration > 0 ? $"{CS / (GameDuration / 60.0):F1}/min" : "—";

public long GameDuration { get; set; }

public int PerformanceScore
{
    get
    {
        if (GameDuration <= 0) return 0;
        double min   = GameDuration / 60.0;
        double kda   = Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;
        double csMin = min > 0 ? CS / min : 0;
        double raw   = Math.Min(kda / 6.0, 1.0) * 40 + Math.Min(csMin / 7.0, 1.0) * 30 + (Win ? 30 : 0);
        int score    = Math.Clamp((int)Math.Round(raw), 0, 98);
        if (Win && kda >= 5.0 && csMin >= 6.0) return 100;
        if (kda >= 4.0 && score >= 75)         return 99;
        return score;
    }
}

        // ─── Rang Solo/Duo ────────────────────────────────────────────────────
        public string Tier         { get; set; } = string.Empty;
        public string Rank         { get; set; } = string.Empty;
        public int    LeaguePoints { get; set; }
        public string RankDisplay  => string.IsNullOrEmpty(Tier) ? "Unranked" : $"{Tier} {Rank}";
        public string LpDisplay    => string.IsNullOrEmpty(Tier) ? string.Empty : $"{LeaguePoints} LP";
    }

    // ─── Legacy ───────────────────────────────────────────────────────────────
    public class MatchParticipantPair
    {
        public MatchParticipantSummary? Ally  { get; set; }
        public MatchParticipantSummary? Enemy { get; set; }
    }

    public class MatchLaneViewModel
    {
        public string                     Position { get; set; } = string.Empty;
        public MatchParticipantViewModel? Ally     { get; set; }
        public MatchParticipantViewModel? Enemy    { get; set; }
    }
}
