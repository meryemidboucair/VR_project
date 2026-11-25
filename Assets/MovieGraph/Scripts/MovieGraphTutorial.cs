using TMPro;
using UnityEngine;

namespace MovieGraph
{
    /// <summary>
    /// Mini-tutoriel guidé : une bulle qui flotte sous le regard et avance
    /// toute seule dès que l'utilisateur a fait le geste demandé.
    /// </summary>
    public class MovieGraphTutorial : MonoBehaviour
    {
        public MovieGraphManager graph;
        public bool showTutorial = true;
        [Tooltip("Distance de la bulle devant les yeux")]
        public float distance = 0.9f;
        [Tooltip("Décalage vertical (négatif = sous le regard)")]
        public float heightOffset = -0.32f;

        enum Step { Pick, Open, Navigate, BackAndClose, Done, Hidden }

        Step step = Step.Hidden;
        bool begun;
        float stepTime;
        float navigateAccum;

        Transform bubble;
        TextMeshPro text;
        Renderer background, border;
        float alpha;          // fondu d'apparition
        float targetAlpha;

        static readonly string Gold = "#FFD36B";

        void Start()
        {
            if (graph == null) graph = GetComponent<MovieGraphManager>();
            if (!showTutorial || graph == null) return;

            CreateBubble();

            // Le tutoriel ne se joue qu'une fois par session (pas à chaque retour au menu)
            graph.MenuShown += () => { if (!begun) { begun = true; Go(Step.Pick); } };
            graph.Started += _ =>
            {
                if (step == Step.Pick) Go(Step.Open);
                else if (!begun) { begun = true; Go(Step.Open); }   // démarrage direct sans menu
            };
            graph.Expanded += n => { if (step == Step.Open && n != graph.Root) Go(Step.Navigate); };
            graph.Navigated += amount =>
            {
                if (step != Step.Navigate) return;
                navigateAccum += amount;
                if (navigateAccum > 0.6f) Go(Step.BackAndClose);
            };
            graph.WentBack += () => { if (step == Step.BackAndClose) Go(Step.Done); };
            graph.ClosedAll += () => { if (step == Step.BackAndClose) Go(Step.Done); };
        }

        void Go(Step s)
        {
            step = s;
            stepTime = 0f;
            switch (s)
            {
                case Step.Pick:
                    Show($"<b>Bienvenue !</b>\nPointe une affiche avec la <color={Gold}>manette droite</color>\npuis appuie sur la <color={Gold}>gâchette</color> pour commencer.");
                    break;
                case Step.Open:
                    Show($"Le badge <color={Gold}><b>+8</b></color> indique ce qui se cache derrière une carte.\n<color={Gold}>Gâchette</color> sur une autre carte pour l'ouvrir.\n<size=80%><color=#999999>Une carte grisée = fin du chemin</color></size>");
                    break;
                case Step.Navigate:
                    Show($"<color={Gold}>Joystick droit</color>\ngauche / droite : tourner autour du graphe\nhaut / bas : zoomer");
                    break;
                case Step.BackAndClose:
                    Show($"<color={Gold}>A</color> : revenir en arrière\n<color={Gold}>B</color> : tout fermer  <size=80%><color=#999999>(B encore : menu)</color></size>\n<size=80%><color=#999999>Grip : attraper une carte   ·   Y : recentrer</color></size>");
                    break;
                case Step.Done:
                    Show("<b>C'est parti !</b>\nBonne exploration");
                    break;
                case Step.Hidden:
                    targetAlpha = 0f;
                    break;
            }
        }

        void Show(string message)
        {
            text.text = message;
            alpha = 0f;           // petit fondu à chaque nouvelle étape
            targetAlpha = 1f;
            MovieGraphFeedback.Instance?.Haptic(0.2f, 0.03f);
        }

        void Update()
        {
            if (bubble == null) return;
            stepTime += Time.deltaTime;

            // Étapes qui se terminent toutes seules
            if (step == Step.BackAndClose && stepTime > 12f) Go(Step.Done);
            else if (step == Step.Done && stepTime > 3f) Go(Step.Hidden);

            alpha = Mathf.MoveTowards(alpha, targetAlpha, Time.deltaTime * 2.5f);
            bubble.gameObject.SetActive(alpha > 0.01f);
            if (alpha <= 0.01f) return;

            text.alpha = alpha;
            background.material.color = new Color(0.03f, 0.03f, 0.08f, 0.82f * alpha);
            border.material.color = new Color(1f, 0.78f, 0.32f, 0.5f * alpha);

            // Suit le regard en douceur (sans être collé au visage)
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 target = cam.transform.position + fwd * distance + Vector3.up * heightOffset;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 3f);
            bubble.position = Vector3.Lerp(bubble.position, target, k);
            bubble.rotation = Quaternion.LookRotation(bubble.position - cam.transform.position, Vector3.up);
        }

        void CreateBubble()
        {
            bubble = new GameObject("TutorialBubble").transform;
            var cam = Camera.main;
            if (cam != null) bubble.position = cam.transform.position + cam.transform.forward * distance;

            background = MovieGraphVisuals.CreateSpriteQuad("Background", bubble, null,
                new Color(0.03f, 0.03f, 0.08f, 0.82f), 1f, 0.002f);
            background.transform.localScale = new Vector3(0.62f, 0.2f, 1f);

            border = MovieGraphVisuals.CreateSpriteQuad("Border", bubble, null,
                new Color(1f, 0.78f, 0.32f, 0.5f), 1f, 0.003f);
            border.transform.localScale = new Vector3(0.63f, 0.205f, 1f);

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(bubble, false);
            text = tgo.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 0.32f;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.rectTransform.sizeDelta = new Vector2(0.58f, 0.18f);
            text.color = Color.white;

            bubble.gameObject.SetActive(false);
        }
    }
}
