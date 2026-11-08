// ChSideChainPicker.cs — the shot 3 checkpoint's side-chain picker (schematic, not computed docking).
// The student picks Phe, Tyr, Trp, Lys, Glu or Gly as the substrate's P1 side chain. The Phe ring of the
// bound substrate (ES complex) is swapped for the chosen side chain, built on the same Cβ and ring frame:
// Phe, Tyr and Trp settle into the hydrophobic pocket; Lys and Glu (charged) swing out of the pocket toward the
// solvent and the substrate backs off a little; Gly (no side chain to hold) just backs off. The screen says it is a schematic.
using System.Collections.Generic;
using UnityEngine;
using HbTutorial;

namespace ChTutorial
{
    public class ChSideChainPicker : MonoBehaviour
    {
        public ChMechanism mechanism;
        public string current = "";                  // "" = picker off
        public float outDistance = 0.15f;             // scene units the substrate backs off when the side chain is turned away
        public float animTime = 1.6f;

        static readonly string[] Ring = { "S:CP1", "S:CP2", "S:CP3", "S:CP4", "S:CP5", "S:CP6" };
        public static readonly string[] Choices = { "Phe", "Tyr", "Trp", "Lys", "Glu", "Gly" };
        public static bool Fits(string aa) { return aa == "Phe" || aa == "Tyr" || aa == "Trp"; }
        public static string Verdict(string aa)
        {
            switch (aa)
            {
                case "Phe": case "Tyr": case "Trp": return "Fits: a large aromatic side chain settles into the hydrophobic pocket.";
                case "Lys": return "Turned away: the charged amino group does not belong in the hydrophobic pocket.";
                case "Glu": return "Turned away: the charged carboxylate does not belong in the hydrophobic pocket.";
                default: return "Turned away: glycine has no side chain to hold it in the pocket.";
            }
        }

        readonly List<Transform> _atoms = new List<Transform>();
        readonly List<(int a, int b, Transform tf)> _bonds = new List<(int, int, Transform)>();
        readonly List<Vector3> _template = new List<Vector3>();   // positions in the ring frame (u along Cβ->ring, w in plane, n normal)
        float _t0;
        Transform _root;

        /// <summary>Show a side chain in place of the Phe ring ("" turns the picker off and restores Phe).</summary>
        public void Pick(string aa)
        {
            current = aa ?? "";
            Clear();
            mechanism.hidden.Clear();
            mechanism.substrateOffset = Vector3.zero;
            if (current == "" || current == "Phe") { _t0 = Time.time; mechanism.Apply(); return; }
            foreach (var r in Ring) mechanism.hidden.Add(r);
            if (current == "Gly") mechanism.hidden.Add("S:C4");
            BuildTemplate(current);
            _t0 = Time.time;
            mechanism.Apply();
        }

        void Clear()
        {
            if (_root != null) { if (Application.isPlaying) Destroy(_root.gameObject); else DestroyImmediate(_root.gameObject); }
            _root = null; _atoms.Clear(); _bonds.Clear(); _template.Clear();
        }

        // Template atoms in the ring frame. Origin = Cβ (S:C4); u = unit vector Cβ -> ring centre; w = in the ring plane.
        void BuildTemplate(string aa)
        {
            var elems = new List<string>(); var bonds = new List<(int, int)>();
            const float cc = 1.52f;
            float a60 = 60f * Mathf.Deg2Rad;
            System.Func<float, float, Vector3> P = (uu, ww) => new Vector3(uu, ww, 0f);   // (u, w, n) in angstrom
            switch (aa)
            {
                case "Tyr":
                {
                    // the Phe ring plus the para O-H: ring atoms from the real Phe are kept, so only the O is new
                    foreach (var r in Ring) mechanism.hidden.Remove(r);
                    float ringR = 1.39f, cg = 1.51f;
                    _template.Add(P(cg + 2f * ringR + 1.36f, 0f)); elems.Add("O");
                    break;
                }
                case "Trp":
                {
                    // indole: Cγ on the Cβ->ring axis, pyrrole then benzene, flat in the ring plane
                    float d = 1.40f;
                    Vector3 cgp = P(1.50f, 0f);
                    // regular pentagon with Cγ at the near vertex, opening along +u
                    float R5 = d / (2f * Mathf.Sin(Mathf.PI / 5f));
                    Vector3 c5 = cgp + new Vector3(R5, 0f, 0f);
                    var pent = new Vector3[5];
                    for (int i = 0; i < 5; i++) { float ang = Mathf.PI + i * 2f * Mathf.PI / 5f; pent[i] = c5 + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * R5; }
                    // pent: 0 CG, 1 CD1, 2 NE1, 3 CE2, 4 CD2 ; benzene fused on CD2-CE2 (3-4), away from the pentagon centre
                    Vector3 m = (pent[3] + pent[4]) * 0.5f, away = (m - c5).normalized;
                    Vector3 c6 = m + away * (d * Mathf.Sqrt(3f) / 2f);
                    var hex = new Vector3[6];
                    Vector3 e0 = (pent[4] - c6);
                    for (int i = 0; i < 6; i++) hex[i] = c6 + Quaternion.AngleAxis(-60f * i, Vector3.forward) * e0;   // hex[0]=CD2, hex[5]=CE2 or hex[1]
                    string[] pe = { "C", "C", "N", "C", "C" };
                    for (int i = 0; i < 5; i++) { _template.Add(pent[i]); elems.Add(pe[i]); }
                    bonds.Add((-1, 0)); for (int i = 0; i < 5; i++) bonds.Add((i, (i + 1) % 5));
                    // the four new benzene carbons (hex[2..5] minus the one that coincides with CE2)
                    int start = _template.Count;
                    var extra = new List<Vector3>();
                    for (int i = 1; i < 6; i++) if ((hex[i] - pent[3]).magnitude > 0.3f) extra.Add(hex[i]);
                    // order the extra atoms as a path from CD2's neighbour to CE2's neighbour
                    extra.Sort((x, y) => (x - pent[4]).sqrMagnitude.CompareTo((y - pent[4]).sqrMagnitude));
                    var path = new List<Vector3> { extra[0] };
                    var left = new List<Vector3>(extra); left.RemoveAt(0);
                    while (left.Count > 0) { left.Sort((x, y) => (x - path[path.Count - 1]).sqrMagnitude.CompareTo((y - path[path.Count - 1]).sqrMagnitude)); path.Add(left[0]); left.RemoveAt(0); }
                    foreach (var p in path) { _template.Add(p); elems.Add("C"); }
                    bonds.Add((4, start)); for (int i = 0; i < path.Count - 1; i++) bonds.Add((start + i, start + i + 1)); bonds.Add((start + path.Count - 1, 3));
                    break;
                }
                case "Lys":
                {
                    // Cγ, Cδ, Cε, Nζ as a zigzag along u
                    string[] e = { "C", "C", "C", "N" };
                    for (int i = 0; i < 4; i++) { _template.Add(P(cc * Mathf.Sin(a60) * (i + 1) * 1.15f, (i % 2 == 0 ? 0.5f : -0.5f) * 1.25f)); elems.Add(e[i]); bonds.Add((i - 1, i)); }
                    break;
                }
                case "Glu":
                {
                    // Cγ, Cδ, then the carboxylate O's
                    _template.Add(P(1.30f, 0.62f)); elems.Add("C"); bonds.Add((-1, 0));
                    _template.Add(P(2.60f, 0f)); elems.Add("C"); bonds.Add((0, 1));
                    _template.Add(P(3.25f, 1.05f)); elems.Add("O"); bonds.Add((1, 2));
                    _template.Add(P(3.20f, -1.10f)); elems.Add("O"); bonds.Add((1, 3));
                    break;
                }
            }
            if (aa == "Tyr") bonds.Add((-2, 0));   // bond from the para ring carbon (resolved at runtime) to the O
            // build the objects
            _root = new GameObject("Picker " + aa).transform;
            _root.SetParent(mechanism.transform, false);
            for (int i = 0; i < _template.Count; i++)
            {
                var go = new GameObject(aa + " " + elems[i] + i);
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = HbStyle.Sphere;
                go.AddComponent<MeshRenderer>().sharedMaterial = HbStyle.Mat(elems[i] == "C" ? mechanism.acylCarbon : HbStyle.ElementColor(elems[i]));
                float r = HbStyle.ElementRadius(elems[i]) * mechanism.atomScale;
                go.transform.localScale = Vector3.one * 2f * r;
                _atoms.Add(go.transform);
            }
            var bm = HbStyle.Mat(new Color(0.8f, 0.8f, 0.8f));
            foreach (var (a, b) in bonds)
            {
                var go = new GameObject("bond"); go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = HbStyle.Cylinder; go.AddComponent<MeshRenderer>().sharedMaterial = bm;
                _bonds.Add((a, b, go.transform));
            }
        }

        void LateUpdate()
        {
            if (mechanism == null || current == "") return;
            float k = Mathf.Clamp01((Time.time - _t0) / animTime); k = k * k * (3f - 2f * k);
            mechanism.substrateOffset = Fits(current) ? Vector3.zero : OutDir() * outDistance * k;
            mechanism.Apply();
            if (_root == null) return;
            // ring frame from the bound Phe (ES state), shifted with the substrate
            var cb = mechanism.AtomTransform("S:C4"); if (cb == null) return;
            Vector3 B = cb.localPosition, ringC = Vector3.zero;
            foreach (var r in Ring) ringC += Local(r);
            ringC /= Ring.Length;
            Vector3 u = (ringC - B).normalized;
            Vector3 n = Vector3.Cross(Local("S:CP2") - Local("S:CP1"), Local("S:CP3") - Local("S:CP1")).normalized;
            Vector3 w = Vector3.Cross(n, u).normalized;
            if (!Fits(current))
            {
                // turned away: swing the side chain out of the pocket, about Cβ, at right angles to Cα-Cβ, pointing away
                // from both the pocket and the catalytic Ser/His (toward the solvent at the mouth of the cleft)
                Vector3 ca = (Local("S:C3") - B).normalized;
                Vector3 triad = (Local("Ser195:OG") + Local("His57:NE2")) * 0.5f;
                Vector3 o = -u + (B - triad).normalized;
                o -= Vector3.Dot(o, ca) * ca;
                if (o.sqrMagnitude > 1e-6f)
                {
                    var q = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(u, o.normalized), k);
                    u = q * u; w = q * w; n = q * n;
                }
            }
            float s = 0.1f;                                                    // angstrom -> scene units (unityScale)
            var pos = new Vector3[_template.Count];
            for (int i = 0; i < _template.Count; i++) { var t = _template[i]; pos[i] = B + (u * t.x + w * t.y + n * t.z) * s; }
            for (int i = 0; i < _atoms.Count; i++) _atoms[i].localPosition = pos[i];
            Vector3 para = ParaCarbon(B);
            foreach (var (a, b, tf) in _bonds)
            {
                Vector3 p = a == -1 ? B : (a == -2 ? para : pos[a]), q = pos[b];
                Vector3 d = q - p; float len = d.magnitude; if (len < 1e-5f) continue;
                tf.localPosition = (p + q) * 0.5f; tf.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
                tf.localScale = new Vector3(mechanism.stickRadius * 2f, len * 0.5f, mechanism.stickRadius * 2f);
            }
        }

        Vector3 Local(string name) { var t = mechanism.AtomTransform(name); return t != null ? t.localPosition : Vector3.zero; }

        Vector3 ParaCarbon(Vector3 cb)
        {
            float best = -1f; Vector3 bp = cb;
            foreach (var r in Ring) { var p = Local(r); float d = (p - cb).sqrMagnitude; if (d > best) { best = d; bp = p; } }
            return bp;
        }

        Vector3 OutDir()
        {
            // out of the pocket: from the ring centre back past Cβ toward the scissile carbon
            Vector3 ringC = Vector3.zero; foreach (var r in Ring) ringC += mechanism.atoms.Find(a => a.name == r).P[1]; ringC /= Ring.Length;
            Vector3 c2 = mechanism.atoms.Find(a => a.name == "S:C3").P[1];
            return (c2 - ringC).normalized;
        }

        void OnDisable() { if (mechanism != null) { mechanism.hidden.Clear(); mechanism.substrateOffset = Vector3.zero; } Clear(); current = ""; }
    }
}
