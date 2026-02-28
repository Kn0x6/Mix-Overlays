using System.Collections.Generic;
using MixOverlays.ViewModels;

namespace MixOverlays.Models
{
    // ─── LCU Models ────────────────────────────────────────────────────────────

    public class LcuChampSelectSession
    {
        public List<LcuMyTeam> myTeam { get; set; } = new();
        public List<LcuTheirTeam> theirTeam { get; set; } = new();
        public bool isSpectating { get; set; }
    }

    public class LcuMyTeam
    {
        public long summonerId { get; set; }
        public string puuid { get; set; } = string.Empty;
        public int championId { get; set; }
        public int assignedPosition { get; set; }
        public int cellId { get; set; }
        public int teamId { get; set; }
    }

    public class LcuTheirTeam
    {
        public long summonerId { get; set; }
        public string puuid { get; set; } = string.Empty;
        public int championId { get; set; }
        public int cellId { get; set; }
        public int teamId { get; set; }
    }

    public class LcuSummoner
    {
        public long summonerId { get; set; }
        public string puuid { get; set; } = string.Empty;
        public string displayName { get; set; } = string.Empty;
        public string gameName { get; set; } = string.Empty;
        public string tagLine { get; set; } = string.Empty;
        public int summonerLevel { get; set; }
        public long profileIconId { get; set; }
    }

    public class LcuGameflowSession
    {
        public string phase { get; set; } = string.Empty;
        public LcuGameData? gameData { get; set; }
    }

    public class LcuGameData
    {
        public long  gameId       { get; set; }
        public bool  isCustomGame { get; set; }
        // "queue" est un objet JSON — on ignore pour éviter crash désérialisation
        public List<LcuTeamMember>              teamOne                  { get; set; } = new();
        public List<LcuTeamMember>              teamTwo                  { get; set; } = new();
        public List<LcuPlayerChampionSelection> playerChampionSelections { get; set; } = new();
    }

    /// <summary>
    /// Joueur dans playerChampionSelections (partie custom / pratique)
    /// </summary>
    public class LcuPlayerChampionSelection
    {
        public string puuid      { get; set; } = string.Empty;
        public int    championId { get; set; }
        public int    spell1Id   { get; set; }
        public int    spell2Id   { get; set; }
    }

    public class LcuTeamMember
    {
        public long   summonerId   { get; set; }
        public string puuid        { get; set; } = string.Empty;
        public string summonerName { get; set; } = string.Empty;
        // Champs optionnels pré-remplis depuis le port 2999
        public string championName { get; set; } = string.Empty;
        public int    spell1Id     { get; set; }
        public int    spell2Id     { get; set; }
    }

    // ─── Riot API Models ───────────────────────────────────────────────────────

    public class RiotAccount
    {
        public string puuid { get; set; } = string.Empty;
        public string gameName { get; set; } = string.Empty;
        public string tagLine { get; set; } = string.Empty;
    }

    public class RiotSummoner
    {
        public string id { get; set; } = string.Empty;
        public string accountId { get; set; } = string.Empty;
        public string puuid { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public int profileIconId { get; set; }
        public long revisionDate { get; set; }
        public int summonerLevel { get; set; }
    }

    public class LeagueEntry
    {
        public string leagueId { get; set; } = string.Empty;
        public string summonerId { get; set; } = string.Empty;
        public string queueType { get; set; } = string.Empty;
        public string tier { get; set; } = string.Empty;
        public string rank { get; set; } = string.Empty;
        public int leaguePoints { get; set; }
        public int wins { get; set; }
        public int losses { get; set; }
        public bool hotStreak { get; set; }
        public bool veteran { get; set; }
        public bool freshBlood { get; set; }

        public double WinRate => wins + losses > 0 ? (double)wins / (wins + losses) * 100 : 0;
        public string DisplayRank => $"{tier} {rank}";
        public string LpDisplay => $"{leaguePoints} LP";
    }

    public class ChampionMastery
    {
        public string puuid { get; set; } = string.Empty;
        public int championId { get; set; }
        public int championLevel { get; set; }
        public long championPoints { get; set; }
        public long lastPlayTime { get; set; }
        public string ChampionName { get; set; } = string.Empty; // Filled by app
    }

    public class MatchDto
    {
        public MatchMetadata metadata { get; set; } = new();
        public MatchInfo info { get; set; } = new();
    }

    public class MatchMetadata
    {
        public string matchId { get; set; } = string.Empty;
        public List<string> participants { get; set; } = new();
    }

    public class MatchInfo
    {
        public long gameCreation { get; set; }
        public long gameDuration { get; set; }
        public string gameMode { get; set; } = string.Empty;
        public string gameType { get; set; } = string.Empty;
        public int queueId { get; set; }
        public List<MatchParticipant> participants { get; set; } = new();
    }

    public class MatchParticipant
    {
        public string puuid { get; set; } = string.Empty;
        public string summonerName { get; set; } = string.Empty;
        public string riotIdGameName { get; set; } = string.Empty;
        public string riotIdTagline { get; set; } = string.Empty;
        public int championId { get; set; }
        public string championName { get; set; } = string.Empty;
        public bool win { get; set; }
        public int kills { get; set; }
        public int deaths { get; set; }
        public int assists { get; set; }
        public int totalDamageDealt { get; set; }
        public int totalDamageDealtToChampions { get; set; }
        public int visionScore { get; set; }
        public int goldEarned { get; set; }
        public int totalMinionsKilled { get; set; }
        public int neutralMinionsKilled { get; set; }
        public string individualPosition { get; set; } = string.Empty;
        public string teamPosition { get; set; } = string.Empty;
        public int item0 { get; set; }
        public int item1 { get; set; }
        public int item2 { get; set; }
        public int item3 { get; set; }
        public int item4 { get; set; }
        public int item5 { get; set; }
        public int item6 { get; set; }
        public int summoner1Id { get; set; }
        public int summoner2Id { get; set; }
        public int spell1Casts { get; set; }
        public int spell2Casts { get; set; }

        // Perks (runes) — Riot API nested object
        public MatchPerks? perks { get; set; }

        public double KDA => deaths == 0 ? kills + assists : (double)(kills + assists) / deaths;
        public int CS => totalMinionsKilled + neutralMinionsKilled;

        /// <summary>ID de la rune principale (keystone) ex: 8005 = Press the Attack</summary>
        public int PrimaryRune => perks?.styles?.Count > 0
            ? (perks.styles[0].selections?.Count > 0 ? perks.styles[0].selections[0].perk : 0)
            : 0;
    }

    // ─── Perks (Runes) Models ──────────────────────────────────────────────────

    public class MatchPerks
    {
        public List<MatchPerkStyle> styles { get; set; } = new();
    }

    public class MatchPerkStyle
    {
        public string description { get; set; } = string.Empty;
        public int style { get; set; }   // ID du path (ex: 8000 = Précision)
        public List<MatchPerkSelection> selections { get; set; } = new();
    }

    public class MatchPerkSelection
    {
        public int perk { get; set; }  // ID de la rune individuelle
        public int var1 { get; set; }
        public int var2 { get; set; }
        public int var3 { get; set; }
    }

    // ─── Display / ViewModel Models ────────────────────────────────────────────

    public partial class PlayerData
    {
        public string Puuid { get; set; } = string.Empty;
        public string SummonerId { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string TagLine { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(TagLine) ? GameName : $"{GameName}#{TagLine}";
        public int ProfileIconId { get; set; }
        public int SummonerLevel { get; set; }
        public int TeamId { get; set; }
        public int ChampionId { get; set; }
        public string ChampionName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;

        // Ranked
        public LeagueEntry? SoloRank { get; set; }
        public LeagueEntry? FlexRank { get; set; }

        // Mastery
        public List<ChampionMastery> TopMasteries { get; set; } = new();

        // Match history
        public List<MatchSummary> RecentMatches { get; set; } = new();

        // Pagination : buffer d'IDs et offset courant
        public List<string> MatchIdBuffer { get; set; } = new();
        public int MatchesOffset { get; set; } = 0;
        public bool HasMoreMatches => MatchIdBuffer.Count > MatchesOffset;

        // State
        public bool IsLoading { get; set; } = true;
        public bool IsLoaded { get; set; } = false;
        public string? ErrorMessage { get; set; }

        // Live game
        public bool IsInGame { get; set; } = false;
        public string? CurrentChampionName { get; set; }
        public int LiveSpell1Id { get; set; }
        public int LiveSpell2Id { get; set; }
        public SpectatorGameInfo? ActiveGame { get; set; }
        public long LiveGameStartTime { get; set; } = 0; // epoch ms (gameStartTime du spectateur)
    }

    public class MatchSummary
    {
        public string MatchId { get; set; } = string.Empty;
        public bool Win { get; set; }
        public string ChampionName { get; set; } = string.Empty;
        public int ChampionId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int CS { get; set; }
        public double KDA => Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;
        public string Position { get; set; } = string.Empty;
        public long GameDuration { get; set; }
        public long GameCreation { get; set; }
        public string QueueName { get; set; } = string.Empty;
        public int QueueId { get; set; }
        public string GameModeDisplay => QueueHelper.GetQueueName(QueueId);

        // Sorts d'invocateur du joueur principal
        public int Summoner1Id { get; set; }
        public int Summoner2Id { get; set; }

        // Rune principale du joueur (keystone)
        public int PrimaryRuneId { get; set; }

        // Champion de l'opposant direct (même position, équipe adverse)
        public int    OpponentChampionId   { get; set; }
        public string OpponentChampionName { get; set; } = string.Empty;

        // Participants complets pour le détail de partie (stockés localement)
        public List<MatchParticipantSummary> AllParticipants { get; set; } = new();

        // État d'expansion pour l'affichage "face-à-face"
        public bool IsExpanded { get; set; } = false;
    }

    public class MatchParticipantSummary
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
        public string Position     { get; set; } = string.Empty;
        public int    TotalDamage  { get; set; }
        public int    GoldEarned   { get; set; }
        public int    VisionScore  { get; set; }
        public int[]  Items        { get; set; } = new int[7];
        public int    Summoner1Id  { get; set; }
        public int    Summoner2Id  { get; set; }
        public double KDA          => Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;

        // ─── Rang Solo/Duo (chargé en arrière-plan lors de l'ouverture du détail) ──
        public string Tier         { get; set; } = string.Empty;   // ex: "GOLD", "PLATINUM"
        public string Rank         { get; set; } = string.Empty;   // ex: "I", "II", "III", "IV"
        public int    LeaguePoints { get; set; }
        public string RankDisplay  => string.IsNullOrEmpty(Tier) ? "Unranked" : $"{Tier} {Rank}";
        public string LpDisplay    => string.IsNullOrEmpty(Tier) ? string.Empty : $"{LeaguePoints} LP";
    }

    // ─── Spectator API Models ──────────────────────────────────────────────────

    public class SpectatorGameInfo
    {
        public long gameId { get; set; }
        public string gameMode { get; set; } = string.Empty;
        public string gameType { get; set; } = string.Empty;
        public long gameStartTime { get; set; }
        public List<SpectatorParticipant>? participants { get; set; }
    }

    public class SpectatorParticipant
    {
        public string puuid { get; set; } = string.Empty;
        public string summonerName { get; set; } = string.Empty;
        public int championId { get; set; }
        public int teamId { get; set; }
        public int spell1Id { get; set; }
        public int spell2Id { get; set; }
    }

    // Modèle pour Spectator API v4 (fallback)
    public class SpectatorGameInfoV4
    {
        public long gameId { get; set; }
        public string gameMode { get; set; } = string.Empty;
        public string gameType { get; set; } = string.Empty;
        public long gameStartTime { get; set; }
        public List<SpectatorParticipantV4>? participants { get; set; }
    }

    public class SpectatorParticipantV4
    {
        public string summonerName { get; set; } = string.Empty;
        public string summonerId { get; set; } = string.Empty;
        public int championId { get; set; }
        public int teamId { get; set; }
        public int spell1Id { get; set; }
        public int spell2Id { get; set; }
    }

    // ─── Live Client Data API Models (port 2999) ───────────────────────────────

    /// <summary>
    /// Joueur retourné par https://127.0.0.1:2999/liveclientdata/playerlist
    /// riotId = "GameName#TAG" (nouveau format), summonerName = ancien format
    /// team = "ORDER" (equipe bleue) ou "CHAOS" (equipe rouge)
    /// </summary>
    public class Port2999Player
    {
        public string? puuid         { get; set; }
        public string? summonerName  { get; set; }
        public string? riotId        { get; set; }   // "GameName#TAG"
        public string? championName  { get; set; }
        public string? team          { get; set; }   // "ORDER" ou "CHAOS"
        public int     summonerLevel { get; set; }
        public Port2999Spells? summonerSpells { get; set; }
    }

    public class Port2999Spells
    {
        public Port2999Spell? summonerSpellOne { get; set; }
        public Port2999Spell? summonerSpellTwo { get; set; }
    }

    public class Port2999Spell
    {
        public string? displayName { get; set; }
        public string? rawDisplayName { get; set; }  // ex: "SummonerFlash"
    }

    public static class QueueHelper
    {
        private static readonly Dictionary<int, string> Queues = new()
        {
            [0]    = "Custom",
            [400]  = "Normal Draft",
            [420]  = "Ranked Solo",
            [430]  = "Normal Blind",
            [440]  = "Ranked Flex",
            [450]  = "ARAM",
            [700]  = "Clash",
            [830]  = "Co-op vs AI",
            [840]  = "Co-op vs AI",
            [850]  = "Co-op vs AI",
            [900]  = "URF",
            [1020] = "One for All",
            [1300] = "Nexus Blitz",
            [1400] = "Ultimate Spellbook",
            [1700] = "Arena",
            [1900] = "URF",
        };

        public static string GetQueueName(int queueId) =>
            Queues.TryGetValue(queueId, out var name) ? name : $"Mode {queueId}";
    }
}
