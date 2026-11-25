using System.Collections;
using UnityEngine;

namespace MovieGraph
{
    /// <summary>
    /// Retours sensoriels : sons spatialisés générés procéduralement (aucun fichier audio)
    /// + vibrations des manettes Quest.
    /// </summary>
    public class MovieGraphFeedback : MonoBehaviour
    {
        public static MovieGraphFeedback Instance { get; private set; }

        [Range(0f, 1f)] public float volume = 0.6f;

        AudioClip hoverClip, openClip, closeClip, closeAllClip, errorClip;
        const int Rate = 44100;

        void Awake()
        {
            Instance = this;
            hoverClip = Tone("hover", 0.035f, 2400f, 2400f, 0.18f, 60f);
            openClip = Chime("open", 0.45f, new[] { 523.25f, 783.99f, 1046.5f });         // do-sol-do : ça s'ouvre
            closeClip = Chime("close", 0.35f, new[] { 1046.5f, 783.99f });                // descend : ça se ferme
            closeAllClip = Chime("closeAll", 0.8f, new[] { 1318.5f, 1046.5f, 783.99f, 523.25f });
            errorClip = Tone("error", 0.18f, 220f, 180f, 0.4f, 12f);
        }

        // ---------------------------------------------------------------- API

        public void Hover(Vector3 pos) { Play(hoverClip, pos, 0.35f); Haptic(0.15f, 0.02f); }
        public void Open(Vector3 pos) { Play(openClip, pos, 1f); Haptic(0.6f, 0.08f); }
        public void Close(Vector3 pos) { Play(closeClip, pos, 0.9f); Haptic(0.4f, 0.06f); }
        public void CloseAll(Vector3 pos) { Play(closeAllClip, pos, 1f); Haptic(0.9f, 0.2f); }
        public void Error(Vector3 pos) { Play(errorClip, pos, 0.7f); Haptic(0.3f, 0.15f); }

        // ---------------------------------------------------------------- Son

        void Play(AudioClip clip, Vector3 pos, float vol)
        {
            if (clip == null) return;
            var go = new GameObject("Sfx_" + clip.name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = vol * volume;
            src.spatialBlend = 0.8f;          // spatialisé, mais toujours audible
            src.minDistance = 0.5f;
            src.maxDistance = 8f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.pitch = Random.Range(0.97f, 1.03f);
            src.Play();
            Destroy(go, clip.length + 0.1f);
        }

        static AudioClip Tone(string name, float duration, float f0, float f1, float amp, float decay)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(f0, f1, t);
                ph += 2 * Mathf.PI * f / Rate;
                float env = Mathf.Exp(-t * decay * duration * 10f) * Mathf.Clamp01(i / (Rate * 0.002f));
                data[i] = Mathf.Sin((float)ph) * amp * env;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Petite mélodie cristalline (notes successives avec harmoniques et queue douce).</summary>
        static AudioClip Chime(string name, float duration, float[] notes)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            float step = duration / (notes.Length + 1.5f);

            for (int k = 0; k < notes.Length; k++)
            {
                int start = Mathf.FloorToInt(k * step * Rate);
                float f = notes[k];
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t / 0.003f);
                    float s = Mathf.Sin(2 * Mathf.PI * f * t)
                            + 0.35f * Mathf.Sin(2 * Mathf.PI * f * 2f * t)
                            + 0.12f * Mathf.Sin(2 * Mathf.PI * f * 3.01f * t);
                    data[i] += s * env * 0.16f;
                }
            }
            // fondu de fin
            for (int i = 0; i < n; i++)
                data[i] *= Mathf.Clamp01((n - i) / (Rate * 0.03f));

            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------------------------------------------------------- Haptique

        Coroutine hapticRoutine;

        public void Haptic(float amplitude, float seconds, OVRInput.Controller ctrl = OVRInput.Controller.RTouch)
        {
            if (!OVRManager.isHmdPresent) return;
            if (hapticRoutine != null) StopCoroutine(hapticRoutine);
            hapticRoutine = StartCoroutine(HapticRoutine(amplitude, seconds, ctrl));
        }

        IEnumerator HapticRoutine(float amplitude, float seconds, OVRInput.Controller ctrl)
        {
            OVRInput.SetControllerVibration(0.6f, amplitude, ctrl);
            yield return new WaitForSeconds(seconds);
            OVRInput.SetControllerVibration(0f, 0f, ctrl);
            hapticRoutine = null;
        }

        void OnDisable()
        {
            if (OVRManager.isHmdPresent) OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}
