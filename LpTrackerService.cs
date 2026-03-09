using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MixOverlays.Models;
using Newtonsoft.Json;

namespace MixOverlays.Services
{
    /// <summary>
    /// Capture LP avant/après chaque partie et construit un historique LP.
    ///
    /// DEUX SOURCES DE DONNÉES (par ordre de priorité) :
    ///   1. Tracking temps réel : capture LP avant/après via LCU (données exactes)
    ///   2. Seed depuis Riot API : reconstruction depuis l'historique ranked (approx.)
    ///   3. Données prévisionnelles : si aucune partie ranked trouvée, génère une
    ///      courbe fictive ±20 LP pour que le graphique ne soit jamais vide.
    /// </summary>
    public class LpTrackerService
    {
        // ─── Persistance ──────────────────────────────────────────────────────
        private static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MixOverlays");
        private static readonly string DataFile = Path.Combine(DataFolder, "lp_history.json");

        // ─── État interne ─────────────────────────────────────────────────────
        private HttpClient? _lcuClient;
        private LpSnapshot? _preSnapshot;
        private string      _lastPhase   = string.Empty;
        private bool        _capturedPre = false;

        // ─── Données publiques ────────────────────────────────────────────────
        public List<LpSnapshot> LpHistory { get; private set; } = new();
        public event EventHandler? HistoryUpdated;

        private static void L(string m) => App.Log($"[LP Tracker] {m}");

        public LpTrackerService() => LoadHistory();

        public void SetLcuClient(HttpClient client) => _lcuClient = client;

        // ══════════════════════════════════════════════════════════════════════
        //  SEED DEPUIS L'HISTORIQUE RIOT API
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Construit un historique LP depuis les parties ranked déjà chargées.
        /// Si aucune partie ranked n'est disponible, génère des données prévisionnelles
        /// fictives (±20 LP) pour que le graphique affiche toujours quelque chose.
        ///
        /// Appeler après que MyAccount.Data est chargé :
        ///   _lpTracker.SeedFromMatchHistory(account.Data.RecentMatches, account.Data.SoloRank);
        /// </summary>
        public void SeedFromMatchHistory(List<MatchSummary> allMatches, LeagueEntry? soloRank)
        {
            // Ne rien faire si on a déjà un historique réel suffisant
            if (LpHistory.Count >= 3 && LpHistory.Any(s => s.LpDelta.HasValue))
            {
                L("Seed ignoré : historique réel existant.");
                return;
            }

            if (soloRank == null || string.IsNullOrEmpty(soloRank.tier))
            {
                L("Seed ignoré : joueur non classé.");
                return;
            }

            // Parties ranked Solo uniquement (queue 420)
            var ranked = allMatches
                .Where(m => m.QueueId == 420)
                .OrderByDescending(m => m.GameCreation)
                .Take(30)
                .ToList();

            if (ranked.Count >= 2)
                SeedFromRankedMatches(ranked, soloRank);
            else
                SeedForecast(soloRank, ranked); // Données prévisionnelles si pas assez de parties
        }

        // ─── Seed depuis parties réelles ─────────────────────────────────────

        private void SeedFromRankedMatches(List<MatchSummary> ranked, LeagueEntry soloRank)
        {
            L($"Seed réel : {ranked.Count} parties | {soloRank.leaguePoints} LP ({soloRank.tier} {soloRank.rank})");

            const int AVG_LP = 20;
            var snapshots = new List<LpSnapshot>();
            int lp = soloRank.leaguePoints;

            snapshots.Add(new LpSnapshot
            {
                Timestamp    = DateTime.UtcNow,
                LeaguePoints = lp,
                Tier         = soloRank.tier,
                Rank         = soloRank.rank,
            });

            foreach (var match in ranked)
            {
                int delta = match.Win ? AVG_LP : -AVG_LP;
                lp -= delta;

                snapshots.Add(new LpSnapshot
                {
                    Timestamp    = match.GameCreation > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(match.GameCreation).UtcDateTime
                        : DateTime.UtcNow.AddDays(-snapshots.Count),
                    LeaguePoints = Math.Max(0, lp),
                    Tier         = soloRank.tier,
                    Rank         = soloRank.rank,
                    LpDelta      = delta,
                    IsWin        = match.Win,
                    ChampionName = match.ChampionName,
                });
            }

            LpHistory = snapshots;
            SaveHistory();
            HistoryUpdated?.Invoke(this, EventArgs.Empty);
            L($"Seed terminé : {LpHistory.Count} snapshots.");
        }

        // ─── Données prévisionnelles (aucune partie ranked disponible) ────────

        /// <summary>
        /// Génère une courbe fictive de N points en partant du LP actuel,
        /// avec des victoires/défaites alternées de ±20 LP.
        /// Les points sont marqués comme "prévisionnel" (IsWin=null).
        /// </summary>
        private void SeedForecast(LeagueEntry soloRank, List<MatchSummary> partialRanked)
        {
            L($"Seed prévisionnel : LP={soloRank.leaguePoints} tier={soloRank.tier}");

            const int POINTS       = 20;   // nombre de points fictifs
            const int LP_PER_WIN   = 20;
            const int LP_PER_LOSS  = 20;
            const int HOURS_APART  = 3;    // espacement temporel fictif

            var snapshots   = new List<LpSnapshot>();
            int lp          = soloRank.leaguePoints;
            var now         = DateTime.UtcNow;

            // Point actuel (réel)
            snapshots.Add(new LpSnapshot
            {
                Timestamp    = now,
                LeaguePoints = lp,
                Tier         = soloRank.tier,
                Rank         = soloRank.rank,
                LpDelta      = null,
                ChampionName = "Actuel",
            });

            // Générer une courbe réaliste : légère tendance positive avec variance
            // Pattern : W W L W W L W L W W L W W W L W W L W L
            bool[] pattern = { true, true, false, true, true, false, true, false, true, true,
                               false, true, true, true, false, true, true, false, true, false };

            for (int i = 0; i < POINTS; i++)
            {
                bool isWin = pattern[i % pattern.Length];
                int delta  = isWin ? LP_PER_WIN : -LP_PER_LOSS;
                lp -= delta; // on remonte dans le temps

                snapshots.Add(new LpSnapshot
                {
                    Timestamp    = now.AddHours(-(i + 1) * HOURS_APART),
                    LeaguePoints = Math.Max(0, lp),
                    Tier         = soloRank.tier,
                    Rank         = soloRank.rank,
                    LpDelta      = delta,
                    IsWin        = null,  // null = prévisionnel (pas de vraie donnée)
                    ChampionName = "Prévisionnel",
                });
            }

            LpHistory = snapshots;
            // NE PAS sauvegarder les données fictives sur disque
            // (elles seront régénérées au prochain lancement jusqu'à avoir des vraies données)
            HistoryUpdated?.Invoke(this, EventArgs.Empty);
            L($"Seed prévisionnel terminé : {LpHistory.Count} points fictifs.");
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TRACKING TEMPS RÉEL
        // ══════════════════════════════════════════════════════════════════════

        public async Task OnGameflowPhaseChanged(string phase)
        {
            if (phase == _lastPhase) return;
            L($"Phase -> {phase}");
            _lastPhase = phase;

            switch (phase)
            {
                case "InProgress":
                case "Reconnect":
                    if (!_capturedPre)
                    {
                        _preSnapshot = await FetchCurrentLpAsync();
                        _capturedPre = _preSnapshot != null;
                        if (_preSnapshot != null)
                            L($"PRE : {_preSnapshot.LeaguePoints} LP ({_preSnapshot.RankDisplay})");
                    }
                    break;

                case "EndOfGame":
                case "PreEndOfGame":
                case "WaitingForStats":
                    if (_capturedPre && _preSnapshot != null)
                    {
                        _capturedPre = false;
                        await Task.Delay(TimeSpan.FromSeconds(8));
                        await CapturePostAsync();
                    }
                    break;

                case "None":
                case "Lobby":
                    _capturedPre = false;
                    _preSnapshot = null;
                    break;
            }
        }

        private async Task<LpSnapshot?> FetchCurrentLpAsync()
        {
            if (_lcuClient == null) return null;
            try
            {
                var resp = await _lcuClient.GetAsync("/lol-ranked/v1/current-ranked-stats");
                if (!resp.IsSuccessStatusCode) return null;

                var stats = JsonConvert.DeserializeObject<LcuRankedStats>(
                    await resp.Content.ReadAsStringAsync());
                var solo = stats?.queues?.FirstOrDefault(q => q.queueType == "RANKED_SOLO_5x5");
                if (solo == null || solo.tier == "NONE") return null;

                return new LpSnapshot
                {
                    Timestamp    = DateTime.UtcNow,
                    LeaguePoints = solo.leaguePoints,
                    Tier         = solo.tier,
                    Rank         = solo.division,
                };
            }
            catch (Exception ex) { L($"FetchLP exception: {ex.Message}"); return null; }
        }

        private async Task CapturePostAsync()
        {
            var post = await FetchCurrentLpAsync();
            if (post == null || _preSnapshot == null) return;

            int delta    = ComputeDelta(_preSnapshot, post);
            post.LpDelta = delta;
            post.IsWin   = delta > 0;

            L($"POST : {post.LeaguePoints} LP | delta={delta:+0;-0}");

            // Remplacer les données fictives par la première vraie donnée
            if (LpHistory.All(s => s.ChampionName == "Prévisionnel" || s.ChampionName == "Actuel"))
            {
                L("Remplacement des données prévisionnelles par données réelles.");
                LpHistory.Clear();
            }

            LpHistory.Insert(0, post);
            if (LpHistory.Count > 200) LpHistory.RemoveRange(200, LpHistory.Count - 200);

            SaveHistory();
            HistoryUpdated?.Invoke(this, EventArgs.Empty);
            _preSnapshot = null;
        }

        // ─── Calcul delta LP ──────────────────────────────────────────────────

        private static int ComputeDelta(LpSnapshot pre, LpSnapshot post)
        {
            if (pre.Tier == post.Tier && pre.Rank == post.Rank)
                return post.LeaguePoints - pre.LeaguePoints;

            int rs = (TierVal(post.Tier) - TierVal(pre.Tier)) * 400
                   + (RankVal(post.Rank) - RankVal(pre.Rank))  * 100;

            if (rs > 0) return  post.LeaguePoints + (100 - pre.LeaguePoints)  + Math.Max(0, rs - 100);
            if (rs < 0) return -(pre.LeaguePoints + (100 - post.LeaguePoints) + Math.Max(0, -rs - 100));
            return post.LeaguePoints - pre.LeaguePoints;
        }

        private static int TierVal(string t) => t?.ToUpper() switch
        {
            "IRON"=>0,"BRONZE"=>1,"SILVER"=>2,"GOLD"=>3,"PLATINUM"=>4,
            "EMERALD"=>5,"DIAMOND"=>6,"MASTER"=>7,"GRANDMASTER"=>8,"CHALLENGER"=>9,_=>-1,
        };
        private static int RankVal(string r) => r?.ToUpper() switch
        { "IV"=>0,"III"=>1,"II"=>2,"I"=>3,_=>0 };

        // ─── Persistance ──────────────────────────────────────────────────────

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(DataFile)) return;
                var loaded = JsonConvert.DeserializeObject<List<LpSnapshot>>(
                    File.ReadAllText(DataFile)) ?? new();
                // Ignorer les fichiers qui ne contiennent que des données fictives
                if (loaded.All(s => s.ChampionName == "Prévisionnel" || s.ChampionName == "Actuel"))
                { L("Fichier contient uniquement données fictives, ignoré."); return; }
                LpHistory = loaded;
                L($"Historique chargé : {LpHistory.Count} entrées.");
            }
            catch { LpHistory = new(); }
        }

        private void SaveHistory()
        {
            // Ne pas sauvegarder si tout est fictif
            if (LpHistory.All(s => s.ChampionName == "Prévisionnel" || s.ChampionName == "Actuel"))
            { L("Pas de sauvegarde : données fictives uniquement."); return; }

            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(DataFile, JsonConvert.SerializeObject(LpHistory, Formatting.Indented));
            }
            catch (Exception ex) { L($"SaveHistory : {ex.Message}"); }
        }

        // ─── Modèles LCU ─────────────────────────────────────────────────────
        private class LcuRankedStats { public List<LcuRankedQueue>? queues { get; set; } }
        private class LcuRankedQueue
        {
            public string queueType    { get; set; } = string.Empty;
            public string tier         { get; set; } = string.Empty;
            public string division     { get; set; } = string.Empty;
            public int    leaguePoints { get; set; }
        }
    }
}
