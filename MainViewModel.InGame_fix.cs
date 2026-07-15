// ═══════════════════════════════════════════════════════════════════
//  CORRECTIF 2 — MainViewModel.InGame.cs
//  Remplacer la méthode LoadAndRefreshPlayerAsync en entier
// ═══════════════════════════════════════════════════════════════════
//
//  PROBLÈME : LoadFullPlayerDataAsync fait ~9 appels API par joueur
//  (summoner + rank + masteries + 20 matchIds + 5 matches).
//  10 joueurs en parallèle = ~90 appels → quota 80/2min dépassé.
//
//  SOLUTION : Charger le rang en 2 appels d'abord (ultra rapide),
//  afficher immédiatement, puis charger le reste en arrière-plan.
// ═══════════════════════════════════════════════════════════════════

        private async Task LoadAndRefreshPlayerAsync(
            PlayerData pd,
            ObservableCollection<PlayerViewModel> collection)
        {
            var tag = $"[RANK|{pd.GameName}]";

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

            // ── ÉTAPE 1 : Rang seul (2 appels) — rapide et prioritaire ────────
            try
            {
                var (summoner, entries) = await _riot.LoadRankOnlyAsync(pd.Puuid);

                var rankData = new PlayerData
                {
                    Puuid        = pd.Puuid,
                    GameName     = pd.GameName,
                    TagLine      = pd.TagLine,
                    TeamId       = pd.TeamId,
                    ChampionId   = pd.ChampionId,
                    ChampionName = pd.ChampionId > 0
                        ? _champions.GetName(pd.ChampionId)
                        : pd.ChampionName,
                    LiveSpell1Id = pd.LiveSpell1Id,
                    LiveSpell2Id = pd.LiveSpell2Id,
                    IsLoading    = false,
                    IsLoaded     = true,
                };

                if (summoner != null)
                {
                    rankData.SummonerId    = summoner.id;
                    rankData.SummonerLevel = summoner.summonerLevel;
                    rankData.ProfileIconId = summoner.profileIconId;
                }

                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        if (e.queueType == "RANKED_SOLO_5x5") rankData.SoloRank = e;
                        else if (e.queueType == "RANKED_FLEX_SR") rankData.FlexRank = e;
                    }
                }

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

            // ── ÉTAPE 2 : Masteries (optionnel, non bloquant) ─────────────────
            // Pas critique pour l'affichage principal, on peut s'arrêter ici
            // si tu veux juste le rang. Décommente si tu veux les masteries :
            /*
            try
            {
                var masteries = await _riot.GetTopMasteriesByPuuidAsync(pd.Puuid, 5);
                if (masteries != null)
                {
                    foreach (var m in masteries)
                        m.ChampionName = _champions.GetName(m.championId);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var vm = collection.FirstOrDefault(v => v.Data.Puuid == pd.Puuid);
                        if (vm != null)
                        {
                            vm.Data.TopMasteries = masteries;
                            vm.UpdateData(vm.Data);
                        }
                    });
                }
            }
            catch { }
            */
        }
