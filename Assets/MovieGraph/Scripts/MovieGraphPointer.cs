using UnityEngine;
using UnityEngine.InputSystem;

namespace MovieGraph
{
    /// <summary>
    /// Pointeur unique pour le graphe :
    ///  - VR (Quest)  : rayon depuis la manette droite. Gâchette = déplier/replier, Grip = attraper/déplacer.
    ///  - Éditeur/PC  : souris. Clic gauche = déplier/replier, clic droit maintenu = déplacer.
    /// </summary>
    public class MovieGraphPointer : MonoBehaviour
    {
        public MovieGraphManager graph;
        [Tooltip("Origine du rayon en VR (ex : RightControllerAnchor). Vide = souris.")]
        public Transform rayOrigin;
        public float maxDistance = 10f;
        public Color rayColor = new Color(1f, 0.82f, 0.4f, 0.85f);

        LineRenderer ray;
        MovieGraphNode hovered;
        MovieGraphTrailer hoveredTrailer;
        MovieGraphNode dragged;
        float dragDistance;

        bool UseVR => rayOrigin != null && OVRManager.isHmdPresent;

        void Start()
        {
            if (graph == null) graph = FindFirstObjectByType<MovieGraphManager>();

            ray = gameObject.AddComponent<LineRenderer>();
            ray.positionCount = 2;
            ray.startWidth = 0.004f;
            ray.endWidth = 0.001f;
            ray.material = new Material(Shader.Find("Sprites/Default"));
            ray.startColor = rayColor;
            ray.endColor = new Color(rayColor.r, rayColor.g, rayColor.b, 0f);
        }

        void Update()
        {
            if (graph == null) return;

            if (!GetRay(out Ray r))
            {
                ray.enabled = false;
                return;
            }

            // --- Déplacement en cours
            if (dragged != null)
            {
                Vector3 target = r.origin + r.direction * dragDistance;
                dragged.transform.position = target;
                DrawRay(r.origin, target);
                if (DragReleased()) EndDrag();
                return;
            }

            // --- Survol
            MovieGraphNode hit = null;
            MovieGraphTrailer hitTrailer = null;
            Vector3 end = r.origin + r.direction * maxDistance;
            if (Physics.Raycast(r, out RaycastHit info, maxDistance))
            {
                hit = info.collider.GetComponentInParent<MovieGraphNode>();
                if (hit == null) hitTrailer = info.collider.GetComponentInParent<MovieGraphTrailer>();
                end = info.point;
            }

            if (hit != hovered)
            {
                hovered = hit;
                graph.OnNodeHovered(hovered);
            }

            if (hitTrailer != hoveredTrailer)
            {
                if (hoveredTrailer != null) hoveredTrailer.SetHovered(false);
                hoveredTrailer = hitTrailer;
                if (hoveredTrailer != null) hoveredTrailer.SetHovered(true);
            }

            DrawRay(r.origin, end);

            // Gâchette sur l'écran de bande-annonce : pause / lecture
            if (hoveredTrailer != null && ClickPressed()) hoveredTrailer.TogglePlay();

            if (hovered != null)
            {
                if (ClickPressed()) graph.OnNodeClicked(hovered);
                else if (DragPressed()) BeginDrag(hovered, r);
            }
        }

        void BeginDrag(MovieGraphNode n, Ray r)
        {
            dragged = n;
            dragDistance = Vector3.Distance(r.origin, n.transform.position);
            n.isPinned = true;
            MovieGraphFeedback.Instance?.Haptic(0.35f, 0.05f);
        }

        void EndDrag()
        {
            // Un nœud lâché reste épinglé là où on l'a posé ; les autres s'organisent autour.
            dragged.isPinned = true;
            dragged = null;
        }

        void DrawRay(Vector3 a, Vector3 b)
        {
            if (!UseVR) { ray.enabled = false; return; }
            ray.enabled = true;
            ray.SetPosition(0, a);
            ray.SetPosition(1, b);
        }

        bool GetRay(out Ray r)
        {
            if (UseVR)
            {
                r = new Ray(rayOrigin.position, rayOrigin.forward);
                return true;
            }

            var cam = Camera.main;
            if (cam == null || Mouse.current == null) { r = default; return false; }
            r = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return true;
        }

        bool ClickPressed() => UseVR
            ? OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
            : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        bool DragPressed() => UseVR
            ? OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)
            : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        bool DragReleased() => UseVR
            ? OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch)
            : Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
    }
}
