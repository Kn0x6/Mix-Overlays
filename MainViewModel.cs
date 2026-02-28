using System;
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
    public partial class MainViewModel : BaseViewModel
    {
        // ─── Services partagés ────────────────────────────────────────────────
        //  Accessibles par les deux fichiers partiels via les champs protégés.
        private readonly LcuService          _lcu;
        private readonly RiotApiService      _riot;
        private readonly SettingsService     _settings;
        private readonly ChampionDataService _champions = new();

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
                OnPropertyChanged(nameof(ClientStateDisplay));
                OnPropertyChanged(nameof(StatusDotColor));
            }
        }

        public bool IsConnected     => _clientState != LcuState.Disconnected;
        public bool IsInChampSelect => _clientState == LcuState.InChampSelect;
        public bool IsInGame        => _clientState == LcuState.InGame;

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

        // ─── Constructeur ─────────────────────────────────────────────────────
        public MainViewModel()
        {
            _settings = App.SettingsService ?? new SettingsService();
            _riot     = App.RiotApiService  ?? new RiotApiService(_settings);
            _lcu      = App.LcuService      ?? new LcuService();

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
            });

            // ── Initialisation des deux sections ──
            InitializeOutOfGameCommands();   // → MainViewModel.OutOfGame.cs
            InitializeInGame();              // → MainViewModel.InGame.cs

            // ── Événements LCU (état de connexion) ──
            _lcu.StateChanged += OnLcuStateChanged;

            _ = _champions.EnsureLoadedAsync();
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
            });
        }

        // ─── Hook partiel pour InGame ─────────────────────────────────────────
        //  Appelé par OnLcuStateChanged pour que InGame nettoie ses équipes.
        partial void OnLcuStateChangedInGame(LcuState state);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ViewModels secondaires (partagés entre les deux sections)
    // ══════════════════════════════════════════════════════════════════════════

    public class LiveGameDetailViewModel : BaseViewModel
    {
        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }

        private System.Collections.ObjectModel.ObservableCollection<PlayerViewModel> _allyTeam = new();
        public System.Collections.ObjectModel.ObservableCollection<PlayerViewModel> AllyTeam
        {
            get => _allyTeam;
            set => SetField(ref _allyTeam, value);
        }

        private System.Collections.ObjectModel.ObservableCollection<PlayerViewModel> _enemyTeam = new();
        public System.Collections.ObjectModel.ObservableCollection<PlayerViewModel> EnemyTeam
        {
            get => _enemyTeam;
            set => SetField(ref _enemyTeam, value);
        }
    }

    public class MatchDetailViewModel : BaseViewModel
    {
        public string MatchId      { get; set; } = string.Empty;
        public long   GameDuration { get; set; }
        public long   GameCreation { get; set; }
        public int    QueueId      { get; set; }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }

        public string? ErrorMessage { get; set; }
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> Participants { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> BlueTeam     { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<MatchParticipantViewModel> RedTeam      { get; set; } = new();

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
