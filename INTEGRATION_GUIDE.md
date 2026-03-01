# 📋 Guide d'intégration — Overlay losange in-game

## Fichiers fournis

| Fichier | Action |
|---|---|
| `DiamondPlayerCard.xaml` | **Nouveau** → `Views/DiamondPlayerCard.xaml` |
| `DiamondPlayerCard.xaml.cs` | **Nouveau** → `Views/DiamondPlayerCard.xaml.cs` |
| `OverlayWindow.xaml` | **Remplacer** l'existant |
| `OverlayWindow.xaml.cs` | **Remplacer** l'existant |
| `PlayerViewModel.DiamondCard.cs` | **Intégrer** dans `PlayerViewModel.cs` |
| `PlayerData.DiamondCard.cs` | **Intégrer** dans `PlayerData.cs` |

---

## 1. PlayerData.cs — Ajouter `LiveRuneId`

Dans la section **// Live game** de `PlayerData` :

```csharp
public int LiveRuneId { get; set; }  // ← AJOUT
```

Et dans `SpectatorParticipant` (si pas encore présent) :

```csharp
public SpectatorPerks? perks { get; set; }
```

Avec la classe :

```csharp
public class SpectatorPerks
{
    public List<int> perkIds      { get; set; } = new();
    public int       perkStyle    { get; set; }
    public int       perkSubStyle { get; set; }
}
```

---

## 2. RiotApiService.cs — Peupler `LiveRuneId`

Là où `SpectatorGameInfo` est traité (méthode `LoadFullPlayerDataAsync` ou équivalent),
après `player.LiveSpell1Id = self.spell1Id;` :

```csharp
// Rune keystone depuis les perks spectateur
if (self.perks?.perkIds?.Count > 0)
    player.LiveRuneId = self.perks.perkIds[0];
```

> **Note :** L'API Spectateur v5 retourne bien `perks.perkIds`.
> Le premier ID est toujours la rune keystone.

---

## 3. PlayerViewModel.cs — Nouvelles propriétés

Ajouter **toutes les propriétés** du fichier `PlayerViewModel.DiamondCard.cs` :

- `LiveChampionName`
- `LiveRuneId`, `HasLiveRune`
- `ChampionWinRate`, `ChampionGamesPlayed`, `ChampionWinRateDisplay`
- `CurrentChampionMasteryPoints`
- `ExpertiseLabel`, `ExpertiseBadgeBackground`, `ExpertiseBadgeForeground`

Puis dans `RefreshFromData()`, ajouter :

```csharp
OnPropertyChanged(nameof(LiveChampionName));
OnPropertyChanged(nameof(LiveRuneId));
OnPropertyChanged(nameof(HasLiveRune));
OnPropertyChanged(nameof(ChampionWinRate));
OnPropertyChanged(nameof(ChampionWinRateDisplay));
OnPropertyChanged(nameof(ChampionGamesPlayed));
OnPropertyChanged(nameof(CurrentChampionMasteryPoints));
OnPropertyChanged(nameof(ExpertiseLabel));
OnPropertyChanged(nameof(ExpertiseBadgeBackground));
OnPropertyChanged(nameof(ExpertiseBadgeForeground));
```

---

## 4. Vérifier le TeamId dans PlayerData

`DiamondPlayerCard` utilise `{Binding TeamId}` pour coloriser le contour :
- **Équipe bleue (100)** → contour `#4DC7DB`
- **Équipe rouge (200)** → contour `#FF6B6B`

Vérifier que `PlayerData.TeamId` est bien rempli depuis `SpectatorGameInfo.participants[i].teamId`.

---

## 5. Seuils d'expertise

| Label | Points de maîtrise | Parties récentes mini |
|---|---|---|
| 🩶 DÉBUTANT | < 30 000 | — |
| 💙 INTERMÉDIAIRE | 30 000 – 150 000 | ≥ 2 parties |
| 💛 EXPERT | > 150 000 | ≥ 3 parties |

> Le critère "parties récentes" évite qu'un joueur avec beaucoup de points
> anciens mais inactif sur le champion passe "Expert" par défaut.

---

## 6. Fonctionnement du toggle Ctrl+X

Le raccourci est déjà enregistré globalement dans `MainWindow.xaml.cs` :

```csharp
RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL, VK_X);
```

Quand `WM_HOTKEY` est reçu, `MainWindow` appelle `_overlay.Toggle()`.
Aucune modification nécessaire côté hotkey — il fonctionne même quand LoL est en premier plan.

---

## 7. Apparence des cartes

```
     ▲
    /|\
   / | \
  / RUNE\
 /  NOM  \
/ RANG    \
\ WINRATE /
 \ BADGE /
  \SPELLS/
   \   /
    \ /
     ▼
```

- **Fond** : splash art du champion (`ChampionNameToSplashConverter`)
- **Contour** : cyan (alliés) / rouge (ennemis), via `TeamId`
- **Cartes** : légèrement chevauchées (`Margin="-10,0"`) pour réduire la largeur totale (~600px pour 5 joueurs)
