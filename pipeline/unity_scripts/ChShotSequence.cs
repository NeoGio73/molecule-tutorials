// ChShotSequence.cs — the camera choreography and mechanism timing for the chymotrypsin tutorial, as a list of
// keys on the sequence's own clock (same idea as HbShotSequence). Between two keys the camera, fov, mechanism
// step, arrow draw-on and highlights ease in/out; a key marked "cut" jumps instead. A key with an AudioClip plays it.
using System.Collections.Generic;
using UnityEngine;
using HbTutorial;

namespace ChTutorial
{
    [ExecuteAlways]
    public class ChShotSequence : MonoBehaviour
    {
        [System.Serializable]
        public class Key
        {
            public string label;
            public float time;
            public Vector3 camPos;
            public Vector3 lookAt;
            public Vector3 up;                       // zero = world up
            public float fov = 35f;
            [Range(0f, 7f)] public float step;       // mechanism state (0 free enzyme ... 7 free enzyme)
            [Range(0f, 1f)] public float arrows;     // draw-on of that state's curved arrows
            [Range(0f, 1f)] public float lbhb;       // Asp102-His57 hydrogen bond highlight
            [Range(0f, 1f)] public float hole;       // oxyanion-hole highlight
            [Range(0f, 1f)] public float chainsAlpha = 1f;
            [Range(0f, 1f)] public float pocketAlpha = 1f;   // the hydrophobic-pocket surface
            public bool pocket;                      // hydrophobic-pocket sticks (off by default; the surface stands for them)
            public string labels = "";               // label groups shown, comma separated: triad, hole, pocket, scissile, water
            public bool cut;                         // hold the previous key, then jump to this one at its time
            public AudioClip audio;                  // narration for the shot that starts at this key
        }

        public ChSceneBuilder builder;
        public Camera cam;
        public AudioSource audioSource;
        public HbPanelOverlay panels;                // 2D panels that follow the sequence clock
        public List<Key> keys = new List<Key>();
        public bool playOnStart = true;
        public bool active = true;                   // false while the student is in free look
        public bool playing;
        public float time;

        int _lastKeyIndex = -1;
        float _lastAppliedTime = -1f;

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
        public void Pause() { playing = false; if (audioSource != null) audioSource.Pause(); }
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
            if (Application.isPlaying || !Mathf.Approximately(time, _lastAppliedTime)) Apply(time);
        }

        public int KeyIndexAt(float tm)
        {
            int i = 0;
            while (i + 1 < keys.Count && keys[i + 1].time <= tm) i++;
            return i;
        }

        public void Apply(float tm)
        {
            if (keys.Count == 0) return;
            _lastAppliedTime = tm;
            int i = KeyIndexAt(tm);
            Key a = keys[i];
            Key b = i + 1 < keys.Count ? keys[i + 1] : a;
            float u = 0f;
            if (b != a && !b.cut && b.time > a.time) u = Mathf.Clamp01((tm - a.time) / (b.time - a.time));
            float s = u * u * (3f - 2f * u);

            if (panels != null) panels.UpdateAt(tm);
            if (cam != null)
            {
                Vector3 p = Vector3.Lerp(a.camPos, b.camPos, s);
                Vector3 l = Vector3.Lerp(a.lookAt, b.lookAt, s);
                Vector3 ua = a.up.sqrMagnitude > 1e-6f ? a.up.normalized : Vector3.up;
                Vector3 ub = b.up.sqrMagnitude > 1e-6f ? b.up.normalized : Vector3.up;
                Vector3 up = Vector3.Slerp(ua, ub, s);
                float fov = Mathf.Lerp(a.fov, b.fov, s);
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
            if (builder != null)
            {
                var m = builder.mechanism;
                if (m != null)
                {
                    // step and arrows: linear in the eased parameter, so a state change and its arrows stay in sync
                    m.step = Mathf.Lerp(a.step, b.step, s);
                    m.arrows = Mathf.Lerp(a.arrows, b.arrows, s);
                    m.lbhb = Mathf.Lerp(a.lbhb, b.lbhb, s);
                    m.hole = Mathf.Lerp(a.hole, b.hole, s);
                    m.Apply();
                }
                builder.SetChainsAlpha(Mathf.Lerp(a.chainsAlpha, b.chainsAlpha, s));
                builder.SetPocketAlpha(Mathf.Lerp(a.pocketAlpha, b.pocketAlpha, s));
                if (builder.labels != null)
                {
                    foreach (var g in builder.labels.groupNames)
                        builder.labels.SetGroup(g, Mathf.Lerp(HasGroup(a.labels, g) ? 1f : 0f, HasGroup(b.labels, g) ? 1f : 0f, s));
                    builder.labels.Apply();
                }
                if (builder.pocketRoot != null && builder.pocketRoot.gameObject.activeSelf != a.pocket) builder.pocketRoot.gameObject.SetActive(a.pocket);
            }

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

        // ------------------------------------------------------------------ default shot list (storyboard, Nov 7)
        // Placeholder times until the narration is recorded. Camera poses come from the built scene's targets.
        [ContextMenu("Populate default shots")]
        public void PopulateDefaults()
        {
            if (builder == null || builder.targets == null || builder.mechanism == null) { Debug.LogError("ChShotSequence: build the scene first"); return; }
            var mech = builder.mechanism;
            Vector3 C = builder.Target("center").position, S = builder.Target("site").position, Pk = builder.Target("pocket").position;
            Vector3 outward = (S - C).normalized;
            // the close-up frame: look straight at the plane of Ser195 O, the scissile C and the pocket (ES complex), from
            // the open side of the cleft, with His57 and Ser195 up and the substrate down, as in the 2D panels
            Vector3 og = mech.WorldPos("Ser195:OG", 1), ne2 = mech.WorldPos("His57:NE2", 1), c2 = mech.WorldPos("S:C2", 1);
            Vector3 nrm = Vector3.Cross(og - c2, Pk - c2).normalized;
            if (Vector3.Dot(nrm, outward) < 0f) nrm = -nrm;
            Vector3 up = (og + ne2) * 0.5f - c2; up = (up - Vector3.Dot(up, nrm) * nrm).normalized;
            Vector3 triad = builder.Target("triad") != null ? builder.Target("triad").position : S;
            keys = new List<Key>();
            System.Func<string, float, Vector3, Vector3, Vector3, float, Key> K = (label, t, pos, look, upv, fov) =>
                new Key { label = label, time = t, camPos = pos, lookAt = look, up = upv, fov = fov, chainsAlpha = 1f, pocketAlpha = 0f };

            // 1 establishing orbit around the whole enzyme, ending on the cleft side
            float R0 = 7.0f;
            for (int k = 0; k <= 4; k++)
            {
                Vector3 dir = Quaternion.AngleAxis(-80f + 20f * k, Vector3.up) * nrm;
                keys.Add(K(k == 0 ? "1 Establishing" : "", 5f * k, C + dir * R0, C, Vector3.up, 35f));
            }
            // 2 into the cleft: the triad in the free enzyme
            var k2a = K("2 Into the cleft", 32f, triad + nrm * 2.6f, triad, up, 30f); k2a.chainsAlpha = 0.15f; keys.Add(k2a);
            var k2b = Clone(k2a, "", 38f); k2b.labels = "triad"; keys.Add(k2b);
            var k2c = Clone(k2b, "", 50f); keys.Add(k2c);
            // 3 the substrate arrives (free -> ES); the frame widens to take in the pocket
            Vector3 mid3 = (S + Pk) * 0.5f;
            var k3a = K("3 Substrate binds", 54f, mid3 + nrm * 3.4f, mid3, up, 30f); k3a.chainsAlpha = 0.15f; k3a.pocketAlpha = 1f; k3a.labels = "pocket"; keys.Add(k3a);
            var k3b = Clone(k3a, "", 66f); k3b.step = 1f; keys.Add(k3b);
            var k3c = Clone(k3b, "", 70f); k3c.labels = "pocket,scissile"; keys.Add(k3c);
            keys.Add(Clone(k3c, "", 80f));
            // 4 acylation: the low-barrier hydrogen bond, the three arrows, then the tetrahedral intermediate
            Vector3 cc, cd; float ce;
            ChemistryView(out cc, out cd, out ce);
            Vector3 cup = (og + ne2) * 0.5f - c2; cup = (cup - Vector3.Dot(cup, cd) * cd).normalized;
            float cdist = ce * 1.25f / Mathf.Tan(15f * Mathf.Deg2Rad);
            var k4a = K("4 Acylation", 84f, cc + cd * cdist, cc, cup, 30f); k4a.step = 1f; k4a.chainsAlpha = 0f; k4a.pocketAlpha = 0.45f; k4a.labels = "triad"; keys.Add(k4a);
            var k4b = Clone(k4a, "", 90f); k4b.lbhb = 1f; keys.Add(k4b);
            var k4c = Clone(k4b, "", 98f); k4c.arrows = 1f; keys.Add(k4c);
            keys.Add(Clone(k4c, "", 104f));
            var k4e = Clone(k4c, "", 114f); k4e.step = 2f; k4e.arrows = 0f; k4e.lbhb = 0.6f; keys.Add(k4e);
            keys.Add(Clone(k4e, "", 120f));
            // 5 hold on the intermediate: the oxyanion hole lights up
            var k5a = Clone(k4e, "5 Oxyanion hole", 124f); k5a.hole = 1f; k5a.lbhb = 0.3f; k5a.labels = "hole"; keys.Add(k5a);
            keys.Add(Clone(k5a, "", 140f));
            // 6 collapse: arrows, then the acyl-enzyme; the amine half leaves
            var k6a = Clone(k5a, "6 Collapse", 144f); k6a.hole = 0.3f; k6a.labels = "scissile"; keys.Add(k6a);
            var k6b = Clone(k6a, "", 150f); k6b.arrows = 1f; keys.Add(k6b);
            keys.Add(Clone(k6b, "", 154f));
            var k6d = Clone(k6b, "", 166f); k6d.step = 3f; k6d.arrows = 0f; k6d.hole = 0f; k6d.lbhb = 0f; keys.Add(k6d);
            keys.Add(Clone(k6d, "", 170f));
            // 7 deacylation: water comes in, arrows, second intermediate, arrows, product 2
            var k7a = Clone(k6d, "7 Deacylation", 172f); k7a.labels = "water"; keys.Add(k7a);
            var k7b = Clone(k7a, "", 180f); k7b.step = 4f; keys.Add(k7b);
            var k7c = Clone(k7b, "", 186f); k7c.arrows = 1f; keys.Add(k7c);
            var k7d = Clone(k7c, "", 196f); k7d.step = 5f; k7d.arrows = 0f; k7d.labels = ""; keys.Add(k7d);
            var k7e = Clone(k7d, "", 200f); k7e.arrows = 1f; k7e.hole = 0.6f; keys.Add(k7e);
            var k7f = Clone(k7e, "", 210f); k7f.step = 6f; k7f.arrows = 0f; k7f.hole = 0f; keys.Add(k7f);
            // 8 pull back to the whole enzyme while product 2 leaves
            keys.Add(Clone(k7f, "8 Pull back", 210.5f));
            var k8b = K("", 226f, C + nrm * R0, C, Vector3.up, 35f); k8b.step = 7f; k8b.pocketAlpha = 0f; keys.Add(k8b);
            // 9 wide orbit, free enzyme
            for (int k = 1; k <= 3; k++)
            {
                Vector3 dir = Quaternion.AngleAxis(20f * k, Vector3.up) * nrm;
                var key = K(k == 1 ? "9 Wide orbit" : "", 226f + 5f * k, C + dir * R0, C, Vector3.up, 35f);
                key.step = 7f; keys.Add(key);
            }
            time = 0f; Apply(0f);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>The view for the chemistry close-ups: the direction in which the reacting atoms of the ES complex and
        /// the first intermediate lie flattest on screen (least spread in depth), from the open side of the cleft and
        /// clear of the chain tubes. Returns the look-at point, the direction toward the camera and the half-extent.</summary>
        public void ChemistryView(out Vector3 centre, out Vector3 dir, out float extent)
        {
            var m = builder.mechanism;
            string[] names = { "Ser195:OG", "H1", "His57:NE2", "His57:ND1", "S:C2", "S:O2", "L:N", "Gly193:N", "Ser195:N", "Asp102:OD1", "S:C3" };
            var pts = new List<Vector3>();
            foreach (int st in new[] { 1, 2 }) foreach (var nm in names) pts.Add(m.WorldPos(nm, st));
            centre = Vector3.zero; foreach (var p in pts) centre += p; centre /= pts.Count;
            Vector3 outward = (centre - builder.Target("center").position).normalized;
            var trace = new List<Vector3>();
            foreach (var t in builder.chains) foreach (var p in t.ptsT) trace.Add(t.transform.TransformPoint(p));
            float best = float.MaxValue; dir = outward;
            for (int i = 0; i < 2000; i++)
            {
                float z = -1f + 2f * (i + 0.5f) / 2000f, phi = i * 2.39996f, r = Mathf.Sqrt(1f - z * z);
                var d = new Vector3(r * Mathf.Cos(phi), z, r * Mathf.Sin(phi));
                if (Vector3.Dot(d, outward) < 0.2f) continue;
                float var = 0f; foreach (var p in pts) { float dp = Vector3.Dot(p - centre, d); var += dp * dp; }
                int occ = 0;
                foreach (var p in trace) { Vector3 v = p - centre; float tp = Vector3.Dot(v, d); if (tp < 0.3f || tp > 3f) continue; if ((v - d * tp).magnitude < 0.35f) occ++; }
                float score = var / pts.Count + 0.02f * occ;
                if (score < best) { best = score; dir = d; }
            }
            extent = 0f;
            foreach (var p in pts) { Vector3 v = p - centre; v -= Vector3.Dot(v, dir) * dir; extent = Mathf.Max(extent, v.magnitude); }
        }

        static bool HasGroup(string list, string g)
        {
            if (string.IsNullOrEmpty(list)) return false;
            foreach (var x in list.Split(',')) if (x.Trim() == g) return true;
            return false;
        }

        static Key Clone(Key k, string label, float time)
        {
            return new Key { label = label, time = time, camPos = k.camPos, lookAt = k.lookAt, up = k.up, fov = k.fov, step = k.step,
                             arrows = k.arrows, lbhb = k.lbhb, hole = k.hole, chainsAlpha = k.chainsAlpha, pocketAlpha = k.pocketAlpha, pocket = k.pocket, labels = k.labels };
        }
    }
}
