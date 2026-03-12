# 🔍 Analyse complète du code — MixOverlays

> Revue exhaustive de chaque fichier. Classement par sévérité :  
> 🔴 Bug / cassure garantie · 🟠 Code fantôme / inutile · 🟡 Fragilité / dette technique · 🟢 Optimisation possible

---

## 1. `MainWindow.xaml.cs` — 🔴 BUG CRITIQUE (double hotkey)

### Problème
Le fichier contient **deux systèmes de hotkey actifs simultanément** :

| Système | Mécanisme | Déclenche |
|---|---|---|
| **Ancien** | `RegisterHotKey` + `WndProc` (via `MainWindow_SourceInitialized`) | `ToggleOverlay()` |
| **Nouveau** | `GlobalHotkeyService` (via `InitGlobalHotkey()`) | `ToggleOverlay()` |

Résultat : chaque pression de Ctrl+X déclenche `ToggleOverlay()` **deux fois** → l'overlay s'ouvre et se referme immédiatement.

### Ce qu'il faut supprimer de `MainWindow.xaml.cs`

```csharp
// ❌ 1 — Constantes Win32 (obsolètes)
private const int  HOTKEY_ID   = 9000;
private const uint MOD_CONTROL = 0x0002;
private const uint VK_X        = 0x58;

// ❌ 2 — P/Invokes (obsolètes)
[DllImport("user32.dll")] private static extern bool RegisterHotKey(...);
[DllImport("user32.dll")] private static extern bool UnregisterHotKey(...);

// ❌ 3 — Champ inutilisé
private HwndSource? _hwndSource;

// ❌ 4 — Méthode entière à supprimer
private void MainWindow_SourceInitialized(object? sender, EventArgs e)
{
    var handle = new WindowInteropHelper(this).Handle;
    _hwndSource = HwndSource.FromHwnd(handle);
    _hwndSource?.AddHook(WndProc);
    RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL, VK_X);
}

// ❌ 5 — Méthode entière à supprimer
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    const int WM_HOTKEY = 0x0312;
    if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
    {
        ToggleOverlay();
        handled = true;
    }
    return IntPtr.Zero;
}

// ❌ 6 — Abonnement à supprimer du constructeur
SourceInitialized += MainWindow_SourceInitialized;
```

### Ce qu'il faut garder (via `MainWindow_Hotkey_Fix.cs`)

```csharp
// ✅ Constructeur : appeler InitGlobalHotkey() uniquement
// ✅ MainWindow_Closed : appeler DisposeGlobalHotkey() uniquement
// ✅ InitGlobalHotkey, DisposeGlobalHotkey, ToggleOverlay — déjà corrects
```

---

## 2. `MainWindow_Hotkey_Fix.cs` — 🟠 Fichier de patch à fusionner

### Problème
Ce fichier existe comme "fiche d'instructions" avec des commentaires procéduraux (`// 1. SUPPRIMER…`, `// 2. AJOUTER…`). Une fois les modifications appliquées à `MainWindow.xaml.cs`, ce fichier doit être **supprimé du projet**. Son contenu (`InitGlobalHotkey`, `DisposeGlobalHotkey`, `ToggleOverlay`) doit vivre directement dans `MainWindow.xaml.cs`.

---

## 3. `OverlayWindow.xaml.cs` — 🟠 Méthode orpheline `Toggle()`

### Problème
La méthode `Toggle()` est définie mais **jamais appelée** :

```csharp
// Définie dans OverlayWindow.xaml.cs — jamais utilisée
public void Toggle()
{
    if (IsVisible) Hide();
    else { Show(); ForceTopmost(); }
}
```

`ToggleOverlay()` dans `MainWindow_Hotkey_Fix.cs` gère la logique d'ouverture/fermeture directement via `App.OverlayWindow.Show()` / `.Hide()`. La méthode `Toggle()` est donc redondante.

### Correction
Soit **supprimer `Toggle()`**, soit l'utiliser dans `ToggleOverlay()` :

```csharp
// Option A — supprimer Toggle() et garder la logique dans ToggleOverlay() (état actuel)

// Option B — utiliser Toggle() pour simplifier ToggleOverlay()
private void ToggleOverlay()
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        if (App.OverlayWindow == null)
            App.OverlayWindow = new OverlayWindow();

        App.OverlayWindow.SetTeamData(_vm.AllyTeam, _vm.EnemyTeam);

        if (App.OverlayWindow.IsVisible)
            App.OverlayWindow.Hide();
        else if (_vm.ClientState == LcuState.InGame)
            App.OverlayWindow.Toggle(); // ← appelle ForceTopmost automatiquement
    });
}
```

---

## 4. `MainViewModel.cs` — 🟠 Classe fantôme `LiveGameDetailViewModel`

### Problème
La classe `LiveGameDetailViewModel` est définie dans `MainViewModel.cs` mais **n'est jamais instanciée ni utilisée**. Les équipes en live sont gérées directement par `AllyTeam` / `EnemyTeam` dans `MainViewModel.InGame.cs`.

```csharp
// ❌ À supprimer — jamais utilisée
public class LiveGameDetailViewModel : BaseViewModel
{
    public bool IsLoading { ... }
    public ObservableCollection<PlayerViewModel> AllyTeam { ... }
    public ObservableCollection<PlayerViewModel> EnemyTeam { ... }
}
```

---

## 5. `MainViewModel.cs` — 🟠 Méthode vide `RefreshLpChart()`

### Problème
`RefreshLpChart()` est appelée dans deux endroits (`HistoryUpdated` handler et dans le constructeur) mais ne fait **absolument rien d'utile** — seulement un `App.Log` :

```csharp
private void RefreshLpChart()
{
    App.Log($"[VM] RefreshLpChart — MyAccount=..., HasLpData=..., Snapshots=...");
    // ← PAS de logique de graphique !
}
```

Si le graphique LP est géré par le binding XAML sur `LpSnapshots`, cette méthode est inutile. Si elle doit déclencher une mise à jour du graphique, elle est incomplète.

### Correction
- Si le graphique se met à jour via binding : **supprimer `RefreshLpChart()`** et ses deux appels.
- Si le graphique nécessite un refresh manuel : implémenter la logique manquante.

---

## 6. `MainViewModel.InGame.cs` — 🟡 Logs de debug en production

### Problème
`LoadAndRefreshPlayerAsync` contient une quantité importante de `System.Diagnostics.Debug.WriteLine` qui témoignent d'une phase de débogage non nettoyée :

```csharp
System.Diagnostics.Debug.WriteLine($"{tag} ══ DÉBUT LoadAndRefreshPlayer ══");
System.Diagnostics.Debug.WriteLine($"{tag} PUUID entrant = '{pd.Puuid?.Substring(0, 8)}...'");
System.Diagnostics.Debug.WriteLine($"{tag} ApiKey présente = ...");
// ... ~15 lignes de debug supplémentaires
```

`App.Log()` existe pour les logs persistants. Ces `Debug.WriteLine` sont ignorés en Release mais polluent le canal de debug en développement.

### Correction
Remplacer par `App.Log()` pour les informations structurelles, et **supprimer entièrement** les lignes de pure vérification :

```csharp
// ✅ Garder (niveau INFO)
App.Log($"[InGame|Rank] Chargement {pd.GameName}#{pd.TagLine}");
App.Log($"[InGame|Rank] SoloRank={fullData.SoloRank?.tier ?? "NULL"}");

// ❌ Supprimer (bruit de débogage)
System.Diagnostics.Debug.WriteLine($"{tag} ApiKey présente = ...");
System.Diagnostics.Debug.WriteLine($"{tag} Collection.Count={collCount}...");
// etc.
```

---

## 7. `RiotApiService.cs` — 🟡 Logs de debug en production

### Problème
`GetAsync<T>` contient des commentaires `// ─── DEBUG TEMPORAIRE ───` avec des `Debug.WriteLine` qui n'ont pas été retirés :

```csharp
// ─── DEBUG TEMPORAIRE ───
System.Diagnostics.Debug.WriteLine($"[HTTP] GET {url}");
System.Diagnostics.Debug.WriteLine($"[HTTP] X-Riot-Token present=...");
// ...
System.Diagnostics.Debug.WriteLine($"[HTTP] {(int)resp.StatusCode} → {url}");
System.Diagnostics.Debug.WriteLine($"[HTTP] Response body: {body}");
// ─── FIN DEBUG ───
```

### Correction
Supprimer le bloc entier entre `// ─── DEBUG TEMPORAIRE ───` et `// ─── FIN DEBUG ───`.  
Remplacer uniquement par un log d'erreur structurel :

```csharp
if (!resp.IsSuccessStatusCode)
{
    var segment = new Uri(url).AbsolutePath.Split('/')
        .LastOrDefault(s => !string.IsNullOrEmpty(s) && s.Length < 30) ?? url;
    LastHttpError = $"HTTP {(int)resp.StatusCode} {resp.StatusCode} sur /{segment}";
    App.Log($"[RiotAPI] {LastHttpError}");

    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    { /* backoff existant */ }

    return default;
}
```

---

## 8. `RiotApiService.cs` — 🟡 `HttpClient` non disposé

### Problème
`RiotApiService` crée un `HttpClient` dans son constructeur mais n'implémente pas `IDisposable`. Si le service est recréé (changement de clé API, etc.), le `HttpClient` précédent fuit.

```csharp
// Actuellement
public class RiotApiService  // ← pas de IDisposable
{
    private readonly HttpClient _client; // ← jamais disposé
```

### Correction

```csharp
public class RiotApiService : IDisposable
{
    private readonly HttpClient _client;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.Dispose();
        _rateLimitGate.Dispose();
    }
}
```

---

## 9. `RiotApiService.cs` — 🟢 Cache sans limite de taille

### Problème
`_cache` (ConcurrentDictionary) peut grossir indéfiniment. Sur une longue session avec de nombreuses recherches de joueurs différents, la mémoire n'est jamais libérée (seule l'expiration TTL est vérifiée à la lecture).

### Correction
Ajouter un nettoyage périodique ou une limite :

```csharp
// Nettoyage des entrées expirées — appeler de temps en temps
private void PurgeExpiredCache()
{
    var now = DateTime.UtcNow;
    foreach (var key in _cache.Keys.ToList())
        if (_cache.TryGetValue(key, out var v) && v.Expires <= now)
            _cache.TryRemove(key, out _);
}
```

Ou limiter à 200 entrées max avec un ConcurrentDictionary + compteur.

---

## 10. `GlobalHotkeyService.cs` — 🟠 Import inutilisé

```csharp
using System.Windows.Input; // ❌ — aucun type de cet espace n'est utilisé dans ce fichier
```

---

## 11. `MainViewModel.cs` — 🟡 `Task.Delay(1500)` fragile dans le constructeur

### Problème
```csharp
_ = Task.Run(async () =>
{
    await Task.Delay(1500); // "laisser le temps à LCU de tenter la connexion"
    Application.Current.Dispatcher.Invoke(() =>
    {
        if (MyAccount == null && _clientState == LcuState.Disconnected)
            _ = LoadMyAccountFromCacheAsync();
    });
});
```

Ce délai arbitraire de 1,5 seconde est fragile : sur une machine lente, LCU peut ne pas encore avoir répondu ; sur une machine rapide, c'est 1,5s perdues au démarrage.

### Correction
Écouter un événement plutôt que d'attendre arbitrairement. Le bon endroit est dans `OnLcuStateChanged` — qui est déjà partiellement correct mais manque le cas de démarrage "jamais connecté" :

```csharp
// Dans OnLcuStateChanged — déjà présent, renforcer avec un flag de démarrage
private bool _initialCacheLoadAttempted = false;

// Dans OnLcuStateChanged :
if (e.State == LcuState.Disconnected && MyAccount == null && !_initialCacheLoadAttempted)
{
    _initialCacheLoadAttempted = true;
    _ = LoadMyAccountFromCacheAsync();
}
```

Et supprimer le bloc `Task.Delay(1500)` du constructeur.

---

## 12. `MainWindow.xaml.cs` — 🟡 `Task.Delay(50)` fragile dans `MatchParticipant_Click`

```csharp
_ = Task.Run(async () =>
{
    await Task.Delay(50); // ← attend que SearchInput soit mis à jour
    Application.Current.Dispatcher.Invoke(() =>
    {
        if (_vm.SearchPlayerCommand.CanExecute(null))
            _vm.SearchPlayerCommand.Execute(null);
    });
});
```

### Correction
Directement exécuter la commande sur le dispatcher après avoir mis à jour `SearchInput`, sans délai :

```csharp
Application.Current.Dispatcher.Invoke(() =>
{
    _vm.SearchInput = $"{p.GameName}#{p.TagLine}";
    if (_vm.SearchPlayerCommand.CanExecute(null))
        _vm.SearchPlayerCommand.Execute(null);
});
```

---

## 13. `ChampionDataService.cs` — 🟡 Race condition au démarrage

### Problème
Dans `RiotApiService` :
```csharp
_champions = champions ?? ChampionDataService.Instance ?? new ChampionDataService();
```

Si `RiotApiService` est instancié avant que `ChampionDataService.EnsureLoadedAsync()` ait assigné `Instance`, un nouveau `ChampionDataService` vide est créé. Cela peut arriver si l'ordre d'initialisation dans le constructeur de `MainViewModel` change.

### Correction
Dans `MainViewModel`, garantir l'ordre d'initialisation :

```csharp
// ✅ S'assurer que _champions est initialisé ET passé explicitement à RiotApiService
private readonly ChampionDataService _champions = new();
// ...
_riot = App.RiotApiService ?? new RiotApiService(_settings, _champions); // ← passer explicitement
```

Et dans `ChampionDataService.EnsureLoadedAsync()`, assigner `Instance` en début (pas en fin) pour que les appels concurrents y accèdent :

```csharp
public async Task EnsureLoadedAsync()
{
    if (_loaded) return;
    Instance = this; // ← déplacer ici pour être accessible dès le début
    // ...
}
```

---

## 14. `LcuService.cs` — 🟢 Polling inutile en phase InGame

### Problème
`PollTick` tourne toutes les 3 secondes et appelle `PollGameflow()` même pendant la partie. Une fois `_inGameDataLoaded = true`, `PollGameflow()` ne fait rien d'utile en phase `InProgress` — mais consomme un aller-retour HTTP vers LCU toutes les 3s.

### Correction
Augmenter l'intervalle de polling en InGame (passe de 3s à 10s par exemple) :

```csharp
// Dans PollGameflow, après avoir chargé les données InGame
if (!phaseChanged && phase is "InProgress" or "Reconnect" && _inGameDataLoaded)
    return; // rien à faire, éviter un poll HTTP inutile

// Ou : adapter le timer selon la phase
```

---

## 15. `PlayerViewModel.cs` — 🟢 `RefreshMatchesAndPagination()` verbeux

### Problème
La méthode appelle `OnPropertyChanged` sur **15 propriétés distinctes** une par une. Si une mise à jour batch est effectuée, WPF reçoit 15 notifications successives.

### Correction
WPF supporte un refresh global avec `string.Empty` ou `null` :

```csharp
// Déclenche la réévaluation de toutes les propriétés bindées
OnPropertyChanged(string.Empty);
```

Cela simplifie `RefreshMatchesAndPagination()`, `RefreshRankAndStatus()` et `RefreshFromData()` en un seul appel si la granularité fine n'est pas nécessaire.

---

## 16. `SettingsService.cs` — 🟢 `_champions` non-readonly dans `RiotApiService`

```csharp
private ChampionDataService _champions; // ← devrait être readonly
```

`_champions` n'est jamais réassigné après le constructeur. Ajouter `readonly` documente l'intention et protège contre une réassignation accidentelle.

---

## Synthèse — Plan d'action priorisé

| Priorité | Fichier | Action |
|---|---|---|
| 🔴 1 | `MainWindow.xaml.cs` | Supprimer ancien système RegisterHotKey (constantes, DllImport, `_hwndSource`, `MainWindow_SourceInitialized`, `WndProc`) |
| 🔴 2 | `MainWindow_Hotkey_Fix.cs` | Fusionner dans `MainWindow.xaml.cs` puis **supprimer** le fichier |
| 🟠 3 | `OverlayWindow.xaml.cs` | Supprimer `Toggle()` ou la brancher dans `ToggleOverlay()` |
| 🟠 4 | `MainViewModel.cs` | Supprimer `LiveGameDetailViewModel` |
| 🟠 5 | `MainViewModel.cs` | Supprimer ou implémenter `RefreshLpChart()` |
| 🟠 6 | `GlobalHotkeyService.cs` | Supprimer `using System.Windows.Input` |
| 🟡 7 | `MainViewModel.InGame.cs` | Remplacer les `Debug.WriteLine` par `App.Log` ou supprimer |
| 🟡 8 | `RiotApiService.cs` | Supprimer le bloc `// DEBUG TEMPORAIRE` |
| 🟡 9 | `RiotApiService.cs` | Implémenter `IDisposable` |
| 🟡 10 | `MainViewModel.cs` | Remplacer `Task.Delay(1500)` par écoute d'événement |
| 🟡 11 | `MainWindow.xaml.cs` | Supprimer `Task.Delay(50)` dans `MatchParticipant_Click` |
| 🟡 12 | `ChampionDataService.cs` | Assigner `Instance` en début de `EnsureLoadedAsync` |
| 🟢 13 | `RiotApiService.cs` | Ajouter un nettoyage du cache expiré |
| 🟢 14 | `LcuService.cs` | Réduire le polling en phase InGame |
| 🟢 15 | `PlayerViewModel.cs` | Simplifier `RefreshMatchesAndPagination()` avec `OnPropertyChanged(string.Empty)` |
| 🟢 16 | `RiotApiService.cs` | Passer `_champions` en `readonly` |

---

## Ce qui est bien fait ✅

- **Token bucket** dans `RiotApiService` — gestion propre du rate limiting Riot API avec fenêtre glissante 2 min
- **Appels parallèles** dans `LoadFullPlayerDataAsync` — `Task.WhenAll` bien utilisé
- **Architecture partial class** pour `MainViewModel` — séparation claire InGame / OutOfGame
- **`GlobalHotkeyService`** — hook bas-niveau WH_KEYBOARD_LL correct, fonctionne en plein écran LoL
- **`LcuService`** — merge port 2999 + LCU session propre, fallback bien géré
- **`VersionHolder`** — singleton partagé entre les converters XAML, évite les dépendances cycliques
- **Cache HTTP avec TTL** — `ConcurrentDictionary` thread-safe, évite les appels répétés
- **`ShowActivated = false`** sur l'overlay — ne vole pas le focus à LoL
- **`SetWindowPos` P/Invoke** — force l'overlay au-dessus en borderless windowed sans activer la fenêtre
- **Pagination des matchs** — `MatchIdBuffer` + `MatchesOffset` propre et extensible
- **`LpTrackerService`** — seed depuis l'historique Riot + persistance JSON bien structurés
