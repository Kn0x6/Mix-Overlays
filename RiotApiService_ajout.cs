// ═══════════════════════════════════════════════════════════════════
//  CORRECTIF 1 — RiotApiService.cs
//  Ajouter cette méthode juste après LoadFullPlayerDataAsync
// ═══════════════════════════════════════════════════════════════════
//
//  Copie ce bloc dans RiotApiService.cs, après la méthode
//  LoadFullPlayerDataAsync existante.
// ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Charge UNIQUEMENT le summoner + le rang d'un joueur (2 appels API).
        /// Utilisé pendant le champ select pour afficher le rang rapidement,
        /// sans bloquer sur les masteries et l'historique de parties.
        /// </summary>
        public async Task<(RiotSummoner? summoner, List<LeagueEntry>? entries)> LoadRankOnlyAsync(string puuid)
        {
            var summonerTask = GetSummonerByPuuidAsync(puuid);
            var rankTask     = GetLeagueEntriesByPuuidAsync(puuid);
            await Task.WhenAll(summonerTask, rankTask);
            return (summonerTask.Result, rankTask.Result);
        }
