# Movie Graph VR

Graphe 3D interactif **Films ↔ Acteurs** en réalité virtuelle (Meta Quest), alimenté par l'API TMDB.

- Menu de départ avec les films et personnalités tendance de la semaine
- Ambiance « constellation », liens dorés, sons et vibrations
- Tutoriel guidé, badges `+N`, cartes « fin du chemin »
- Bandes-annonces lues sur un grand écran dans la scène (via le navigateur intégré UnityWebBrowser)

## Prérequis

- Unity **6000.1.14f1**
- Meta Quest + Quest Link (ou Air Link) sur PC Windows
- Une clé API TMDB gratuite : https://www.themoviedb.org/settings/api

## Installation

1. Cloner le dépôt et l'ouvrir avec Unity 6000.1.14f1 (les packages, dont UnityWebBrowser, se téléchargent au premier lancement).
2. Créer le fichier **`Assets/StreamingAssets/tmdb_key.txt`** contenant uniquement votre clé TMDB
   (clé API v3 ou jeton d'accès v4). Ce fichier est ignoré par git : ne jamais versionner la clé.
3. Ouvrir la scène `Assets/Scenes/SampleScene.unity`, lancer Quest Link, puis **Play**.

## Commandes

| Action | Casque | Éditeur |
|---|---|---|
| Ouvrir / replier une carte | Gâchette droite | Clic gauche |
| Attraper une carte | Grip droit | Clic droit maintenu |
| Bande-annonce du film survolé | X | T |
| Pause / lecture (sur l'écran) | Gâchette droite | Clic |
| Revenir en arrière | A | Z |
| Tout fermer (encore : menu) / fermer l'écran | B | Retour arrière |
| Tourner / zoomer | Joystick droit | Flèches / molette |
| Recentrer | Y | R |

## Code

Tout le code du projet est dans `Assets/MovieGraph/` :

- `MovieGraphManager` : graphe, menu, navigation, disposition 3D
- `MovieGraphNode`, `MovieGraphEdge` : cartes et liens
- `MovieGraphTrailer` : écran de bande-annonce (navigateur intégré + page locale `http://localhost:8787`)
- `MovieGraphTutorial`, `MovieGraphFeedback`, `MovieGraphEnvironment`, `MovieGraphVisuals`
- `TmdbClient` : appels à l'API TMDB

Ce produit utilise l'API TMDB mais n'est ni approuvé ni certifié par TMDB.
