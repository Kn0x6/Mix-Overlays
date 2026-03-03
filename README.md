# 🎮 MixOverlays

Overlay pour League of Legends — affiche les stats des joueurs en temps réel pendant la sélection de champion.

## Fonctionnalités

- **Connexion automatique** au client LoL via LCU API (détection du fichier lockfile)
- **Session Live** : stats des alliés et ennemis pendant la sélection de champion
  - Rang Solo/Duo et Flex
  - Winrate + nombre de parties
  - Top 5 masteries de champions
  - Historique des 20 dernières parties (K/D/A, CS, mode, durée)
- **Recherche hors jeu** : cherchez n'importe quel joueur par Riot ID (`Nom#TAG`)
- **Overlay in-game** : fenêtre flottante transparente affichée pendant la partie
- **Historique des parties** : affichage "face-à-face" des autres joueurs et champions
- **Thème sombre** style Porofessor

## Installation

### Prérequis
- .NET 8 SDK : https://dotnet.microsoft.com/download/dotnet/8
- Visual Studio 2022+ ou Rider
- Clé API Riot Developer : https://developer.riotgames.com

### Lancer le projet

```bash
git clone https://github.com/votre-repo/MixOverlays
cd MixOverlays
dotnet restore
dotnet run
```

### Build release

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Configuration

1. Lancez MixOverlays
2. Allez dans **Paramètres**
3. Entrez votre **clé API Riot** (format `RGAPI-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`)
4. Choisissez votre **région** (EUW1, NA1, KR, etc.)
5. Sauvegardez

> ⚠️ Les clés API de développement expirent toutes les 24h. Pour un usage permanent, demandez une Production Key sur le portail Riot.

## Architecture du projet

### Organisation par couches et responsabilités

Le projet suit le pattern **MVVM (Model-View-ViewModel)** avec une architecture en couches clairement définie :

```
MixOverlays/
├── App.xaml / App.xaml.cs              # Point d'entrée de l'application, gestion des services globaux
├── MainWindow.xaml / MainWindow.xaml.cs # Fenêtre principale (hors jeu)
├── OverlayWindow.xaml / OverlayWindow.xaml.cs # Fenêtre overlay in-game (transparente)
├── MatchDetailWindow.xaml / MatchDetailWindow.xaml.cs # Détails des parties avec affichage face-à-face
├── PlayerCard.xaml / PlayerCard.xaml.cs # Carte de stats d'un joueur (hors jeu)
├── LivePlayerCard.xaml / LivePlayerCard.xaml.cs # Carte joueur en live (in-game)
├── DarkTheme.xaml                     # Thème sombre complet (styles WPF)
├── AppSettings.cs                     # Configuration utilisateur (API key, région, etc.)
├── BaseViewModel.cs                   # Base pour les ViewModels (INotifyPropertyChanged)
├── MainViewModel.cs                   # ViewModel principal (orchestration globale)
├── PlayerViewModel.cs                 # ViewModel d'un joueur (stats, rang, maîtrise)
├── PlayerData.cs                      # Modèles de données (LCU, Riot API, Display)
├── MatchParticipantPair.cs            # Modèle pour les paires de participants
├── VersionHolder.cs                   # Gestion de la version (Data Dragon)
├── LcuService.cs                      # Connexion au client LoL (lockfile + polling)
├── RiotApiService.cs                  # Tous les appels Riot API (cache, pagination)
├── ChampionDataService.cs             # Données champions via Data Dragon
├── SettingsService.cs                 # Persistance des paramètres
├── Converters.cs                      # IValueConverter pour les bindings XAML
├── MixOverlays.csproj                 # Fichier projet
├── Mix overlays Csharp.sln            # Solution Visual Studio
├── Icone.ico / Icone.jpeg             # Icônes de l'application
├── convert_icon.py                    # Script de conversion d'icône
├── README.md                          # Documentation
├── templates/                         # Templates pour le build
│   ├── MainWindow_UiScale_patch.cs.txt
│   └── SettingsUiScale_fragment.xaml.txt
├── bin/                               # Binaires compilés
└── obj/                               # Fichiers objets de compilation
```

### Composants clés et leurs responsabilités

#### **Services (Couche métier)**
- **`LcuService.cs`** : Communication avec le client League of Legends
  - Détecte automatiquement le client via le lockfile
  - Polling toutes les 3 secondes pour surveiller l'état du jeu
  - Gère les phases : Connected, InChampSelect, InGame
  - Récupère les données de sélection de champion et les données en jeu
  - Fallback sur le port 2999 pour les données en jeu

- **`RiotApiService.cs`** : Appels aux API Riot Games
  - Gestion du cache (3 minutes) pour éviter les limites d'API
  - Appels parallèles pour optimiser les temps de chargement
  - Pagination pour l'historique des parties
  - Support des différentes versions d'API (v4/v5)

- **`ChampionDataService.cs`** : Données statiques des champions
  - Télécharge les données depuis Data Dragon
  - Cache en mémoire pour éviter les appels répétés
  - Fournit les noms de champions et les icônes

- **`SettingsService.cs`** : Persistance des paramètres utilisateur
  - Sauvegarde/restaure la configuration (API key, région, etc.)
  - Gestion des chemins d'installation du jeu

#### **Modèles de données (Couche données)**
- **`PlayerData.cs`** : Modèles de données complexes
  - `LcuChampSelectSession` : Données de sélection de champion
  - `RiotSummoner` : Informations summoner Riot API
  - `LeagueEntry` : Informations de classement
  - `MatchSummary` : Résumé d'une partie avec pagination
  - `MatchParticipantSummary` : Détails d'un participant
  - `SpectatorGameInfo` : Informations spectateur (in-game)

#### **ViewModels (Couche présentation)**
- **`MainViewModel.cs`** : Cerveau de l'application
  - Gère l'état global de l'application
  - Coordonne les services et les vues
  - Gère les commandes principales (recherche, paramètres, etc.)
  - Partiellement divisé en fichiers séparés pour la logique in-game et hors jeu

- **`PlayerViewModel.cs`** : ViewModel d'un joueur spécifique
  - Affiche les stats, rang, maîtrise, historique
  - Gère le timer live pendant les parties
  - Calcule les lanes principales et secondaires
  - Gère le chargement paginé des parties

#### **Vues (Couche UI)**
- **`MainWindow.xaml`** : Interface principale hors jeu
  - Recherche de joueur par Riot ID
  - Affichage des stats du joueur sélectionné
  - Historique des parties avec pagination
  - Accès aux paramètres

- **`OverlayWindow.xaml`** : Fenêtre overlay in-game
  - Fenêtre transparente avec `AllowsTransparency=True`
  - Affichage des stats des alliés et ennemis pendant la sélection
  - Mise à jour en temps réel via LCU

- **`MatchDetailWindow.xaml`** : Détails des parties
  - Affichage "face-à-face" des participants
  - Organisé par lane (TOP, JUNGLE, MID, ADC, SUPPORT)
  - Comparaison équipe vs équipe

- **`PlayerCard.xaml`** : Carte de joueur compacte
  - Affiche rang, maîtrise, K/D/A, etc.
  - Utilisée dans les listes et l'overlay

#### **Utilitaires**
- **`BaseViewModel.cs`** : Base pour tous les ViewModels
  - Implémente `INotifyPropertyChanged`
  - Fournit des méthodes utilitaires pour les bindings

- **`Converters.cs`** : Convertisseurs WPF
  - Convertit les données brutes en format affichable
  - Gère les icônes, les couleurs, les formats de texte

- **`AppSettings.cs`** : Configuration utilisateur
  - Stocke la clé API, la région, les préférences
  - Validée par le `SettingsService`

### Relations entre composants

```
Client LoL (LCU)
    │
    ▼
LcuService (polling /lol-gameflow/v1/gameflow-phase)
    │ ChampSelectSessionUpdated event
    ▼
MainViewModel.LoadChampSelectDataAsync()
    │ Pour chaque joueur : PUUID → Riot API
    ▼
RiotApiService
    ├── GetSummonerByPuuidAsync()     → summonerId
    ├── GetLeagueEntriesByPuuidAsync() → rang
    ├── GetTopMasteriesByPuuidAsync()  → masteries
    └── GetMatchIdsByPuuidAsync()      → historique
    │
    ▼
PlayerViewModel → PlayerCard (UI)
```

### Patterns de conception utilisés

- **MVVM** : Séparation claire entre la logique métier (ViewModel) et l'interface (View)
- **Service Locator** : Services globaux accessibles via `App.ServiceName`
- **Cache** : Cache HTTP avec TTL pour éviter les appels répétés aux API
- **Pagination** : Chargement progressif des données (10 parties à la fois)
- **Fallback** : Plusieurs sources de données pour la robustesse (LCU, port 2999, API Riot)
- **Events** : Communication entre composants via des événements dédiés

### Flux de données typique

1. **Détection du client** : `LcuService` détecte le lockfile et se connecte
2. **Surveillance de l'état** : Polling toutes les 3 secondes pour détecter les changements
3. **Chargement des données** : Lors de la sélection de champion, récupération des PUUIDs
4. **Appels API parallèles** : `RiotApiService` charge summoner, rang, maîtrise, historique
5. **Affichage** : `PlayerViewModel` met à jour l'UI via les bindings WPF
6. **Mise à jour live** : Pendant la partie, mise à jour des timers et des stats en temps réel

## Flux de données

```
Client LoL
    │
    ▼
LcuService (polling /lol-gameflow/v1/gameflow-phase)
    │ ChampSelectSessionUpdated event
    ▼
MainViewModel.LoadChampSelectDataAsync()
    │ Pour chaque joueur : PUUID → Riot API
    ▼
RiotApiService
    ├── GetSummonerByPuuidAsync()     → summonerId
    ├── GetLeagueEntriesByPuuidAsync() → rang
    ├── GetTopMasteriesByPuuidAsync()  → masteries
    └── GetMatchIdsByPuuidAsync()      → historique
    │
    ▼
PlayerViewModel → PlayerCard (UI)
```

## Nouveautés : Affichage "face-à-face" de l'historique

L'historique des parties peut maintenant se déplier pour afficher les autres joueurs et champions de la partie dans un format "face-à-face" :

- **Compact par défaut** : Affichage V/D, champion, K/D/A, sorts, KDA, durée
- **Déployé au clic** : Affichage des 9 autres joueurs organisés par lane (TOP, JUNGLE, MID, ADC, SUPPORT)
- **Organisation intuitive** : Votre équipe vs Équipe ennemie, par position/lane
- **Comparaison facile** : K/D/A, champions, sorts d'invocateur côte à côte

## Notes de développement

- L'API LCU est sondée toutes les **3 secondes**
- Les certificats auto-signés du client LoL sont acceptés (`ServerCertificateCustomValidationCallback`)
- Data Dragon est utilisé pour les icônes de champions et de profil
- L'overlay in-game est une fenêtre `AllowsTransparency=True` avec `Topmost=True`

 
