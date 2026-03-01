using System;
using System.Collections.Generic;
using System.Windows.Threading;
using MixOverlays.Models;
using MixOverlays.Services;

namespace MixOverlays.ViewModels
{
    public partial class PlayerViewModel : BaseViewModel
    {
        private PlayerData _data;
        private DispatcherTimer? _liveTimer;
        private string _liveTimerDisplay = string.Empty;

        public string LiveTimerDisplay
        {
            get => _liveTimerDisplay;
            private set { _liveTimerDisplay = value; OnPropertyChanged(nameof(LiveTimerDisplay)); }
        }

        public PlayerViewModel(PlayerData data)
        {
            _data = data;
            RefreshFromData();
        }

        public void UpdateData(PlayerData data)
        {
            try
            {
                _data = data ?? _data; // Ne pas remplacer par null
                RefreshFromData();
                UpdateLiveTimer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.UpdateData] Erreur: {ex.Message}");
            }
        }

        private void UpdateLiveTimer()
        {
            if (_data.IsInGame && _data.LiveGameStartTime > 0)
            {
                if (_liveTimer == null)
                {
                    _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _liveTimer.Tick += (_, _) => RefreshLiveTimer();
                    _liveTimer.Start();
                }
                RefreshLiveTimer();
            }
            else
            {
                _liveTimer?.Stop();
                _liveTimer = null;
                LiveTimerDisplay = string.Empty;
            }
        }

        private void RefreshLiveTimer()
        {
            if (_data.LiveGameStartTime <= 0) return;
            var startMs  = _data.LiveGameStartTime;
            var nowMs    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var elapsed  = TimeSpan.FromMilliseconds(nowMs - startMs);
            if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;
            LiveTimerDisplay = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        }

        private void RefreshFromData()
        {
            try
            {
                OnPropertyChanged(nameof(Puuid));
                OnPropertyChanged(nameof(GameName));
                OnPropertyChanged(nameof(TagLine));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(SummonerLevel));
                OnPropertyChanged(nameof(ProfileIconId));
                OnPropertyChanged(nameof(TeamId));
                OnPropertyChanged(nameof(ChampionName));
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(SoloRank));
                OnPropertyChanged(nameof(FlexRank));
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(IsLoaded));
                OnPropertyChanged(nameof(ErrorMessage));
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(TopMasteries));
                OnPropertyChanged(nameof(RecentMatches));
                OnPropertyChanged(nameof(HasMoreMatches));
                OnPropertyChanged(nameof(TopChampionName));
                OnPropertyChanged(nameof(SoloWinRate));
                OnPropertyChanged(nameof(SoloWins));
                OnPropertyChanged(nameof(SoloLosses));
                OnPropertyChanged(nameof(ProfileIconUrl));
                OnPropertyChanged(nameof(IsInGame));
                OnPropertyChanged(nameof(CurrentChampionName));
                OnPropertyChanged(nameof(LiveSpell1Id));
                OnPropertyChanged(nameof(LiveSpell2Id));
                OnPropertyChanged(nameof(PrimaryLane));
                OnPropertyChanged(nameof(SecondaryLane));
                OnPropertyChanged(nameof(HasLaneData));
                OnPropertyChanged(nameof(TopChampionsFromHistory));
                OnPropertyChanged(nameof(HasChampionStats));
                OnPropertyChanged(nameof(RecentWins));
                OnPropertyChanged(nameof(RecentLosses));
                OnPropertyChanged(nameof(RecentGames));
                OnPropertyChanged(nameof(RecentWinRate));
                OnPropertyChanged(nameof(RecentWinRateDisplay));
                OnPropertyChanged(nameof(RecentWinRateHex));
                OnPropertyChanged(nameof(RecentAvgKDA));
                OnPropertyChanged(nameof(RecentAvgKDADisplay));
                OnPropertyChanged(nameof(RecentAvgKDAHex));
                OnPropertyChanged(nameof(RecentAvgKillsDisplay));
                OnPropertyChanged(nameof(RecentAvgDeathsDisplay));
                OnPropertyChanged(nameof(RecentAvgAssistsDisplay));
                OnPropertyChanged(nameof(RecentAvgCSPerMinDisplay));
                OnPropertyChanged(nameof(RecentAvgCSDisplay));
                OnPropertyChanged(nameof(RecentAvgDurationDisplay));
                OnPropertyChanged(nameof(CurrentStreakDisplay));
                OnPropertyChanged(nameof(CurrentStreakHex));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.RefreshFromData] Erreur: {ex.Message}");
            }
        }

        // Expose underlying data for inter-ViewModel access
        public PlayerData Data => _data;

        public string Puuid        => _data.Puuid;
        public string GameName     => _data.GameName;
        public string TagLine      => _data.TagLine;
        public string DisplayName  => _data.DisplayName;
        public int SummonerLevel   => _data.SummonerLevel;
        public int ProfileIconId   => _data.ProfileIconId;
        public int TeamId          => _data.TeamId;
        public string ChampionName => _data.ChampionName;
        public string Position     => _data.Position;

        public LeagueEntry? SoloRank => _data.SoloRank;
        public LeagueEntry? FlexRank => _data.FlexRank;

        public double SoloWinRate => _data.SoloRank?.WinRate ?? 0;
        public int    SoloWins    => _data.SoloRank?.wins    ?? 0;
        public int    SoloLosses  => _data.SoloRank?.losses  ?? 0;

        public bool IsLoading              => _data.IsLoading;
        public bool IsLoaded               => _data.IsLoaded;
        public string? ErrorMessage        => _data.ErrorMessage;
        public bool HasError               => !string.IsNullOrEmpty(_data.ErrorMessage);
        public bool IsInGame               => _data.IsInGame;
        public string? CurrentChampionName => _data.CurrentChampionName;
        public int LiveSpell1Id            => _data.LiveSpell1Id;
        public int LiveSpell2Id            => _data.LiveSpell2Id;
        public SpectatorGameInfo? ActiveGame => _data.ActiveGame;

        public List<ChampionMastery> TopMasteries => _data.TopMasteries;
        public List<MatchSummary>    RecentMatches => _data.RecentMatches;
        public bool HasMoreMatches => _data.HasMoreMatches;

        /// <summary>Nom du champion le plus maîtrisé (pour le fond d'écran splash).</summary>
        public string TopChampionName => _data.TopMasteries.Count > 0 ? _data.TopMasteries[0].ChampionName : string.Empty;

        private bool _isLoadingMoreMatches;
        public bool IsLoadingMoreMatches
        {
            get => _isLoadingMoreMatches;
            set { _isLoadingMoreMatches = value; OnPropertyChanged(nameof(IsLoadingMoreMatches)); }
        }

        /// <summary>Rafraîchit les propriétés liées à l'historique après chargement d'une nouvelle page.</summary>
        public void RefreshMatchesAndPagination()
        {
            OnPropertyChanged(nameof(RecentMatches));
            OnPropertyChanged(nameof(HasMoreMatches));
            OnPropertyChanged(nameof(TopChampionsFromHistory));
            OnPropertyChanged(nameof(HasChampionStats));
            OnPropertyChanged(nameof(RecentWins));
            OnPropertyChanged(nameof(RecentLosses));
            OnPropertyChanged(nameof(RecentGames));
            OnPropertyChanged(nameof(RecentWinRate));
            OnPropertyChanged(nameof(RecentWinRateDisplay));
            OnPropertyChanged(nameof(RecentWinRateHex));
            OnPropertyChanged(nameof(RecentAvgKDA));
            OnPropertyChanged(nameof(RecentAvgKDADisplay));
            OnPropertyChanged(nameof(RecentAvgKDAHex));
            OnPropertyChanged(nameof(RecentAvgKillsDisplay));
            OnPropertyChanged(nameof(RecentAvgDeathsDisplay));
            OnPropertyChanged(nameof(RecentAvgAssistsDisplay));
            OnPropertyChanged(nameof(RecentAvgCSPerMinDisplay));
            OnPropertyChanged(nameof(RecentAvgCSDisplay));
            OnPropertyChanged(nameof(RecentAvgDurationDisplay));
            OnPropertyChanged(nameof(CurrentStreakDisplay));
            OnPropertyChanged(nameof(CurrentStreakHex));
        }
        public string ProfileIconUrl =>
            $"https://ddragon.leagueoflegends.com/cdn/{VersionHolder.Latest}/img/profileicon/{_data.ProfileIconId}.png";

        public string RankTierDisplay => _data.SoloRank != null
            ? $"{_data.SoloRank.tier} {_data.SoloRank.rank}"
            : "Unranked";

        public string RankLpDisplay => _data.SoloRank != null
            ? $"{_data.SoloRank.leaguePoints} LP"
            : string.Empty;

        /// <summary>
        /// Lane principale calculée à partir des 10 dernières parties (position la plus jouée).
        /// Retourne le nom de lane avec emoji, ou chaîne vide si pas de données.
        /// </summary>
        public string PrimaryLane   => ComputeLane(0);
        public string SecondaryLane => ComputeLane(1);
        public bool   HasLaneData   => !string.IsNullOrEmpty(PrimaryLane);

        private string ComputeLane(int rank)
        {
            if (_data.RecentMatches == null || _data.RecentMatches.Count == 0) return string.Empty;

            var laneCounts = _data.RecentMatches
                .Where(m => !string.IsNullOrEmpty(m.Position))
                .GroupBy(m => m.Position.ToUpper())
                .Select(g => (Lane: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

            if (rank >= laneCounts.Count) return string.Empty;
            return LaneDisplayName(laneCounts[rank].Lane);
        }

        private static string LaneDisplayName(string lane) => lane switch
        {
            "TOP"     => "🗡 Top",
            "JUNGLE"  => "🌿 Jungle",
            "MID"     => "⚡ Mid",
            "MIDDLE"  => "⚡ Mid",
            "BOTTOM"  => "🏹 Bot",
            "UTILITY" => "🛡 Support",
            "SUPPORT" => "🛡 Support",
            _         => lane
        };
    }
}
