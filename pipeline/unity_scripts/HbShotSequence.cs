// HbShotSequence.cs — the camera choreography, morph timing and narration for one tutorial track,
// as a list of keys on the sequence's own clock. Between two keys the camera, fov, morph t and
// Fe exaggeration ease in/out; a key marked "cut" jumps instead. A key with an AudioClip plays it.
using System.Collections.Generic;
using UnityEngine;

namespace HbTutorial
{
    public class HbShotSequence : MonoBehaviour
    {
        [System.Serializable]
        public class Key
        {
            public string label;
            public float time;
            public Vector3 camPos;
            public Vector3 lookAt;
            public Vector3 up;                     // camera up; zero = world up (use the heme normal for heme shots)
            public float fov = 40f;
            [Range(0f, 1f)] public float t;
            public float feExaggeration = 1f;
            public bool cut;                       // hold the previous key, then jump to this one at its time
            public bool chains = true, ghost, ionPairs = true, hemes = true, residues = true, o2 = true, sickle;
            [Range(0f, 1f)] public float chainsAlpha = 1f;   // chains=false counts as alpha 0, so fades run between keys
            public float ChainAlpha { get { return chains ? chainsAlpha : 0f; } }
            public AudioClip audio;                // narration for the shot that starts at this key
        }

        public HbMorphController morph;
        public Camera cam;
        public AudioSource audioSource;
        public HbPanelOverlay panels;              // optional 2D panels that follow the sequence clock
        public List<Key> keys = new List<Key>();
        public bool playOnStart = true;
        public bool active = true;                 // false while the student is in free-look
        public bool playing;
        public float time;

        int _lastKeyIndex = -1;

        public float Duration { get { return keys.Count > 0 ? keys[keys.Count - 1].time : 0f; } }

        void Start()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (Application.isPlaying && playOnStart) { Seek(0f); Play(); }
        }

        public void Play()
        {
            playing = true; active = true;
            if (audioSource != null && audioSource.clip != null && !audioSource.isPlaying) audioSource.UnPause();
        }

        public void Pause()
        {
            playing = false;
            if (audioSource != null) audioSource.Pause();
        }

        public void Seek(float t)
        {
            time = Mathf.Clamp(t, 0f, Duration);
            _lastKeyIndex = -1;
            if (audioSource != null) audioSource.Stop();
            Apply(time);
        }

        void LateUpdate()
        {
            if (!active || keys.Count == 0) { if (panels != null) panels.UpdateAt(-1f); return; }
            if (Application.isPlaying && playing)
            {
                time += Time.deltaTime;
                if (time >= Duration) { time = Duration; playing = false; }
            }
            Apply(time);
        }

        public int KeyIndexAt(float tm)
        {
            int i = 0;
            while (i + 1 < keys.Count && keys[i + 1].time <= tm) i++;
            return i;
        }

        public Key KeyAt(float tm) { return keys.Count > 0 ? keys[KeyIndexAt(tm)] : null; }

        public void Apply(float tm)
        {
            if (keys.Count == 0 || morph == null) return;
            int i = KeyIndexAt(tm);
            Key a = keys[i];
            Key b = i + 1 < keys.Count ? keys[i + 1] : a;
            float u = 0f;
            if (b != a && !b.cut && b.time > a.time) u = Mathf.Clamp01((tm - a.time) / (b.time - a.time));
            float s = u * u * (3f - 2f * u);

            if (panels != null) panels.UpdateAt(tm);   // first, so the camera can make room for the card
            if (cam != null)
            {
                Vector3 p = Vector3.Lerp(a.camPos, b.camPos, s);
                Vector3 l = Vector3.Lerp(a.lookAt, b.lookAt, s);
                Vector3 ua = a.up.sqrMagnitude > 1e-6f ? a.up.normalized : Vector3.up;
                Vector3 ub = b.up.sqrMagnitude > 1e-6f ? b.up.normalized : Vector3.up;
                Vector3 up = Vector3.Slerp(ua, ub, s);
                float fov = Mathf.Lerp(a.fov, b.fov, s);
                // while a card is up, slide camera and target sideways so the subject sits in the free part of the frame
                float shift = panels != null ? panels.Shift * panels.reframe : 0f;
                if (Mathf.Abs(shift) > 1e-4f && (l - p).sqrMagnitude > 1e-6f)
                {
                    Vector3 fwd = (l - p).normalized, right = Vector3.Cross(up, fwd).normalized;
                    float halfW = (l - p).magnitude * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * cam.aspect;
                    Vector3 d = right * halfW * shift;
                    p += d; l += d;
                }
                cam.transform.position = p;
                if ((l - p).sqrMagnitude > 1e-6f) cam.transform.LookAt(l, up);
                cam.fieldOfView = fov;
            }
            morph.t = Mathf.Lerp(a.t, b.t, s);
            morph.feExaggeration = Mathf.Lerp(a.feExaggeration, b.feExaggeration, s);
            float ca = Mathf.Lerp(a.ChainAlpha, b.ChainAlpha, s);
            morph.showChains = ca > 0.004f; morph.chainsAlpha = ca; morph.showGhost = a.ghost; morph.showIonPairs = a.ionPairs; morph.showHemes = a.hemes;
            morph.showResidues = a.residues; morph.showO2 = a.o2; morph.showSickle = a.sickle;
            morph.Apply();

            // narration: start the clip of the key we just entered, offset if we seeked into the middle of it
            if (i != _lastKeyIndex)
            {
                _lastKeyIndex = i;
                if (Application.isPlaying && playing && audioSource != null && a.audio != null)
                {
                    audioSource.clip = a.audio;
                    audioSource.time = Mathf.Clamp(tm - a.time, 0f, Mathf.Max(0f, a.audio.length - 0.05f));
                    audioSource.Play();
                }
            }
        }

#if UNITY_EDITOR
        void OnValidate() { if (!Application.isPlaying && active) Apply(time); }
#endif
    }
}
