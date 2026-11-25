using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MovieGraph
{
    public enum NodeKind { Movie, Person }
    public enum PrefetchState { None, Loading, Done, Failed }

    /// <summary>
    /// Un nœud "étoile" : affiche/photo + liseré + halo lumineux + titre.
    /// États visuels : normal, survolé (doré), atténué (quand on survole un autre nœud),
    /// chargement (halo qui pulse), clic (onde de choc + rebond).
    /// </summary>
    public class MovieGraphNode : MonoBehaviour
    {
        public NodeKind kind;
        public int tmdbId;
        public string title;
        public string subtitle;
        [TextArea] public string overview;
        public float popularity;

        public bool isExpanded;
        public bool isLoading;
        public bool isPinned;          // tenu / posé : la physique ne le bouge plus
        public bool isMenuItem;        // carte du menu de départ (hors graphe)

        // Voisins préchargés en arrière-plan (badge "+N" et ouverture instantanée)
        public PrefetchState prefetch = PrefetchState.None;
        public List<TmdbPerson> castCandidates;
        public List<TmdbMovie> movieCandidates;
        [HideInInspector] public int newNeighborCount;   // voisins pas encore reliés

        // Données de layout (gérées par MovieGraphManager)
        [HideInInspector] public Vector3 velocity;
        [HideInInspector] public MovieGraphNode spawnedBy;
        [HideInInspector] public bool hasMoveTarget;
        [HideInInspector] public Vector3 moveTarget;
        public readonly HashSet<MovieGraphNode> neighbors = new HashSet<MovieGraphNode>();

        /// <summary>Plus rien de nouveau derrière cette carte.</summary>
        public bool IsDeadEnd => !isMenuItem && !isExpanded && prefetch == PrefetchState.Done && newNeighborCount == 0;

        public string Key => (kind == NodeKind.Movie ? "m" : "p") + tmdbId;
        public Color Tint => kind == NodeKind.Movie ? MovieGraphVisuals.MovieTint : MovieGraphVisuals.PersonTint;

        Transform visual;              // tout ce qui s'anime (échelle)
        Renderer cardRenderer, frameRenderer, haloRenderer, ringRenderer;
        TextMeshPro label;
        float cardW;

        GameObject badge;
        Renderer badgeRenderer;
        TextMeshPro badgeText;
        string badgeShown;

        TextMeshPro toast;
        float toastT = 1f;
        float deadT;

        bool highlighted, dimmed;
        float spawnT;                  // 0 → 1 : apparition
        float pulseT = 1f;             // 0 → 1 : onde de choc après un clic
        float hiT, dimT;               // transitions douces

        public void Build(float size)
        {
            cardW = size;
            float w = size, h = size * 1.5f;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.zero;          // apparaît en grandissant

            // Halo d'étoile derrière la carte
            haloRenderer = MovieGraphVisuals.CreateSpriteQuad("Halo", visual, MovieGraphVisuals.SoftCircle,
                WithAlpha(Tint, 0.35f), h * 2.6f, 0.01f);

            // Onde de choc (invisible au repos)
            ringRenderer = MovieGraphVisuals.CreateSpriteQuad("Ring", visual, MovieGraphVisuals.Ring,
                WithAlpha(MovieGraphVisuals.GoldBright, 0f), h, 0.005f);

            // Liseré
            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = "Frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(visual, false);
            frame.transform.localScale = new Vector3(w * 1.06f, h * 1.04f, 1f);
            frame.transform.localPosition = new Vector3(0, 0, 0.002f);
            frameRenderer = frame.GetComponent<Renderer>();
            frameRenderer.material = MovieGraphVisuals.NewUnlitMaterial(Tint);

            // Affiche
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "Card";
            Destroy(card.GetComponent<Collider>());
            card.transform.SetParent(visual, false);
            card.transform.localScale = new Vector3(w, h, 1f);
            cardRenderer = card.GetComponent<Renderer>();
            cardRenderer.material = MovieGraphVisuals.NewUnlitMaterial(new Color(0.12f, 0.12f, 0.16f));

            // Collider sur la racine (clic / grab)
            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(w * 1.15f, h * 1.15f, 0.03f);

            // Titre
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(visual, false);
            labelGo.transform.localPosition = new Vector3(0, -h * 0.5f - 0.035f, 0);
            label = labelGo.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Top;
            label.fontSize = 0.35f;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.sizeDelta = new Vector2(w * 2.2f, 0.1f);
            label.color = Color.white;
            label.outlineWidth = 0.2f;
            label.outlineColor = Color.black;
            RefreshLabel();

            // Badge "+N" (coin haut-droit, devant la carte)
            badge = new GameObject("Badge");
            badge.transform.SetParent(visual, false);
            badge.transform.localPosition = new Vector3(w * 0.5f, h * 0.5f, -0.004f);
            badgeRenderer = MovieGraphVisuals.CreateSpriteQuad("BadgeDisc", badge.transform,
                MovieGraphVisuals.SoftCircle, MovieGraphVisuals.Gold, w * 0.62f, 0f);
            var bt = new GameObject("BadgeText");
            bt.transform.SetParent(badge.transform, false);
            bt.transform.localPosition = new Vector3(0, 0, -0.001f);
            badgeText = bt.AddComponent<TextMeshPro>();
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.fontSize = 0.3f;
            badgeText.fontStyle = FontStyles.Bold;
            badgeText.color = new Color(0.12f, 0.08f, 0.02f);
            badgeText.rectTransform.sizeDelta = new Vector2(w * 0.6f, w * 0.3f);
            badge.SetActive(false);

            // Message temporaire au-dessus de la carte ("Fin du chemin"...)
            var tg = new GameObject("Toast");
            tg.transform.SetParent(visual, false);
            tg.transform.localPosition = new Vector3(0, h * 0.5f + 0.04f, -0.005f);
            toast = tg.AddComponent<TextMeshPro>();
            toast.alignment = TextAlignmentOptions.Bottom;
            toast.fontSize = 0.3f;
            toast.rectTransform.sizeDelta = new Vector2(w * 3f, 0.08f);
            toast.outlineWidth = 0.25f;
            toast.outlineColor = Color.black;
            tg.SetActive(false);
        }

        /// <summary>Met à jour le badge : "…" pendant le préchargement, "+N" s'il reste des liens à ouvrir.</summary>
        public void RefreshBadge()
        {
            if (badge == null) return;
            string txt = null;
            if (!isMenuItem && !isExpanded)
            {
                if (prefetch == PrefetchState.Loading) txt = "...";
                else if (prefetch == PrefetchState.Done && newNeighborCount > 0) txt = "+" + newNeighborCount;
            }
            if (txt == badgeShown) return;
            badgeShown = txt;
            badge.SetActive(txt != null);
            if (txt != null) badgeText.text = txt;
        }

        /// <summary>Petit message qui monte et s'efface au-dessus de la carte.</summary>
        public void ShowToast(string message, Color color)
        {
            if (toast == null) return;
            toast.text = message;
            toast.color = color;
            toast.gameObject.SetActive(true);
            toastT = 0f;
        }

        public void RefreshLabel()
        {
            if (label == null) return;
            string state = isLoading ? " …" : "";
            label.text = string.IsNullOrEmpty(subtitle)
                ? $"<b>{title}</b>{state}"
                : $"<b>{title}</b>{state}\n<size=70%><color=#BBBBBB>{subtitle}</color></size>";
        }

        public void SetTexture(Texture2D tex)
        {
            if (tex == null || cardRenderer == null) return;
            var m = cardRenderer.material;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            ApplyCardColor();
        }

        public void SetHighlighted(bool on) => highlighted = on;
        public void SetDimmed(bool on) => dimmed = on;

        /// <summary>Onde de choc dorée + petit rebond (retour visuel du clic).</summary>
        public void Pulse() => pulseT = 0f;

        void Update()
        {
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-dt * 12f);

            spawnT = Mathf.Min(1f, spawnT + dt * 2.5f);
            pulseT = Mathf.Min(1f, pulseT + dt * 2.2f);
            hiT = Mathf.Lerp(hiT, highlighted ? 1f : 0f, k);
            // Une carte "fin du chemin" est grisée en permanence (moins si on la survole)
            float deadTarget = IsDeadEnd ? (highlighted ? 0.4f : 0.65f) : 0f;
            deadT = Mathf.Lerp(deadT, deadTarget, k);
            dimT = Mathf.Lerp(dimT, Mathf.Max(dimmed && !highlighted ? 1f : 0f, deadT), k);

            // Message temporaire
            if (toastT < 1f)
            {
                toastT = Mathf.Min(1f, toastT + dt / 1.6f);
                toast.transform.localPosition = new Vector3(0, cardW * 0.75f + 0.04f + toastT * 0.06f, -0.005f);
                toast.alpha = toastT < 0.7f ? 1f : 1f - (toastT - 0.7f) / 0.3f;
                if (toastT >= 1f) toast.gameObject.SetActive(false);
            }

            // Échelle : apparition (élastique) × survol × rebond du clic
            float appear = EaseOutBack(spawnT);
            float bounce = 1f + 0.25f * Mathf.Sin(pulseT * Mathf.PI) * (1f - pulseT);
            float hover = 1f + 0.15f * hiT;
            visual.localScale = Vector3.one * (appear * bounce * hover);

            // Halo : pulse pendant le chargement, doré au survol, éteint si atténué
            float loadingPulse = isLoading ? 0.25f + 0.25f * Mathf.Sin(Time.time * 8f) : 0f;
            Color halo = Color.Lerp(Tint, MovieGraphVisuals.Gold, hiT);
            float haloA = (0.3f + 0.35f * hiT + loadingPulse + (isExpanded ? 0.1f : 0f)) * (1f - 0.85f * dimT);
            haloRenderer.material.color = WithAlpha(halo, haloA);

            // Onde de choc
            float ringScale = 1f + pulseT * 2.5f;
            ringRenderer.transform.localScale = Vector3.one * (cardW * 1.5f * ringScale);
            ringRenderer.material.color = WithAlpha(MovieGraphVisuals.GoldBright, (1f - pulseT) * 0.9f);

            // Liseré
            Color frame = Color.Lerp(Tint, MovieGraphVisuals.Gold, hiT);
            MovieGraphVisuals.SetColor(frameRenderer.material, frame * (1f - 0.7f * dimT));

            ApplyCardColor();

            // Ne touche au texte que si l'opacité change vraiment (sinon TMP reconstruit le texte à chaque frame)
            float labelAlpha = Mathf.Round((1f - 0.75f * dimT) * 20f) / 20f;
            if (!Mathf.Approximately(label.alpha, labelAlpha)) label.alpha = labelAlpha;
        }

        void ApplyCardColor()
        {
            if (cardRenderer == null) return;
            bool hasTex = cardRenderer.material.mainTexture != null;
            Color baseC = hasTex ? Color.white : new Color(0.12f, 0.12f, 0.16f);
            MovieGraphVisuals.SetColor(cardRenderer.material, baseC * (1f - 0.7f * dimT));
        }

        void LateUpdate()
        {
            // Billboard : toujours face à la caméra
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }
    }
}
