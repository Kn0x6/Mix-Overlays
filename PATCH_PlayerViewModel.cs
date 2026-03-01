// ════════════════════════════════════════════════════════════════════════════
// PATCH PlayerViewModel.cs — Ajouter RefreshStats() dans RefreshFromData()
// ════════════════════════════════════════════════════════════════════════════
//
// Dans PlayerViewModel.cs, trouve la méthode RefreshFromData().
// Elle se termine par :
//
//     OnPropertyChanged(nameof(PrimaryLane));
//     OnPropertyChanged(nameof(SecondaryLane));
//     OnPropertyChanged(nameof(HasLaneData));
//
// Ajoute juste AVANT la fermeture du try{} :
//
//     RefreshStats();   // ← stats récentes (RecentWinRate, AvgKDA, etc.)
//
// ─── Résultat final du bloc try ────────────────────────────────────────────
//
//     try
//     {
//         OnPropertyChanged(nameof(Puuid));
//         // ... toutes les autres lignes existantes ...
//         OnPropertyChanged(nameof(PrimaryLane));
//         OnPropertyChanged(nameof(SecondaryLane));
//         OnPropertyChanged(nameof(HasLaneData));
//
//         RefreshStats();  // ← LIGNE À AJOUTER ICI
//     }
//     catch (Exception ex)
//     {
//         System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.RefreshFromData] Erreur: {ex.Message}");
//     }
//
// ════════════════════════════════════════════════════════════════════════════
// OPTIONNEL : aussi dans RefreshMatchesAndPagination() :
//
//     public void RefreshMatchesAndPagination()
//     {
//         OnPropertyChanged(nameof(RecentMatches));
//         OnPropertyChanged(nameof(HasMoreMatches));
//         RefreshStats();  // ← AJOUTER ICI également
//     }
// ════════════════════════════════════════════════════════════════════════════
