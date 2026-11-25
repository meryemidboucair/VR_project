using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Shared.Events;

namespace MovieGraph
{
    /// <summary>Navigateur intégré (UnityWebBrowser / Chromium) dont l'image est affichée sur un quad 3D.</summary>
    public class TrailerBrowser : BaseUwbClientManager
    {
        public Renderer target;

        protected override void OnStart()
        {
            if (browserClient.BrowserTexture == null || target == null) return;
            var m = target.material;
            m.mainTexture = browserClient.BrowserTexture;
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", browserClient.BrowserTexture);
                // La texture du navigateur arrive à l'envers : on la retourne verticalement
                m.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
                m.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
            }
            m.mainTextureScale = new Vector2(1f, -1f);
            m.mainTextureOffset = new Vector2(0f, 1f);
            MovieGraphVisuals.SetColor(m, Color.white);
        }
    }

    /// <summary>
    /// Mini-serveur web local (http://localhost:8787) qui sert une page contenant le lecteur officiel YouTube.
    /// Pourquoi : YouTube refuse son lecteur intégré quand il n'est pas dans une vraie page web (« Erreur 153 »),
    /// et la page YouTube complète est trop lourde (elle fige Unity). Une petite page locale règle les deux.
    /// </summary>
    public static class TrailerPageServer
    {
        public const int Port = 8787;
        static HttpListener listener;
        static Thread thread;

        public static string UrlFor(string videoKey) => $"http://localhost:{Port}/trailer?v={Uri.EscapeDataString(videoKey)}";

        public static bool EnsureRunning()
        {
            if (listener != null && listener.IsListening) return true;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{Port}/");
                listener.Start();
                thread = new Thread(Loop) { IsBackground = true, Name = "TrailerPageServer" };
                thread.Start();
                Application.quitting += Stop;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MovieGraph] Serveur local des bandes-annonces impossible sur le port {Port} : {e.Message}");
                listener = null;
                return false;
            }
        }

        public static void Stop()
        {
            try { listener?.Stop(); listener?.Close(); } catch { /* déjà arrêté */ }
            listener = null;
        }

        static void Loop()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { break; }

                try
                {
                    string key = ctx.Request.QueryString["v"] ?? "";
                    // Clé YouTube : lettres, chiffres, - et _ uniquement
                    var safe = new StringBuilder();
                    foreach (char ch in key) if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') safe.Append(ch);

                    byte[] body = Encoding.UTF8.GetBytes(ctx.Request.Url.AbsolutePath == "/trailer" ? Page(safe.ToString()) : "ok");
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                    ctx.Response.OutputStream.Close();
                }
                catch { /* client déconnecté */ }
            }
        }

        /// <summary>
        /// Page HTML : lecteur YouTube plein cadre (API IFrame officielle).
        /// Elle renvoie son état à Unity toutes les secondes via uwb.ExecuteJsMethod('TrailerState', enPause, etat).
        /// États YouTube : -1 non démarré, 0 terminé, 1 lecture, 2 pause, 3 chargement, 5 prêt ; erreurs : -100 - code.
        /// </summary>
        static string Page(string key) => @"<!doctype html>
<html><head><meta charset='utf-8'>
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{position:fixed;inset:0;width:100vw;height:100vh}</style>
</head><body><div id='p'></div>
<script>
var player = null, state = -1;
function report(){ try { uwb.ExecuteJsMethod('TrailerState', state !== 1, state); } catch (e) {} }
function onYouTubeIframeAPIReady(){
  player = new YT.Player('p', {
    width: '100%', height: '100%', videoId: 'KEY',
    playerVars: { autoplay: 1, controls: 1, rel: 0, playsinline: 1, modestbranding: 1, iv_load_policy: 3, hl: 'fr', fs: 0, origin: location.origin },
    events: {
      onReady: function(e){ state = 5; e.target.unMute(); e.target.setVolume(100); e.target.playVideo(); report(); },
      onStateChange: function(e){ state = e.data; report(); },
      onError: function(e){ state = -100 - e.data; report(); }
    }
  });
}
window.uwbToggle = function(){ if (!player) return; if (state === 1) player.pauseVideo(); else player.playVideo(); };
setInterval(report, 1000);
</script>
<script src='https://www.youtube.com/iframe_api'></script>
</body></html>".Replace("KEY", key);
    }

    /// <summary>
    /// Écran de cinéma flottant qui lit la bande-annonce YouTube du film dans la scène VR.
    /// Gâchette sur l'écran : pause / lecture.   B : fermer.
    /// Fonctionne dans l'éditeur / PC Windows (Link). Hors Windows, ouvre la vidéo dans le navigateur.
    /// </summary>
    public class MovieGraphTrailer : MonoBehaviour
    {
        const float Width = 1.6f, Height = 0.9f;        // 16:9
        const int ResW = 1920, ResH = 1080;             // résolution par défaut du navigateur

        public bool IsOpen => !closing;
        public MovieGraphNode Movie { get; private set; }

        TrailerBrowser browser;
        Transform screen;
        Renderer border, videoQuad;
        TextMeshPro caption, status;

        List<string> keys = new List<string>();
        int keyIndex;

        float appear;
        bool closing;
        bool hovered;

        // État remonté par la page (lecteur YouTube)
        int ytState = -1;
        bool stateReceived, userPaused;
        int autoClicks;
        float pageStartedAt = -1f, nextPoll, nextHeartbeat;
        volatile bool engineConnected;

        bool Playing => ytState == 1;

        public static MovieGraphTrailer Open(TmdbClient client, MovieGraphNode movie, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Trailer_" + movie.title);
            go.transform.SetPositionAndRotation(position, rotation);
            var t = go.AddComponent<MovieGraphTrailer>();
            t.Movie = movie;
            t.Build();
            t.StartCoroutine(t.Load(client));
            return t;
        }

        // ================================================================== Construction

        void Build()
        {
            screen = new GameObject("Screen").transform;
            screen.SetParent(transform, false);
            screen.localScale = Vector3.zero;

            var glow = MovieGraphVisuals.CreateSpriteQuad("Glow", screen, MovieGraphVisuals.SoftCircle,
                new Color(1f, 0.78f, 0.32f, 0.18f), 1f, 0.03f);
            glow.transform.localScale = new Vector3(Width * 1.7f, Height * 2.2f, 1f);

            border = MovieGraphVisuals.CreateSpriteQuad("Border", screen, null, new Color(1f, 0.82f, 0.4f, 0.9f), 1f, 0.02f);
            border.transform.localScale = new Vector3(Width + 0.03f, Height + 0.03f, 1f);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Video";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(screen, false);
            quad.transform.localScale = new Vector3(Width, Height, 1f);
            videoQuad = quad.GetComponent<Renderer>();
            videoQuad.material = MovieGraphVisuals.NewUnlitMaterial(Color.black);

            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(Width, Height, 0.05f);

            caption = NewText("Caption", new Vector3(0, -Height / 2f - 0.07f, 0), 0.55f);
            caption.text = $"<b>{Movie.title}</b>  <color=#BBBBBB>{Movie.subtitle}</color>";

            status = NewText("Status", new Vector3(0, -Height / 2f - 0.17f, 0), 0.36f);
            status.text = "<color=#AAAAAA>Recherche de la bande-annonce...</color>";
        }

        TextMeshPro NewText(string name, Vector3 pos, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(screen, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Top;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.sizeDelta = new Vector2(Width * 1.1f, 0.2f);
            t.outlineWidth = 0.2f;
            t.outlineColor = Color.black;
            return t;
        }

        // ================================================================== Chargement

        IEnumerator Load(TmdbClient client)
        {
            yield return client.GetTrailerKeys(Movie.tmdbId, k => keys = k ?? new List<string>());
            if (this == null || closing) yield break;

            if (keys.Count == 0)
            {
                SetStatus("<color=#FF9988>Pas de bande-annonce disponible pour ce film</color>");
                MovieGraphFeedback.Instance?.Error(transform.position);
                yield break;
            }

            Debug.Log($"[MovieGraph] Bande-annonce « {Movie.title} » : {keys.Count} vidéo(s), 1re = https://www.youtube.com/watch?v={keys[0]}");

            bool windows = Application.platform == RuntimePlatform.WindowsEditor ||
                           Application.platform == RuntimePlatform.WindowsPlayer;
            if (!windows || !TrailerPageServer.EnsureRunning())
            {
                SetStatus("<color=#FFD36B>Ouverture dans le navigateur...</color>");
                Application.OpenURL("https://www.youtube.com/watch?v=" + keys[0]);
                yield break;
            }

            var engine = Resources.Load<Engine>("Cef Engine Configuration");
            var comms = Resources.Load<CommunicationLayer>("TCP Communication Layer");
            if (engine == null || comms == null)
            {
                SetStatus("<color=#FF9988>Lecteur intégré introuvable (package UnityWebBrowser)</color>");
                Debug.LogError("[MovieGraph] Assets UnityWebBrowser introuvables (Cef Engine Configuration / TCP Communication Layer).");
                yield break;
            }

            SetStatus("<color=#AAAAAA>Chargement du lecteur...</color>");

            // Le navigateur démarre au Start() du composant : on le configure juste après l'ajout
            var bgo = new GameObject("Browser");
            bgo.transform.SetParent(transform, false);
            browser = bgo.AddComponent<TrailerBrowser>();
            browser.target = videoQuad;
            var c = browser.browserClient;
            c.engine = engine;
            c.communicationLayer = comms;
            c.initialUrl = TrailerPageServer.UrlFor(keys[0]);
            c.backgroundColor = new Color32(0, 0, 0, 255);
            c.windowlessFrameRate = 30;
            c.engineStartupTimeout = 15000;
            c.jsMethodManager.jsMethodsEnable = true;
            c.RegisterJsMethod<bool, int>("TrailerState", OnTrailerState);
            // Cet événement arrive sur un autre thread : on ne lit pas l'horloge Unity ici, PollPlayer s'en charge
            c.OnClientConnected += () => { engineConnected = true; Debug.Log("[MovieGraph] Lecteur : moteur Chromium connecté"); };
            c.OnLoadFinish += url => Debug.Log($"[MovieGraph] Lecteur : page chargée {url}");
        }

        void OnTrailerState(bool paused, int state)
        {
            bool changed = !stateReceived || state != ytState;
            stateReceived = true;
            ytState = state;
            if (changed) Debug.Log($"[MovieGraph] Lecteur : état YouTube = {StateName(state)} ({state})");

            // Vidéo refusée (intégration interdite, introuvable...) : on essaie la bande-annonce suivante
            if (state <= -100 && keyIndex + 1 < keys.Count)
            {
                keyIndex++;
                Debug.Log($"[MovieGraph] Lecteur : vidéo refusée, essai de la suivante ({keys[keyIndex]})");
                LoadKey(keys[keyIndex]);
            }
        }

        void LoadKey(string key)
        {
            if (browser == null || !browser.browserClient.IsConnected) return;
            stateReceived = false;
            ytState = -1;
            autoClicks = 0;
            pageStartedAt = Time.unscaledTime;
            browser.browserClient.LoadUrl(TrailerPageServer.UrlFor(key));
        }

        static string StateName(int s)
        {
            switch (s)
            {
                case -1: return "non démarrée";
                case 0: return "terminée";
                case 1: return "lecture";
                case 2: return "pause";
                case 3: return "chargement";
                case 5: return "prête";
                default: return s <= -100 ? $"erreur {-(s + 100)}" : "?";
            }
        }

        /// <summary>Vrai clic de souris au centre du lecteur (compte comme un geste utilisateur → son autorisé).</summary>
        void ClickCenter()
        {
            var c = browser != null ? browser.browserClient : null;
            if (c == null || !c.IsConnected) return;
            var pos = new Vector2(ResW / 2f, ResH / 2f);
            c.SendMouseMove(pos);
            c.SendMouseClick(pos, 1, MouseClickType.Left, MouseEventType.Down);
            c.SendMouseClick(pos, 1, MouseClickType.Left, MouseEventType.Up);
        }

        void SetStatus(string text) { if (status != null) status.text = text + "\n<color=#888888>B : fermer l'écran</color>"; }

        // ================================================================== Interaction

        public void SetHovered(bool on) => hovered = on;

        /// <summary>Gâchette sur l'écran : pause / lecture.</summary>
        public void TogglePlay()
        {
            if (browser == null || !browser.browserClient.IsConnected) return;
            userPaused = Playing;   // si ça jouait, l'utilisateur met en pause
            ClickCenter();          // un clic sur le lecteur YouTube bascule pause / lecture
            MovieGraphFeedback.Instance?.Haptic(0.3f, 0.04f);
        }

        public void Close()
        {
            if (closing) return;
            closing = true;
            // Arrête tout de suite le navigateur (son coupé, processus Chromium fermé)
            if (browser != null)
            {
                try { if (browser.browserClient.IsConnected) browser.browserClient.AudioMute(true); } catch { /* déjà fermé */ }
                Destroy(browser.gameObject);
                browser = null;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            appear = Mathf.MoveTowards(appear, closing ? 0f : 1f, dt * (closing ? 4f : 2.5f));
            screen.localScale = Vector3.one * (closing ? appear : EaseOutBack(appear));
            if (closing && appear <= 0f) { Destroy(gameObject); return; }

            // Toujours face à l'utilisateur
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 dir = transform.position - cam.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * 3f);
            }

            border.material.color = hovered
                ? new Color(1f, 0.92f, 0.6f, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f))
                : new Color(1f, 0.82f, 0.4f, 0.9f);

            PollPlayer();
        }

        /// <summary>Une fois par seconde : démarre la vidéo avec le son si elle est prête mais en pause.</summary>
        void PollPlayer()
        {
            if (closing || browser == null) return;
            float now = Time.unscaledTime;
            if (pageStartedAt < 0f)
            {
                if (!engineConnected) return;
                pageStartedAt = now;   // départ du chrono, sur le thread principal
            }
            if (now < nextPoll) return;
            nextPoll = now + 1f;

            var c = browser.browserClient;
            if (!c.IsConnected) return;
            float since = now - pageStartedAt;

            if (now >= nextHeartbeat)
            {
                nextHeartbeat = now + 5f;
                Debug.Log($"[MovieGraph] Lecteur : t={since:0}s, état reçu={stateReceived}, état={StateName(ytState)}, clics={autoClicks}");
            }

            // Le lecteur est prêt mais ne joue pas (lecture automatique avec son bloquée) :
            // un vrai clic au centre la lance. Jamais si l'utilisateur a mis en pause lui-même.
            bool readyButStopped = stateReceived && (ytState == 5 || ytState == -1 || ytState == 2);
            if (readyButStopped && !userPaused && autoClicks < 3 && since > 3f + autoClicks * 3f)
            {
                autoClicks++;
                Debug.Log($"[MovieGraph] Lecteur : clic de lecture n°{autoClicks} (état={StateName(ytState)})");
                ClickCenter();
            }

            string text =
                !stateReceived ? "<color=#AAAAAA>Chargement de la bande-annonce...</color>"
                : ytState <= -100 ? "<color=#FF9988>Cette bande-annonce ne peut pas être lue ici</color>"
                : Playing ? "<color=#FFD36B>Lecture - gâchette sur l'écran pour mettre en pause</color>"
                : ytState == 3 ? "<color=#AAAAAA>Chargement de la vidéo...</color>"
                : ytState == 0 ? "<color=#FFD36B>Terminé - gâchette sur l'écran pour revoir</color>"
                : "<color=#FFD36B>En pause - gâchette sur l'écran pour lire</color>";
            SetStatus(text);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
