using UnityEngine;

namespace MovieGraph
{
    /// <summary>
    /// Ambiance "constellation" : fond noir profond, champ d'étoiles lointain,
    /// nébuleuses très douces et poussière dorée qui flotte autour du graphe.
    /// Remplace le skybox au lancement (la scène n'est pas modifiée).
    /// </summary>
    public class MovieGraphEnvironment : MonoBehaviour
    {
        public Color backgroundColor = new Color(0.008f, 0.008f, 0.02f);

        [Header("Étoiles lointaines")]
        public int starCount = 1800;
        public float starRadius = 40f;

        [Header("Nébuleuses")]
        public int nebulaCount = 10;
        public Color nebulaA = new Color(0.35f, 0.2f, 0.6f, 0.06f);
        public Color nebulaB = new Color(0.1f, 0.35f, 0.55f, 0.05f);

        [Header("Poussière dorée autour du graphe")]
        public int dustCount = 350;
        public float dustRadius = 3f;

        void Start()
        {
            ApplyBackground();

            CreateStars();
            CreateNebulae();
            CreateDust();
        }

        void ApplyBackground()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.08f);
            RenderSettings.fog = false;

            foreach (var cam in Camera.allCameras)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
            }
        }

        void LateUpdate()
        {
            // Les caméras du rig peuvent être (ré)activées après Start
            var cam = Camera.main;
            if (cam != null && cam.clearFlags != CameraClearFlags.SolidColor)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
            }
        }

        ParticleSystem NewSystem(string name, Transform parent, int max, bool worldSpace)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.maxParticles = max;
            main.playOnAwake = false;
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = MovieGraphVisuals.NewSpriteMaterial(MovieGraphVisuals.SoftCircle);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void CreateStars()
        {
            var ps = NewSystem("Stars", transform, starCount, false);
            var main = ps.main;
            main.startLifetime = Mathf.Infinity;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.85f, 1f, 0.9f), new Color(1f, 0.9f, 0.75f, 1f));

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = starRadius;
            shape.radiusThickness = 0.15f;

            ps.Play();
            ps.Emit(starCount);
        }

        void CreateNebulae()
        {
            var ps = NewSystem("Nebulae", transform, nebulaCount, false);
            var main = ps.main;
            main.startLifetime = Mathf.Infinity;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(14f, 26f);
            main.startColor = new ParticleSystem.MinMaxGradient(nebulaA, nebulaB);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = starRadius * 0.75f;
            shape.radiusThickness = 0.1f;

            ps.GetComponent<ParticleSystemRenderer>().sortingOrder = -10;
            ps.Play();
            ps.Emit(nebulaCount);
        }

        void CreateDust()
        {
            var graph = FindFirstObjectByType<MovieGraphManager>();
            Transform anchor = graph != null ? graph.transform : transform;

            var ps = NewSystem("GoldenDust", anchor, dustCount, true);
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.03f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.5f, 0.8f), new Color(0.7f, 0.8f, 1f, 0.6f));
            main.prewarm = true;

            var emission = ps.emission;
            emission.rateOverTime = dustCount / 17f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = dustRadius;

            // Apparition / disparition en douceur
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                        new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            // Dérive organique
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.04f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.05f;

            ps.Play();
        }
    }
}
