using UnityEngine;

namespace MovieGraph
{
    public enum EdgeState { Normal, Highlighted, Dimmed }

    /// <summary>
    /// Lien "constellation" doré et lumineux entre deux nœuds.
    /// Normal : or doux. Survol d'un des deux nœuds : or éclatant qui scintille. Sinon atténué.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class MovieGraphEdge : MonoBehaviour
    {
        public MovieGraphNode a;
        public MovieGraphNode b;

        public float baseWidth = 0.006f;
        public float highlightWidth = 0.014f;

        public float normalAlpha = 0.45f;
        public float dimmedAlpha = 0.07f;

        EdgeState state = EdgeState.Normal;
        LineRenderer line;
        float currentAlpha;
        float currentWidth;
        float currentHighlight;  // 0 → 1
        float phase;

        public void Init(MovieGraphNode from, MovieGraphNode to)
        {
            a = from;
            b = to;
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.material = MovieGraphVisuals.NewSpriteMaterial(MovieGraphVisuals.GlowLine);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            currentAlpha = 0f;           // apparaît en fondu
            currentWidth = baseWidth;
            phase = Random.value * 10f;
            UpdatePositions();
            Apply();
        }

        public void SetState(EdgeState s) => state = s;

        void LateUpdate()
        {
            if (a == null || b == null) { line.enabled = false; return; }
            line.enabled = true;

            float targetAlpha = state == EdgeState.Highlighted ? 1f : state == EdgeState.Dimmed ? dimmedAlpha : normalAlpha;
            float targetWidth = state == EdgeState.Highlighted ? highlightWidth : baseWidth;
            float targetHi = state == EdgeState.Highlighted ? 1f : 0f;

            float k = 1f - Mathf.Exp(-Time.deltaTime * 10f);
            currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, k);
            currentWidth = Mathf.Lerp(currentWidth, targetWidth, k);
            currentHighlight = Mathf.Lerp(currentHighlight, targetHi, k);

            UpdatePositions();
            Apply();
        }

        void UpdatePositions()
        {
            if (a == null || b == null) return;
            line.SetPosition(0, a.transform.position);
            line.SetPosition(1, b.transform.position);
        }

        void Apply()
        {
            // Scintillement doux quand le lien est mis en avant
            float shimmer = 1f + currentHighlight * 0.25f * Mathf.Sin(Time.time * 6f + phase);
            Color c = Color.Lerp(MovieGraphVisuals.Gold, MovieGraphVisuals.GoldBright, currentHighlight);
            c.a = Mathf.Clamp01(currentAlpha * shimmer);

            line.startColor = c;
            line.endColor = c;
            line.startWidth = currentWidth;
            line.endWidth = currentWidth;
        }
    }
}
