using System;
using System.Collections.Generic;
using System.Linq;
using MixOverlays.Models;

namespace MixOverlays.ViewModels
{
    /// <summary>
    /// Section dédiée au récapitulatif affiché automatiquement après une fin de partie.
    /// </summary>
    public partial class MainViewModel
    {
        private PostGameRecapViewModel? _postGameRecap;
        public PostGameRecapViewModel? PostGameRecap
        {
            get => _postGameRecap;
            private set
            {
                if (SetField(ref _postGameRecap, value))
                    OnPropertyChanged(nameof(HasPostGameRecap));
            }
        }

        public bool HasPostGameRecap => PostGameRecap != null;

        public RelayCommand ClosePostGameRecapCommand { get; private set; } = null!;
        public RelayCommand OpenPostGameDetailCommand { get; private set; } = null!;

        private void InitializePostGameRecapCommands()
        {
            ClosePostGameRecapCommand = new RelayCommand(_ => PostGameRecap = null);

            OpenPostGameDetailCommand = new RelayCommand(async _ =>
            {
                var matchId = PostGameRecap?.MatchId;
                if (string.IsNullOrWhiteSpace(matchId)) return;

                var recap = PostGameRecap;
                PostGameRecap = null;
                await OpenMatchDetailAsync(matchId);
            });
        }

        private void ShowPostGameRecap(MatchSummary match, string playerPuuid)
        {
            PostGameRecap = new PostGameRecapViewModel(match, playerPuuid);
        }
    }

    public class PostGameRecapViewModel
    {
        public PostGameRecapViewModel(MatchSummary match, string playerPuuid)
        {
            Match = match;
            MatchId = match.MatchId;

            Player = match.AllParticipants.FirstOrDefault(p => p.Puuid == playerPuuid)
                ?? new MatchParticipantSummary
                {
                    Puuid        = playerPuuid,
                    ChampionName = match.ChampionName,
                    ChampionId   = match.ChampionId,
                    Kills        = match.Kills,
                    Deaths       = match.Deaths,
                    Assists      = match.Assists,
                    CS           = match.CS,
                    Win          = match.Win,
                    Position     = match.Position,
                    GameDuration = match.GameDuration,
                    Summoner1Id  = match.Summoner1Id,
                    Summoner2Id  = match.Summoner2Id
                };

            Participants = match.AllParticipants.Count > 0
                ? match.AllParticipants
                    .OrderByDescending(p => p.Win)
                    .ThenByDescending(p => p.PerformanceScore)
                    .ThenByDescending(p => p.TotalDamage)
                    .ToList()
                : new List<MatchParticipantSummary> { Player };

            WinningTeam = Participants.Where(p => p.Win).ToList();
            LosingTeam  = Participants.Where(p => !p.Win).ToList();

            TeamKills = Participants
                .Where(p => p.Win == Player.Win)
                .Sum(p => p.Kills);

            EnemyTeamKills = Participants
                .Where(p => p.Win != Player.Win)
                .Sum(p => p.Kills);
        }

        public MatchSummary Match { get; }
        public string MatchId { get; }
        public MatchParticipantSummary Player { get; }
        public IReadOnlyList<MatchParticipantSummary> Participants { get; }
        public IReadOnlyList<MatchParticipantSummary> WinningTeam { get; }
        public IReadOnlyList<MatchParticipantSummary> LosingTeam { get; }
        public int TeamKills { get; }
        public int EnemyTeamKills { get; }

        public bool IsWin => Player.Win;
        public string ResultText => IsWin ? "VICTOIRE" : "DÉFAITE";
        public string ResultIcon => IsWin ? "🏆" : "💀";
        public string ResultHex => IsWin ? "#3FB950" : "#F85149";
        public string ResultBackgroundHex => IsWin ? "#1A3FB950" : "#1AF85149";
        public string QueueName => QueueHelper.GetQueueName(Match.QueueId);
        public long GameDuration => Match.GameDuration;
        public long GameCreation => Match.GameCreation;

        public string ChampionName => Player.ChampionName;
        public int ChampionId => Player.ChampionId;
        public string Position => Player.Position;
        public int Kills => Player.Kills;
        public int Deaths => Player.Deaths;
        public int Assists => Player.Assists;
        public int CS => Player.CS;
        public int TotalDamage => Player.TotalDamage;
        public int GoldEarned => Player.GoldEarned;
        public int VisionScore => Player.VisionScore;
        public int PerformanceScore => Player.PerformanceScore;
        public double KDA => Player.KDA;

        private double GameMinutes => GameDuration > 0 ? GameDuration / 60.0 : 0;

        public double KillParticipation => TeamKills > 0
            ? (Kills + Assists) * 100.0 / TeamKills
            : 0;

        public double DamagePerMin => GameMinutes > 0 ? TotalDamage / GameMinutes : 0;
        public double GoldPerMin => GameMinutes > 0 ? GoldEarned / GameMinutes : 0;
        public double VisionPerMin => GameMinutes > 0 ? VisionScore / GameMinutes : 0;

        public int DamageRank => GetRank(Participants, Player, p => p.TotalDamage);
        public int GoldRank => GetRank(Participants, Player, p => p.GoldEarned);
        public int VisionRank => GetRank(Participants, Player, p => p.VisionScore);
        public int KdaRank => GetRank(Participants, Player, p => p.KDA);
        public int PerformanceRank => GetRank(Participants, Player, p => p.PerformanceScore);

        public double CSPerMin
        {
            get
            {
                var minutes = GameDuration / 60.0;
                return minutes > 0 ? CS / minutes : 0;
            }
        }

        public string KdaLine => $"{Kills} / {Deaths} / {Assists}";
        public string CSPerMinDisplay => $"{CSPerMin:F1}/min";
        public string DamageDisplay => TotalDamage > 0 ? TotalDamage.ToString("N0") : "—";
        public string GoldDisplay => GoldEarned > 0 ? GoldEarned.ToString("N0") : "—";
        public string VisionDisplay => VisionScore > 0 ? VisionScore.ToString() : "—";
        public string KillParticipationDisplay => TeamKills > 0 ? $"{KillParticipation:F0}%" : "—";
        public string DamagePerMinDisplay => TotalDamage > 0 ? $"{DamagePerMin:F0}/min" : "—";
        public string GoldPerMinDisplay => GoldEarned > 0 ? $"{GoldPerMin:F0}/min" : "—";
        public string VisionPerMinDisplay => VisionScore > 0 ? $"{VisionPerMin:F1}/min" : "—";
        public string DamageRankDisplay => FormatRank(DamageRank);
        public string GoldRankDisplay => FormatRank(GoldRank);
        public string VisionRankDisplay => FormatRank(VisionRank);
        public string KdaRankDisplay => FormatRank(KdaRank);
        public string PerformanceRankDisplay => FormatRank(PerformanceRank);
        public string TeamKillsDisplay => TeamKills > 0 ? TeamKills.ToString() : "—";
        public string EnemyTeamKillsDisplay => EnemyTeamKills > 0 ? EnemyTeamKills.ToString() : "—";

        public MatchParticipantSummary? BestDamagePlayer => Participants.OrderByDescending(p => p.TotalDamage).FirstOrDefault();
        public MatchParticipantSummary? BestKdaPlayer => Participants.OrderByDescending(p => p.KDA).FirstOrDefault();
        public MatchParticipantSummary? BestVisionPlayer => Participants.OrderByDescending(p => p.VisionScore).FirstOrDefault();

        private static int GetRank<T>(IEnumerable<MatchParticipantSummary> participants, MatchParticipantSummary player, Func<MatchParticipantSummary, T> selector)
            where T : IComparable<T>
        {
            var ranked = participants
                .OrderByDescending(selector)
                .ToList();

            var index = ranked.FindIndex(p => string.Equals(p.Puuid, player.Puuid, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index + 1 : 0;
        }

        private static string FormatRank(int rank) => rank > 0 ? $"#{rank}" : "—";
    }
}