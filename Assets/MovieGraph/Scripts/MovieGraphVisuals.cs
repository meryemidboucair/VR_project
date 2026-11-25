using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MovieGraph
{
    /// <summary>
    /// Textures procédurales + matériaux partagés (aucun asset à importer).
    /// Tout repose sur "Sprites/Default" (toujours inclus dans les builds) et l'Unlit URP pour les affiches.
    /// </summary>
    public static class MovieGraphVisuals
    {
        // Palette "constellation"
        public static readonly Color Gold = new Color(1f, 0.78f, 0.32f);
        public static readonly Color GoldBright = new Color(1f, 0.9f, 0.55f);
        public static readonly Color MovieTint = new Color(0.55f, 0.75f, 1f);   // étoile froide
        public static readonly Color PersonTint = new Color(1f, 0.72f, 0.45f);  // étoile chaude

        static Texture2D softCircle;
        static Texture2D glowLine;
        static Texture2D ring;
        static Material spriteMat;
        static Shader unlitShader;

        // ------------------------------------------------------------ Polices de secours (coréen, chinois, japonais...)

        static readonly List<TMP_FontAsset> addedFallbacks = new List<TMP_FontAsset>();

        /// <summary>
        /// Ajoute des polices système (Windows et Android/Quest) en secours de la police TMP par défaut,
        /// pour afficher les noms en coréen, chinois, japonais, cyrillique... au lieu de carrés.
        /// Retiré à la fin du Play pour ne pas modifier l'asset de police du projet.
        /// </summary>
        public static void EnsureFontFallbacks()
        {
            if (addedFallbacks.Count > 0) return;
            var main = TMP_Settings.defaultFontAsset;
            if (main == null) return;
            if (main.fallbackFontAssetTable == null) main.fallbackFontAssetTable = new List<TMP_FontAsset>();

            string[] families =
            {
                // Windows
                "Malgun Gothic", "Microsoft YaHei", "Yu Gothic", "Segoe UI",
                // Android / Quest
                "Noto Sans CJK KR", "Noto Sans CJK SC", "Noto Sans CJK JP", "Noto Sans", "Roboto",
            };

            foreach (var family in families)
            {
                TMP_FontAsset fa = null;
                try { fa = TMP_FontAsset.CreateFontAsset(family, "Regular"); }
                catch { /* police absente sur cette plateforme */ }
                if (fa == null) continue;
                fa.name = "Fallback_" + family;
                main.fallbackFontAssetTable.Add(fa);
                addedFallbacks.Add(fa);
            }
            Debug.Log($"[MovieGraph] Polices de secours : {addedFallbacks.Count} ajoutée(s).");
        }

        public static void RemoveFontFallbacks()
        {
            var main = TMP_Settings.defaultFontAsset;
            if (main != null && main.fallbackFontAssetTable != null)
                foreach (var fa in addedFallbacks) main.fallbackFontAssetTable.Remove(fa);
            addedFallbacks.Clear();
        }

        /// <summary>Disque flou (halo, étoiles, poussière).</summary>
        public static Texture2D SoftCircle
        {
            get
            {
                if (softCircle != null) return softCircle;
                const int s = 64;
                softCircle = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.6f + 0.4f * a);          // cœur lumineux, bord très doux
                    softCircle.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                softCircle.Apply();
                return softCircle;
            }
        }

        /// <summary>Anneau fin (onde de choc au clic).</summary>
        public static Texture2D Ring
        {
            get
            {
                if (ring != null) return ring;
                const int s = 64;
                ring = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.12f);
                    ring.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
                ring.Apply();
                return ring;
            }
        }

        /// <summary>Profil de ligne lumineuse : cœur brillant, bords qui s'estompent (axe V = largeur).</summary>
        public static Texture2D GlowLine
        {
            get
            {
                if (glowLine != null) return glowLine;
                const int h = 32;
                glowLine = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);   // 0 au centre, 1 au bord
                    float core = Mathf.Clamp01(1f - v / 0.25f);
                    float glow = Mathf.Pow(1f - v, 2.2f) * 0.55f;
                    float a = Mathf.Clamp01(core + glow);
                    for (int x = 0; x < 4; x++) glowLine.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                glowLine.Apply();
                return glowLine;
            }
        }

        /// <summary>Nouveau matériau transparent teinté par couleur de vertex / _Color.</summary>
        public static Material NewSpriteMaterial(Texture tex)
        {
            if (spriteMat == null) spriteMat = new Material(Shader.Find("Sprites/Default"));
            var m = new Material(spriteMat) { mainTexture = tex };
            return m;
        }

        /// <summary>Matériau opaque non éclairé (affiches).</summary>
        public static Material NewUnlitMaterial(Color c)
        {
            if (unlitShader == null)
                unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var m = new Material(unlitShader);
            SetColor(m, c);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>Quad "sprite" sans collider, enfant de parent.</summary>
        public static Renderer CreateSpriteQuad(string name, Transform parent, Texture tex, Color color, float size, float z)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.material = NewSpriteMaterial(tex);
            r.material.color = color;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }
}
