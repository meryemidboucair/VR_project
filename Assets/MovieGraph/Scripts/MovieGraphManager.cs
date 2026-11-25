using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MovieGraph
{
    /// <summary>
    /// Graphe Films ↔ Acteurs alimenté par TMDB, ambiance "constellation".
    ///
    ///  Menu de départ : carrousel des films et personnalités tendance → on choisit par où commencer.
    ///  Gâchette        : ouvrir / replier une carte (badge "+N" = ce qu'il y a derrière, grisé = fin du chemin)
    ///  A               : revenir en arrière (annule la dernière ouverture)
    ///  B               : tout fermer  (B encore : retour au menu)
    ///  Joystick droit  : tourner (gauche/droite) et zoomer (haut/bas)
    ///  Y               : recentrer devant soi
    ///  Grip            : attraper une carte
    ///
    ///  Éditeur : clic gauche / clic droit maintenu / Z / Retour arrière / flèches + molette / R
    /// </summary>
    public class MovieGraphManager : MonoBehaviour
    {
        [Header("TMDB")]
        [Tooltip("Clé API v3 ou Read Access Token v4 (themoviedb.org > Paramètres > API)")]
        public string tmdbApiKey = "";
        public string language = "fr-FR";

        [Header("Départ")]
        [Tooltip("Affiche le carrousel des tendances au lancement (sinon démarre directement sur Start Query)")]
        public bool showStartMenu = true;
        [Range(2, 10)] public int menuMovies = 7;
        [Range(2, 10)] public int menuPeople = 7;
        public string startQuery = "Inception";
        public bool startIsPerson = false;
        public bool buildOnStart = true;
        [Tooltip("Place le graphe devant la caméra au lancement (casque ou éditeur)")]
        public bool placeInFrontOfCamera = true;
        public float distanceFromCamera = 1.3f;

        [Header("Expansion")]
        [Range(2, 20)] public int maxNeighbors = 8;
        [Tooltip("Ignore les films trop confidentiels (nombre de votes TMDB minimum)")]
        public int minVoteCount = 200;
        public int maxNodes = 150;
        [Tooltip("Délai minimum entre deux clics (évite les double-clics involontaires)")]
        public float clickCooldown = 0.4f;

        [Header("Navigation (joystick droit)")]
        public float rotateSpeed = 70f;
        public float zoomSpeed = 1.2f;
        public float minZoom = 0.35f;
        public float maxZoom = 3f;

        [Header("Apparence (mètres)")]
        public float nodeSize = 0.12f;
        public string imageSize = "w185";

        [Header("Layout")]
        public float springLength = 0.45f;
        public float springStrength = 3f;
        public float repulsion = 0.03f;
        public float centerPull = 0.4f;
        [Range(0.5f, 0.99f)] public float damping = 0.85f;
        public float maxSpeed = 1.5f;

        // ---- Événements (utilisés par le tutoriel)
        public event Action MenuShown;
        public event Action<MovieGraphNode> Started;
        public event Action<MovieGraphNode> Expanded;
        public event Action<float> Navigated;
        public event Action WentBack;
        public event Action ClosedAll;

        public MovieGraphNode Root => root;
        public IReadOnlyCollection<MovieGraphNode> Nodes => nodes.Values;

        TmdbClient client;
        readonly Dictionary<string, MovieGraphNode> nodes = new Dictionary<string, MovieGraphNode>();
        readonly List<MovieGraphEdge> edges = new List<MovieGraphEdge>();
        readonly List<MovieGraphNode> history = new List<MovieGraphNode>();
        readonly List<MovieGraphNode> menuNodes = new List<MovieGraphNode>();

        Transform content;
        MovieGraphNode root;
        TextMeshPro infoPanel;
        TextMeshPro menuTitle;
        MovieGraphNode hovered;
        float lastClickTime = -10f;
        float zoom = 1f;
        bool menuActive;
        int generation;        // incrémenté à chaque "tout fermer" : annule les ouvertures en cours
        MovieGraphTrailer trailer;

        MovieGraphFeedback Fx => MovieGraphFeedback.Instance;

        void OnDestroy() => MovieGraphVisuals.RemoveFontFallbacks();

        const string KeyFileName = "tmdb_key.txt";

        static string ReadKeyFile()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, KeyFileName);
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MovieGraph] Lecture de {KeyFileName} impossible : {e.Message}");
                return null;
            }
        }

        void Awake()
        {
            MovieGraphVisuals.EnsureFontFallbacks();
            if (GetComponent<MovieGraphFeedback>() == null) gameObject.AddComponent<MovieGraphFeedback>();
            if (GetComponent<MovieGraphTutorial>() == null) gameObject.AddComponent<MovieGraphTutorial>().graph = this;
            if (FindFirstObjectByType<MovieGraphEnvironment>() == null)
                new GameObject("MovieGraphEnvironment").AddComponent<MovieGraphEnvironment>();
        }

        void Start()
        {
            if (buildOnStart) Build();
        }

        [ContextMenu("Construire / Recommencer")]
        public void Build()
        {
            // La clé n'est pas versionnée : elle est lue dans Assets/StreamingAssets/tmdb_key.txt (ignoré par git)
            if (string.IsNullOrWhiteSpace(tmdbApiKey)) tmdbApiKey = ReadKeyFile();
            if (string.IsNullOrWhiteSpace(tmdbApiKey))
            {
                Debug.LogError("[MovieGraph] Clé TMDB introuvable : crée le fichier Assets/StreamingAssets/" + KeyFileName +
                               " contenant ta clé (ou renseigne MovieGraphManager > Tmdb Api Key).");
                return;
            }

            Clear();
            client = new TmdbClient(tmdbApiKey, language);

            content = new GameObject("GraphContent").transform;
            content.SetParent(transform, false);
            CreateInfoPanel();

            StartCoroutine(BuildRoutine());
        }

        IEnumerator BuildRoutine()
        {
            if (placeInFrontOfCamera)
            {
                // Attend que la tête soit réellement suivie (casque porté) avant de placer le graphe.
                float t0 = Time.time;
                while (OVRManager.isHmdPresent && !HeadTracked() && Time.time - t0 < 10f)
                    yield return null;
                yield return null;
                Recenter();
            }

            if (showStartMenu) yield return ShowMenuRoutine();
            else yield return StartFromQueryRoutine();
        }

        public void Clear()
        {
            StopAllCoroutines();
            nodes.Clear();
            edges.Clear();
            history.Clear();
            menuNodes.Clear();
            if (trailer != null) { trailer.Close(); trailer = null; }
            hovered = null;
            root = null;
            menuActive = false;
            var old = transform.Find("GraphContent");
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }
        }

        // ================================================================== Menu de départ

        IEnumerator ShowMenuRoutine()
        {
            menuActive = true;
            int gen = generation;

            List<TmdbMovie> movies = null;
            List<TmdbPerson> people = null;
            yield return client.GetTrendingMovies(r => movies = r);
            yield return client.GetTrendingPeople(r => people = r);
            if (gen != generation) yield break;

            var pickMovies = (movies ?? new List<TmdbMovie>())
                .Where(m => !string.IsNullOrEmpty(m.poster_path) && !string.IsNullOrEmpty(m.title))
                .Take(menuMovies).ToList();
            // Personnalités : uniquement des acteurs/actrices (ce sont eux qui mènent à des films)
            var pickPeople = (people ?? new List<TmdbPerson>())
                .Where(p => !string.IsNullOrEmpty(p.profile_path) && p.known_for_department == "Acting")
                .OrderByDescending(p => p.popularity)
                .Take(menuPeople).ToList();

            if (pickMovies.Count + pickPeople.Count == 0)
            {
                Debug.LogWarning("[MovieGraph] Tendances indisponibles : démarrage direct sur " + startQuery);
                menuActive = false;
                yield return StartFromQueryRoutine();
                yield break;
            }

            // Titre
            var tgo = new GameObject("MenuTitle");
            tgo.transform.SetParent(content, false);
            tgo.transform.localPosition = new Vector3(0, 0.43f, 0);
            menuTitle = tgo.AddComponent<TextMeshPro>();
            menuTitle.alignment = TextAlignmentOptions.Center;
            menuTitle.fontSize = 0.9f;
            menuTitle.rectTransform.sizeDelta = new Vector2(1.6f, 0.2f);
            menuTitle.text = "<b>Que veux-tu explorer ?</b>\n<size=55%><color=#FFD36B>Tendances de la semaine sur TMDB</color></size>";
            menuTitle.outlineWidth = 0.2f;
            menuTitle.outlineColor = Color.black;

            // Deux arcs autour de l'utilisateur : films en haut, personnalités en bas
            for (int i = 0; i < pickMovies.Count; i++)
            {
                var m = pickMovies[i];
                var n = CreateNodeObject(NodeKind.Movie, m.id, m.title, MovieSubtitle(m), null,
                    ArcPosition(i, pickMovies.Count, 0.13f));
                n.overview = m.overview;
                n.isMenuItem = true;
                n.isPinned = true;
                menuNodes.Add(n);
                StartCoroutine(client.GetImage(m.poster_path, imageSize, n.SetTexture));
                yield return new WaitForSeconds(0.04f);
                if (gen != generation) yield break;
            }
            for (int i = 0; i < pickPeople.Count; i++)
            {
                var p = pickPeople[i];
                var n = CreateNodeObject(NodeKind.Person, p.id, p.name, Department(p.known_for_department), null,
                    ArcPosition(i, pickPeople.Count, -0.25f));
                n.isMenuItem = true;
                n.isPinned = true;
                menuNodes.Add(n);
                StartCoroutine(client.GetImage(p.profile_path, imageSize, n.SetTexture));
                yield return new WaitForSeconds(0.04f);
                if (gen != generation) yield break;
            }

            Debug.Log($"[MovieGraph] Menu : {pickMovies.Count} films + {pickPeople.Count} personnalités.");
            MenuShown?.Invoke();
        }

        /// <summary>Position sur un arc centré sur l'utilisateur (qui est à -distance sur l'axe Z local).</summary>
        Vector3 ArcPosition(int i, int count, float y)
        {
            float spanDeg = Mathf.Min(115f, (count - 1) * 17f);
            float a = count <= 1 ? 0f : Mathf.Lerp(-spanDeg / 2f, spanDeg / 2f, i / (float)(count - 1)) * Mathf.Deg2Rad;
            float d = distanceFromCamera;
            return new Vector3(Mathf.Sin(a) * d, y, -d + Mathf.Cos(a) * d);
        }

        void StartFromMenu(MovieGraphNode chosen)
        {
            menuActive = false;
            foreach (var n in menuNodes)
                if (n != null && n != chosen) Destroy(n.gameObject);
            menuNodes.Clear();
            if (menuTitle != null) Destroy(menuTitle.gameObject);

            chosen.isMenuItem = false;
            nodes[chosen.Key] = chosen;
            BeginGraph(chosen);
        }

        IEnumerator StartFromQueryRoutine()
        {
            MovieGraphNode start = null;
            if (startIsPerson)
            {
                TmdbPerson p = null;
                yield return client.SearchPerson(startQuery, r => p = r);
                if (p == null) { Debug.LogError($"[MovieGraph] Personne introuvable : {startQuery}"); yield break; }
                start = GetOrCreatePerson(p, null, Vector3.zero);
            }
            else
            {
                TmdbMovie m = null;
                yield return client.SearchMovie(startQuery, r => m = r);
                if (m == null) { Debug.LogError($"[MovieGraph] Film introuvable : {startQuery}"); yield break; }
                start = GetOrCreateMovie(m, null, Vector3.zero);
            }
            BeginGraph(start);
        }

        void BeginGraph(MovieGraphNode start)
        {
            root = start;
            root.isPinned = true;                 // le nœud de départ reste au centre
            root.hasMoveTarget = true;            // ... et y glisse en douceur
            root.moveTarget = Vector3.zero;
            root.Pulse();
            Fx?.Open(root.transform.position);
            Started?.Invoke(root);
            StartCoroutine(ExpandRoutine(root));
        }

        void ReturnToMenu()
        {
            generation++;
            CloseTrailer();
            foreach (var n in nodes.Values.ToList()) RemoveNode(n);
            root = null;
            history.Clear();
            hovered = null;
            UpdateInfoPanel();
            transform.localScale = Vector3.one;
            zoom = 1f;
            Recenter();
            Fx?.CloseAll(transform.position);
            StartCoroutine(ShowMenuRoutine());
        }

        static string MovieSubtitle(TmdbMovie m)
        {
            string year = !string.IsNullOrEmpty(m.release_date) && m.release_date.Length >= 4
                ? m.release_date.Substring(0, 4) : "";
            return m.vote_average > 0 ? $"{year}  -  {m.vote_average:0.0}/10" : year;
        }

        static string Department(string d)
        {
            switch (d)
            {
                case "Acting": return "Acteur / actrice";
                case "Directing": return "Réalisation";
                case "Writing": return "Scénario";
                case "Production": return "Production";
                default: return d;
            }
        }

        // ================================================================== Placement & navigation

        static bool HeadTracked()
        {
            var cam = Camera.main;
            return OVRManager.hasInputFocus && OVRPlugin.userPresent && cam != null &&
                   cam.transform.localPosition.sqrMagnitude > 0.01f;
        }

        /// <summary>Replace le graphe face à l'utilisateur, à hauteur des yeux.</summary>
        [ContextMenu("Recentrer devant la caméra")]
        public void Recenter()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            transform.position = cam.transform.position + fwd * distanceFromCamera;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Debug.Log($"[MovieGraph] Graphe placé en {transform.position} (caméra en {cam.transform.position})");
        }

        void HandleInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool vr = OVRManager.isHmdPresent;

            // B : ferme la bande-annonce si elle est ouverte, sinon tout fermer (puis menu)
            if ((vr && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) ||
                (kb != null && kb.backspaceKey.wasPressedThisFrame))
            {
                if (trailer != null) CloseTrailer();
                else CollapseAll();
            }

            // X (manette gauche) ou T : bande-annonce du film survolé
            if ((vr && OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) ||
                (kb != null && kb.tKey.wasPressedThisFrame))
                OpenTrailer(hovered);

            // A : revenir en arrière
            if ((vr && OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch)) ||
                (kb != null && kb.zKey.wasPressedThisFrame))
                GoBack();

            // Y : recentrer
            if ((vr && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch)) ||
                (kb != null && kb.rKey.wasPressedThisFrame))
                Recenter();

            if (menuActive) return;

            // Joystick droit : tourner / zoomer
            Vector2 stick = vr ? OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch) : Vector2.zero;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed) stick.x -= 1f;
                if (kb.rightArrowKey.isPressed) stick.x += 1f;
                if (kb.upArrowKey.isPressed) stick.y += 1f;
                if (kb.downArrowKey.isPressed) stick.y -= 1f;
            }
            if (mouse != null) stick.y += Mathf.Clamp(mouse.scroll.ReadValue().y / 120f, -1f, 1f) * 4f;

            float dt = Time.deltaTime;
            float moved = 0f;

            if (Mathf.Abs(stick.x) > 0.2f)
            {
                transform.Rotate(Vector3.up, -stick.x * rotateSpeed * dt, Space.World);
                moved += Mathf.Abs(stick.x) * dt;
            }
            if (Mathf.Abs(stick.y) > 0.2f)
            {
                zoom = Mathf.Clamp(zoom * (1f + stick.y * zoomSpeed * dt), minZoom, maxZoom);
                transform.localScale = Vector3.one * zoom;
                moved += Mathf.Abs(stick.y) * dt;
            }
            if (moved > 0f) Navigated?.Invoke(moved);
        }

        // ================================================================== Interaction

        public void OnNodeClicked(MovieGraphNode node)
        {
            if (node == null) return;
            if (Time.time - lastClickTime < clickCooldown) return;
            lastClickTime = Time.time;

            if (node.isMenuItem) { StartFromMenu(node); return; }
            if (node.isLoading) return;

            node.Pulse();
            if (node.isExpanded)
            {
                Fx?.Close(node.transform.position);
                Collapse(node);
                history.Remove(node);
                RefreshAllBadges();
            }
            else if (node.IsDeadEnd)
            {
                Fx?.Error(node.transform.position);
                node.ShowToast("Fin du chemin", new Color(0.8f, 0.8f, 0.85f));
            }
            else
            {
                Fx?.Open(node.transform.position);
                StartCoroutine(ExpandRoutine(node));
            }
            ApplyHoverStates();
            UpdateInfoPanel();
        }

        public void OnNodeHovered(MovieGraphNode node)
        {
            if (hovered == node) return;
            hovered = node;
            if (hovered != null) Fx?.Hover(hovered.transform.position);
            ApplyHoverStates();
            UpdateInfoPanel();
        }

        /// <summary>Survol : le nœud + ses voisins + leurs liens brillent, le reste s'atténue.</summary>
        void ApplyHoverStates()
        {
            foreach (var n in menuNodes)
                if (n != null) n.SetHighlighted(n == hovered);

            var playing = trailer != null ? trailer.Movie : null;
            foreach (var n in nodes.Values)
            {
                bool focus = hovered != null && (n == hovered || hovered.neighbors.Contains(n));
                n.SetHighlighted(n == hovered);
                // Bande-annonce ouverte : le graphe passe en retrait (sauf le film projeté et ce qu'on survole)
                n.SetDimmed(playing != null ? n != playing && n != hovered : hovered != null && !focus);
            }

            foreach (var e in edges)
            {
                if (hovered == null) e.SetState(EdgeState.Normal);
                else if (e.a == hovered || e.b == hovered) e.SetState(EdgeState.Highlighted);
                else e.SetState(EdgeState.Dimmed);
            }
        }

        /// <summary>A : annule la dernière ouverture.</summary>
        public void GoBack()
        {
            if (menuActive) return;
            while (history.Count > 0)
            {
                var last = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (last == null || !last.isExpanded) continue;

                Collapse(last);
                last.Pulse();
                last.ShowToast("Retour", MovieGraphVisuals.GoldBright);
                Fx?.Close(last.transform.position);
                RefreshAllBadges();
                ApplyHoverStates();
                UpdateInfoPanel();
                WentBack?.Invoke();
                return;
            }
            // Rien à annuler
            if (root != null) { root.ShowToast("Rien à annuler  -  B : menu", new Color(0.8f, 0.8f, 0.85f)); Fx?.Error(root.transform.position); }
        }

        /// <summary>B : ferme tout (ne garde que le départ). Si c'est déjà le cas → retour au menu.</summary>
        public void CollapseAll()
        {
            if (menuActive) return;
            if (root == null) { if (showStartMenu) ReturnToMenu(); return; }

            if (nodes.Count <= 1 && !root.isExpanded && showStartMenu)
            {
                ReturnToMenu();
                return;
            }

            generation++;
            foreach (var n in nodes.Values.ToList())
                if (n != root) RemoveNode(n);

            history.Clear();
            root.neighbors.Clear();
            root.isExpanded = false;
            root.isLoading = false;
            root.velocity = Vector3.zero;
            root.hasMoveTarget = true;
            root.moveTarget = Vector3.zero;
            root.RefreshLabel();
            root.Pulse();
            if (showStartMenu) root.ShowToast("B encore : menu", MovieGraphVisuals.GoldBright);

            hovered = null;
            RefreshAllBadges();
            ApplyHoverStates();
            UpdateInfoPanel();
            Fx?.CloseAll(root.transform.position);
            ClosedAll?.Invoke();
            Debug.Log("[MovieGraph] Tout fermé.");
        }

        // ================================================================== Bande-annonce

        /// <summary>Ouvre un grand écran qui lit la bande-annonce du film survolé.</summary>
        public void OpenTrailer(MovieGraphNode node)
        {
            if (node == null || node.kind != NodeKind.Movie || client == null) return;
            if (trailer != null && trailer.Movie == node) return;
            if (trailer != null) trailer.Close();

            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : transform.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 eye = cam != null ? cam.transform.position : transform.position - fwd * distanceFromCamera;
            // Derrière le graphe, légèrement en hauteur, comme un écran de cinéma
            Vector3 pos = eye + fwd * (distanceFromCamera + 0.9f) + Vector3.up * 0.15f;

            trailer = MovieGraphTrailer.Open(client, node, pos, Quaternion.LookRotation(fwd));
            node.Pulse();
            Fx?.Open(pos);
            ApplyHoverStates();
        }

        void CloseTrailer()
        {
            if (trailer == null) return;
            Fx?.Close(trailer.transform.position);
            trailer.Close();
            trailer = null;
            ApplyHoverStates();
        }

        // ================================================================== Ouverture / fermeture

        /// <summary>Précharge les voisins d'un nœud (pour le badge "+N" et une ouverture instantanée).</summary>
        IEnumerator Prefetch(MovieGraphNode node)
        {
            if (node == null || node.prefetch == PrefetchState.Loading || node.prefetch == PrefetchState.Done) yield break;
            node.prefetch = PrefetchState.Loading;
            node.RefreshBadge();

            if (node.kind == NodeKind.Movie)
            {
                List<TmdbPerson> cast = null;
                yield return client.GetMovieCast(node.tmdbId, r => cast = r);
                if (node == null) yield break;
                if (cast != null)
                    node.castCandidates = cast.OrderBy(c => c.order).Take(maxNeighbors).ToList();
                node.prefetch = cast != null ? PrefetchState.Done : PrefetchState.Failed;
            }
            else
            {
                List<TmdbMovie> movies = null;
                yield return client.GetPersonMovies(node.tmdbId, r => movies = r);
                if (node == null) yield break;
                if (movies != null)
                    node.movieCandidates = movies
                        .Where(m => !string.IsNullOrEmpty(m.poster_path) && m.vote_count >= minVoteCount)
                        .GroupBy(m => m.id).Select(g => g.First())
                        .OrderByDescending(m => m.vote_count)
                        .Take(maxNeighbors).ToList();
                node.prefetch = movies != null ? PrefetchState.Done : PrefetchState.Failed;
            }

            UpdateNewCount(node);
            node.RefreshBadge();
        }

        void UpdateNewCount(MovieGraphNode n)
        {
            if (n == null || n.prefetch != PrefetchState.Done) return;
            var linked = new HashSet<int>(n.neighbors.Select(x => x.tmdbId));
            n.newNeighborCount = n.kind == NodeKind.Movie
                ? (n.castCandidates?.Count(c => !linked.Contains(c.id)) ?? 0)
                : (n.movieCandidates?.Count(c => !linked.Contains(c.id)) ?? 0);
        }

        void RefreshAllBadges()
        {
            foreach (var n in nodes.Values)
            {
                UpdateNewCount(n);
                n.RefreshBadge();
            }
        }

        IEnumerator ExpandRoutine(MovieGraphNode node)
        {
            int gen = generation;
            node.isLoading = true;
            node.RefreshLabel();

            if (node.prefetch == PrefetchState.None || node.prefetch == PrefetchState.Failed)
            {
                node.prefetch = PrefetchState.None;
                yield return Prefetch(node);
            }
            while (node != null && node.prefetch == PrefetchState.Loading) yield return null;
            if (node == null || gen != generation) yield break;

            if (node.prefetch == PrefetchState.Failed)
            {
                node.isLoading = false;
                node.RefreshLabel();
                node.ShowToast("Erreur réseau", new Color(1f, 0.5f, 0.45f));
                Fx?.Error(node.transform.position);
                yield break;
            }

            Vector3 origin = node.transform.localPosition;
            if (node.kind == NodeKind.Movie)
            {
                foreach (var p in node.castCandidates)
                {
                    if (nodes.Count >= maxNodes) break;
                    Link(node, GetOrCreatePerson(p, node, origin));
                    yield return new WaitForSeconds(0.05f);       // apparition en cascade
                    if (node == null || gen != generation) yield break;
                }
            }
            else
            {
                foreach (var m in node.movieCandidates)
                {
                    if (nodes.Count >= maxNodes) break;
                    Link(node, GetOrCreateMovie(m, node, origin));
                    yield return new WaitForSeconds(0.05f);
                    if (node == null || gen != generation) yield break;
                }
            }

            node.isLoading = false;
            node.RefreshLabel();

            if (node.neighbors.Count == 0)
            {
                // Rien d'assez connu derrière : on le dit clairement au lieu de rester bloqué
                node.isExpanded = false;
                node.newNeighborCount = 0;
                node.RefreshBadge();
                node.ShowToast(node == root ? "Rien de connu ici  -  B : menu" : "Fin du chemin",
                    new Color(0.8f, 0.8f, 0.85f));
                Fx?.Error(node.transform.position);
                UpdateInfoPanel();
                Debug.Log($"[MovieGraph] « {node.title} » : aucun voisin assez connu.");
                yield break;
            }

            node.isExpanded = true;
            history.Add(node);

            RefreshAllBadges();
            ApplyHoverStates();
            UpdateInfoPanel();
            Expanded?.Invoke(node);
            Debug.Log($"[MovieGraph] « {node.title} » déplié : {node.neighbors.Count} voisins, {nodes.Count} nœuds au total.");
        }

        void Collapse(MovieGraphNode node)
        {
            // Retire les feuilles que ce nœud avait créées (celles qui ne sont reliées qu'à lui)
            var toRemove = node.neighbors
                .Where(n => n.spawnedBy == node && !n.isExpanded && n.neighbors.Count <= 1)
                .ToList();

            foreach (var n in toRemove) RemoveNode(n);

            node.isExpanded = false;
            node.RefreshLabel();
        }

        // ================================================================== Création

        MovieGraphNode GetOrCreateMovie(TmdbMovie m, MovieGraphNode parent, Vector3 near)
        {
            string key = "m" + m.id;
            if (nodes.TryGetValue(key, out var existing)) return existing;

            var n = CreateNodeObject(NodeKind.Movie, m.id, m.title, MovieSubtitle(m), parent,
                near + Random.insideUnitSphere * springLength * 0.3f);
            n.overview = m.overview;
            n.popularity = m.popularity;
            nodes[key] = n;
            StartCoroutine(client.GetImage(m.poster_path, imageSize, n.SetTexture));
            StartCoroutine(Prefetch(n));
            return n;
        }

        MovieGraphNode GetOrCreatePerson(TmdbPerson p, MovieGraphNode parent, Vector3 near)
        {
            string key = "p" + p.id;
            if (nodes.TryGetValue(key, out var existing)) return existing;

            var n = CreateNodeObject(NodeKind.Person, p.id, p.name, p.character, parent,
                near + Random.insideUnitSphere * springLength * 0.3f);
            n.popularity = p.popularity;
            nodes[key] = n;
            StartCoroutine(client.GetImage(p.profile_path, imageSize, n.SetTexture));
            StartCoroutine(Prefetch(n));
            return n;
        }

        MovieGraphNode CreateNodeObject(NodeKind kind, int id, string title, string subtitle, MovieGraphNode parent, Vector3 localPos)
        {
            var go = new GameObject($"{kind}_{title}");
            go.transform.SetParent(content, false);
            go.transform.localPosition = localPos;

            var n = go.AddComponent<MovieGraphNode>();
            n.kind = kind;
            n.tmdbId = id;
            n.title = title;
            n.subtitle = subtitle;
            n.spawnedBy = parent;
            n.Build(nodeSize);
            return n;
        }

        void Link(MovieGraphNode a, MovieGraphNode b)
        {
            if (a == b || a.neighbors.Contains(b)) return;
            a.neighbors.Add(b);
            b.neighbors.Add(a);

            var go = new GameObject($"Edge_{a.title}_{b.title}");
            go.transform.SetParent(content, false);
            go.AddComponent<LineRenderer>();
            var e = go.AddComponent<MovieGraphEdge>();
            e.Init(a, b);
            edges.Add(e);
        }

        void RemoveNode(MovieGraphNode n)
        {
            foreach (var nb in n.neighbors) nb.neighbors.Remove(n);
            n.neighbors.Clear();

            for (int i = edges.Count - 1; i >= 0; i--)
            {
                if (edges[i].a == n || edges[i].b == n)
                {
                    Destroy(edges[i].gameObject);
                    edges.RemoveAt(i);
                }
            }

            if (hovered == n) hovered = null;
            history.Remove(n);
            nodes.Remove(n.Key);
            Destroy(n.gameObject);
        }

        // ================================================================== Layout

        void Update()
        {
            HandleInput();
            if (nodes.Count == 0) { if (hovered != null) PlaceInfoPanel(); return; }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var list = nodes.Values.ToList();
            int count = list.Count;
            var forces = new Vector3[count];
            var index = new Dictionary<MovieGraphNode, int>(count);
            for (int i = 0; i < count; i++) index[list[i]] = i;

            // Répulsion entre toutes les paires (OK jusqu'à ~200 nœuds)
            for (int i = 0; i < count; i++)
            {
                Vector3 pi = list[i].transform.localPosition;
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 d = pi - list[j].transform.localPosition;
                    float dist2 = Mathf.Max(d.sqrMagnitude, 0.0025f);
                    Vector3 f = d / Mathf.Sqrt(dist2) * (repulsion / dist2);
                    forces[i] += f;
                    forces[j] -= f;
                }
                forces[i] -= pi * centerPull;
            }

            // Ressorts sur les liens
            foreach (var e in edges)
            {
                if (e.a == null || e.b == null) continue;
                Vector3 d = e.b.transform.localPosition - e.a.transform.localPosition;
                float dist = d.magnitude;
                if (dist < 0.0001f) continue;
                Vector3 f = d / dist * ((dist - springLength) * springStrength);
                forces[index[e.a]] += f;
                forces[index[e.b]] -= f;
            }

            float k = 1f - Mathf.Exp(-Time.deltaTime * 4f);
            for (int i = 0; i < count; i++)
            {
                var n = list[i];
                if (n.hasMoveTarget)
                {
                    n.transform.localPosition = Vector3.Lerp(n.transform.localPosition, n.moveTarget, k);
                    if ((n.transform.localPosition - n.moveTarget).sqrMagnitude < 0.00001f) n.hasMoveTarget = false;
                }
                if (n.isPinned) { n.velocity = Vector3.zero; continue; }
                n.velocity = Vector3.ClampMagnitude((n.velocity + forces[i] * dt) * damping, maxSpeed);
                n.transform.localPosition += n.velocity * dt;
            }

            if (hovered != null) PlaceInfoPanel();
        }

        // ================================================================== Panneau d'infos

        void CreateInfoPanel()
        {
            var go = new GameObject("InfoPanel");
            go.transform.SetParent(content, false);
            infoPanel = go.AddComponent<TextMeshPro>();
            infoPanel.fontSize = 0.3f;
            infoPanel.alignment = TextAlignmentOptions.TopLeft;
            infoPanel.textWrappingMode = TextWrappingModes.Normal;
            infoPanel.rectTransform.sizeDelta = new Vector2(0.45f, 0.4f);
            infoPanel.outlineWidth = 0.2f;
            infoPanel.outlineColor = Color.black;
            go.SetActive(false);
        }

        void UpdateInfoPanel()
        {
            if (infoPanel == null) return;
            if (hovered == null) { infoPanel.gameObject.SetActive(false); return; }

            string kind = hovered.kind == NodeKind.Movie ? "Film" : "Personne";
            string what = hovered.kind == NodeKind.Movie ? "acteurs" : "films";
            string action =
                hovered.isMenuItem ? "Gâchette : commencer ici" :
                hovered.isLoading ? "Chargement..." :
                hovered.isExpanded ? "Gâchette : replier" :
                hovered.IsDeadEnd ? "Fin du chemin : rien de nouveau ici" :
                hovered.newNeighborCount > 0 ? $"Gâchette : voir {hovered.newNeighborCount} {what}" :
                $"Gâchette : voir ses {what}";
            string overview = string.IsNullOrEmpty(hovered.overview) ? "" :
                "\n\n" + (hovered.overview.Length > 300 ? hovered.overview.Substring(0, 300) + "..." : hovered.overview);
            if (hovered.kind == NodeKind.Movie)
                action += "\n<color=#FFD36B>X : voir la bande-annonce</color>";
            string footer = hovered.isMenuItem ? "" :
                "\n<size=80%><color=#888888>A : retour   ·   B : tout fermer</color></size>";

            infoPanel.text = $"<size=130%><b>{hovered.title}</b></size>\n" +
                             $"<color=#AAAAAA>{kind}  {hovered.subtitle}</color>" +
                             overview +
                             $"\n\n<color=#FFD36B>{action}</color>" + footer;
            infoPanel.gameObject.SetActive(true);
            PlaceInfoPanel();
        }

        void PlaceInfoPanel()
        {
            if (hovered == null || infoPanel == null) return;
            var cam = Camera.main;
            Transform t = hovered.transform;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            infoPanel.transform.position = t.position + right * ((nodeSize * 0.6f + 0.25f) * zoom);
            infoPanel.transform.rotation = t.rotation;
        }
    }
}
