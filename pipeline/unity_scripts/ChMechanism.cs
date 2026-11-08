// ChMechanism.cs — the reacting atoms of the chymotrypsin mechanism, carried through the eight states of
// Lehninger Fig. 6-27 by one number, `step` (0 free enzyme, 1 ES complex, 2 intermediate, 3 acyl-enzyme,
// 4 acyl-enzyme + water, 5 intermediate, 6 enzyme-product 2 complex, 7 free enzyme). Between two states atoms
// slide, bonds grow or thin out, single bonds turn into dashed hydrogen bonds and back, the moving protons hop,
// circled charges fade, and the curved arrows of the current state draw themselves on (`arrows`) and fade as
// the next state begins. Colors follow the 2D panels: leaving half blue, moving protons magenta, arrows blue.
using System.Collections.Generic;
using UnityEngine;
using HbTutorial;

namespace ChTutorial
{
    [ExecuteAlways]
    public class ChMechanism : MonoBehaviour
    {
        [Range(0f, 7f)] public float step;
        [Range(0f, 1f)] public float arrows;          // draw-on of the current state's arrows
        [Range(0f, 1f)] public float lbhb;            // brightens and thickens the Asp102-His57 hydrogen bond (shot 4)
        [Range(0f, 1f)] public float hole;            // lights the oxyanion-hole hydrogen bonds (shot 5)
        [Range(0f, 1f)] public float substrateAlpha = 1f;   // 0 hides substrate, water and leaving group (free-enzyme shots)
        public Camera viewCamera;                     // arrows, double bonds and charges are laid out for this camera

        [Header("Look")]
        public float stickRadius = 0.013f;
        public float hbondRadius = 0.0055f;
        public float atomScale = 0.85f;
        public float doubleOffset = 0.034f;
        public float arrowRadius = 0.0095f;
        public float arrowHeadLength = 0.05f, arrowHeadRadius = 0.025f;
        public float chargeSize = 0.026f;
        public Color arrowColor = new Color(0.30f, 0.66f, 1.00f);
        public Color protonColor = new Color(0.95f, 0.25f, 0.85f);
        public Color leavingColor = new Color(0.35f, 0.62f, 1.00f);
        public Color acylCarbon = new Color(0.50f, 0.50f, 0.52f);
        public Color enzymeCarbon = new Color(0.78f, 0.74f, 0.62f);
        public Color hbondColor = new Color(0.80f, 0.86f, 0.95f);
        public Color glowColor = new Color(1.00f, 0.88f, 0.25f);

        [System.Serializable] public class AtomRec { public string name, elem, cls, res; public Vector3[] P; public float[] vis; public Transform tf; public float r; }
        [System.Serializable] public class BondRec { public int a, b; public float[] order; public Transform main, second; public Transform[] dashes; }
        [System.Serializable] public class ChargeRec { public int atom; public int[] charge; public Transform tf, vbar; }
        [System.Serializable] public class ArrowRec { public int state, fromA, fromB, toA, toB; public MeshFilter tube; public Transform head; }

        public int stateCount = 8;
        public List<AtomRec> atoms = new List<AtomRec>();
        public List<BondRec> bonds = new List<BondRec>();
        public List<ChargeRec> charges = new List<ChargeRec>();
        public List<ArrowRec> arrowRecs = new List<ArrowRec>();
        public int aspHisBond = -1;
        public List<int> holeBonds = new List<int>();

        // For an external drawer (ChShapesDrawer, when the Shapes package is installed): with externalDraw on, the
        // hydrogen-bond dashes, curved arrows and circled charges are not built as meshes here; their geometry is
        // published in these lists (local space of this transform) every Apply, and the drawer renders them.
        [System.NonSerialized] public bool externalDraw;
        public struct HBondDraw { public Vector3 a, b; public float weight, glow; }
        public struct ArrowDraw { public Vector3 p0, p1, p2, p3; public float f; }
        public struct ChargeDraw { public Vector3 pos; public float size; public bool plus; }
        [System.NonSerialized] public readonly List<HBondDraw> hbondDraws = new List<HBondDraw>();
        [System.NonSerialized] public readonly List<ArrowDraw> arrowDraws = new List<ArrowDraw>();
        [System.NonSerialized] public readonly List<ChargeDraw> chargeDraws = new List<ChargeDraw>();

        Vector3[] _pos; float[] _vis;
        MaterialPropertyBlock _mpb;
        const int Dashes = 5;

        // ------------------------------------------------------------------ build
        public void Build(ChDataset d)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            atoms.Clear(); bonds.Clear(); charges.Clear(); arrowRecs.Clear(); holeBonds.Clear(); aspHisBond = -1;
            stateCount = d.StateCount;
            var atomRoot = Child(transform, "Atoms"); var bondRoot = Child(transform, "Bonds");
            var chargeRoot = Child(transform, "Charges"); var arrowRoot = Child(transform, "Arrows");

            foreach (var a in d.atoms)
            {
                var rec = new AtomRec { name = a.name, elem = a.elem, cls = a.cls, res = a.res, P = new Vector3[stateCount], vis = new float[stateCount] };
                for (int s = 0; s < stateCount; s++)
                {
                    rec.P[s] = d.ToUnityAt(a.P, s);
                    rec.vis[s] = (a.vis != null && s < a.vis.Length) ? a.vis[s] : 1f;
                }
                var go = new GameObject(a.name);
                go.transform.SetParent(atomRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = HbStyle.Sphere;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = HbStyle.Mat(AtomColor(rec), false, rec.cls == "proton" ? 0.5f : 0f);
                rec.r = (a.elem == "H" ? 0.024f : HbStyle.ElementRadius(a.elem)) * atomScale;
                rec.tf = go.transform;
                atoms.Add(rec);
            }

            var bondMat = HbStyle.Mat(new Color(0.80f, 0.80f, 0.80f));
            var leaveMat = HbStyle.Mat(Color.Lerp(leavingColor, Color.white, 0.35f));
            var hbMat = HbStyle.Mat(hbondColor, false, 0.6f);
            for (int i = 0; i < d.bonds.Length; i++)
            {
                var b = d.bonds[i];
                var rec = new BondRec { a = b.a, b = b.b, order = b.order };
                bool solid = false, dbl = false, dash = false;
                foreach (var o in b.order) { if (o >= 0.75f) solid = true; if (o >= 1.5f) dbl = true; if (o >= 0.25f && o < 0.75f) dash = true; }
                string nm = atoms[b.a].name + "-" + atoms[b.b].name;
                var mat = (atoms[b.a].cls == "leaving" && atoms[b.b].cls == "leaving") ? leaveMat : bondMat;
                if (solid) rec.main = Cyl(bondRoot, nm, mat);
                if (dbl) rec.second = Cyl(bondRoot, nm + " (2)", mat);
                if (dash)
                {
                    rec.dashes = new Transform[Dashes];
                    for (int k = 0; k < Dashes; k++) rec.dashes[k] = Cyl(bondRoot, nm + " h" + k, hbMat);
                }
                bonds.Add(rec);
                string an = atoms[b.a].name, bn = atoms[b.b].name;
                if ((an == "His57:HD1" && bn.StartsWith("Asp102:")) || (bn == "His57:HD1" && an.StartsWith("Asp102:"))) aspHisBond = i;
                if ((an == "Gly193:H" || an == "Ser195:H") && bn == "S:O2") holeBonds.Add(i);
            }

            var ring = ChMeshes.Ring(); var quad = ChMeshes.Quad();
            var chargeMat = HbStyle.LineMat(Color.white);
            if (chargeMat.HasProperty("_Cull")) chargeMat.SetFloat("_Cull", 0f);
            chargeMat.name = "ch_charge_white";
            foreach (var c in d.charges)
            {
                var rec = new ChargeRec { atom = c.atom, charge = c.charge };
                var go = new GameObject("charge " + atoms[c.atom].name);
                go.transform.SetParent(chargeRoot, false);
                rec.tf = go.transform;
                var r = new GameObject("ring"); r.transform.SetParent(go.transform, false);
                r.AddComponent<MeshFilter>().sharedMesh = ring; r.AddComponent<MeshRenderer>().sharedMaterial = chargeMat;
                var h = new GameObject("bar"); h.transform.SetParent(go.transform, false);
                h.AddComponent<MeshFilter>().sharedMesh = quad; h.AddComponent<MeshRenderer>().sharedMaterial = chargeMat;
                h.transform.localScale = new Vector3(1.1f, 0.16f, 1f);
                var v = new GameObject("vbar"); v.transform.SetParent(go.transform, false);
                v.AddComponent<MeshFilter>().sharedMesh = quad; v.AddComponent<MeshRenderer>().sharedMaterial = chargeMat;
                v.transform.localScale = new Vector3(0.16f, 1.1f, 1f);
                rec.vbar = v.transform;
                charges.Add(rec);
            }

            var cone = ChMeshes.Cone();
            var arrowMat = HbStyle.Mat(arrowColor, false, 1.3f);
            for (int i = 0; i < d.arrows.Length; i++)
            {
                var a = d.arrows[i];
                var rec = new ArrowRec { state = a.state, fromA = a.fromA, fromB = a.fromB, toA = a.toA, toB = a.toB };
                string nm = "arrow s" + a.state + " " + atoms[a.fromA].name + (a.fromB >= 0 ? "|" + atoms[a.fromB].name : "") +
                            " -> " + atoms[a.toA].name + (a.toB >= 0 ? "|" + atoms[a.toB].name : "");
                var go = new GameObject(nm); go.transform.SetParent(arrowRoot, false);
                rec.tube = go.AddComponent<MeshFilter>();
                rec.tube.sharedMesh = new Mesh { name = nm };
                rec.tube.sharedMesh.MarkDynamic();
                go.AddComponent<MeshRenderer>().sharedMaterial = arrowMat;
                var h = new GameObject("head"); h.transform.SetParent(go.transform, false);
                h.AddComponent<MeshFilter>().sharedMesh = cone; h.AddComponent<MeshRenderer>().sharedMaterial = arrowMat;
                rec.head = h.transform;
                arrowRecs.Add(rec);
            }
            Apply();
        }

        Color AtomColor(AtomRec a)
        {
            if (a.cls == "proton") return protonColor;
            if (a.cls == "leaving") return a.elem == "C" ? leavingColor : (a.elem == "H" ? new Color(0.80f, 0.88f, 1f) : HbStyle.ElementColor(a.elem));
            if (a.elem == "H") return new Color(0.95f, 0.95f, 0.95f);
            if (a.elem == "C") return a.cls == "enzyme" ? enzymeCarbon : acylCarbon;
            return HbStyle.ElementColor(a.elem);
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static Transform Cyl(Transform parent, string name, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = HbStyle.Cylinder;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        // ------------------------------------------------------------------ apply
        void LateUpdate() { Apply(); }

        public bool IsSubstrate(AtomRec a) { return a.cls == "acyl" || a.cls == "leaving" || a.cls == "water"; }

        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
        static Vector3 Weights(float o)   // (dashed, solid, second line) for a bond order
        {
            if (o >= 1.5f) return new Vector3(0f, 1f, 1f);
            if (o >= 0.75f) return new Vector3(0f, 1f, 0f);
            if (o >= 0.25f) return new Vector3(1f, 0f, 0f);
            return Vector3.zero;
        }

        /// <summary>World position of a reacting atom in one of the states (for camera framing).</summary>
        public Vector3 WorldPos(string name, int state)
        {
            foreach (var a in atoms) if (a.name == name) return transform.TransformPoint(a.P[Mathf.Clamp(state, 0, stateCount - 1)]);
            return transform.position;
        }

        /// <summary>Transform of a reacting atom by name, or null.</summary>
        public Transform AtomTransform(string name)
        {
            foreach (var a in atoms) if (a.name == name) return a.tf;
            return null;
        }

        public void Apply()
        {
            if (atoms == null || atoms.Count == 0 || stateCount < 2) return;
            float st = Mathf.Clamp(step, 0f, stateCount - 1);
            int s = Mathf.Min(Mathf.FloorToInt(st), stateCount - 2);
            float u = st - s, e = Smooth(u);
            var cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3 viewL = cam ? transform.InverseTransformDirection(cam.transform.forward) : Vector3.forward;
            Vector3 rightL = cam ? transform.InverseTransformDirection(cam.transform.right) : Vector3.right;
            Vector3 upL = cam ? transform.InverseTransformDirection(cam.transform.up) : Vector3.up;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            int n = atoms.Count;
            if (_pos == null || _pos.Length != n) { _pos = new Vector3[n]; _vis = new float[n]; }
            hbondDraws.Clear(); arrowDraws.Clear(); chargeDraws.Clear();

            // atoms
            Vector3 centroid = Vector3.zero; float wsum = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = atoms[i];
                if (a.P == null || a.P.Length < stateCount) continue;
                Vector3 p = Vector3.LerpUnclamped(a.P[s], a.P[s + 1], e);
                float v = Mathf.Lerp(a.vis[s], a.vis[s + 1], e);
                if (IsSubstrate(a)) v *= substrateAlpha;
                _pos[i] = p; _vis[i] = v;
                if (a.tf == null) continue;
                a.tf.localPosition = p;
                a.tf.localScale = Vector3.one * (2f * a.r * v);
                bool on = v > 0.002f;
                if (a.tf.gameObject.activeSelf != on) a.tf.gameObject.SetActive(on);
                if (on) { centroid += p * v; wsum += v; }
            }
            if (wsum > 0f) centroid /= wsum;

            // bonds
            for (int i = 0; i < bonds.Count; i++)
            {
                var b = bonds[i];
                Vector3 w = Vector3.Lerp(Weights(b.order[s]), Weights(b.order[s + 1]), e) * Mathf.Min(_vis[b.a], _vis[b.b]);
                Vector3 p = _pos[b.a], q = _pos[b.b], d = q - p; float len = d.magnitude;
                Vector3 dir = len > 1e-6f ? d / len : Vector3.up;
                Vector3 perp = Vector3.Cross(dir, viewL); perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.right;
                float off = doubleOffset * 0.5f * w.z;
                if (b.main != null) Stick(b.main, p - perp * off, q - perp * off, stickRadius * w.y);
                if (b.second != null) Stick(b.second, p + perp * off, q + perp * off, stickRadius * 0.8f * w.z);
                if (b.dashes != null && b.dashes.Length == Dashes)   // (Unity deserializes a missing array as empty)
                {
                    float glow = (i == aspHisBond) ? lbhb : (holeBonds.Contains(i) ? hole : 0f);
                    float ra = atoms[b.a].r * 0.8f, rb = atoms[b.b].r * 0.8f;
                    Vector3 a0 = p + dir * ra, a1 = q - dir * rb; float L = Mathf.Max(0f, len - ra - rb), seg = L / (2 * Dashes - 1);
                    if (w.x > 0.002f) hbondDraws.Add(new HBondDraw { a = a0, b = a1, weight = w.x, glow = glow });
                    float rad = externalDraw ? 0f : hbondRadius * w.x * (1f + 1.0f * glow);
                    Color c = Color.Lerp(hbondColor, glowColor, glow);
                    for (int k = 0; k < Dashes; k++)
                    {
                        var tf = b.dashes[k]; if (tf == null) continue;
                        Vector3 c0 = a0 + dir * (seg * 2 * k), c1 = c0 + dir * seg;
                        Stick(tf, c0, c1, rad);
                        var mr = tf.GetComponent<MeshRenderer>();
                        if (glow > 0.001f)
                        {
                            _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_EmissionColor", c * (0.6f + 1.4f * glow));
                            mr.SetPropertyBlock(_mpb);
                        }
                        else mr.SetPropertyBlock(null);
                    }
                }
            }

            // circled charges, beside their atom toward the upper right of the screen
            foreach (var c in charges)
            {
                if (c.tf == null) continue;
                float q = Mathf.Lerp(c.charge[s], c.charge[s + 1], e) * _vis[c.atom];
                float mag = Mathf.Abs(q);
                bool on = mag > 0.02f;
                var a = atoms[c.atom];
                Vector3 offDir = (rightL + upL).normalized;
                Vector3 cpos = _pos[c.atom] + offDir * (a.r + chargeSize * 1.6f) - viewL * (a.r * 1.2f);
                if (on) chargeDraws.Add(new ChargeDraw { pos = cpos, size = chargeSize * mag, plus = q > 0f || (c.charge[s] + c.charge[s + 1]) > 0 });
                if (externalDraw) on = false;
                if (c.tf.gameObject.activeSelf != on) c.tf.gameObject.SetActive(on);
                if (!on) continue;
                c.tf.localPosition = cpos;
                c.tf.localRotation = Quaternion.LookRotation(viewL, upL);
                c.tf.localScale = Vector3.one * chargeSize * mag;
                bool plus = q > 0f || (c.charge[s] + c.charge[s + 1]) > 0;
                if (c.vbar != null && c.vbar.gameObject.activeSelf != plus) c.vbar.gameObject.SetActive(plus);
            }

            // curved arrows of the current state: drawn on by `arrows`, fading over the first 30% of the next morph
            foreach (var r in arrowRecs)
            {
                if (r.tube == null) continue;
                float f = (r.state == s) ? Mathf.Clamp01(arrows) * (1f - Smooth(u / 0.3f)) : 0f;
                bool on = f > 0.004f;
                Vector3 p0 = Vector3.zero, p1 = Vector3.zero, p2 = Vector3.zero, p3 = Vector3.zero;
                if (on) { ArrowCurve(r, viewL, centroid, out p0, out p1, out p2, out p3); arrowDraws.Add(new ArrowDraw { p0 = p0, p1 = p1, p2 = p2, p3 = p3, f = f }); }
                if (externalDraw) on = false;
                if (r.tube.gameObject.activeSelf != on) r.tube.gameObject.SetActive(on);
                if (on) DrawArrow(r, f, viewL, p0, p1, p2, p3);
            }
        }

        void Stick(Transform tf, Vector3 p, Vector3 q, float radius)
        {
            bool on = radius > 0.0004f;
            if (tf.gameObject.activeSelf != on) tf.gameObject.SetActive(on);
            if (!on) return;
            Vector3 d = q - p; float len = d.magnitude;
            tf.localPosition = (p + q) * 0.5f;
            if (len > 1e-6f) tf.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            tf.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);   // Unity cylinder is 2 units tall
        }

        readonly List<Vector3> _pts = new List<Vector3>();
        /// <summary>The Bezier curve of an arrow (local space), its end pulled back to leave room for the head.</summary>
        void ArrowCurve(ArrowRec r, Vector3 viewL, Vector3 centroid, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            Vector3 a0 = _pos[r.fromA];
            Vector3 start = r.fromB >= 0 ? (a0 + _pos[r.fromB]) * 0.5f : a0;
            Vector3 target = r.toB >= 0 ? (_pos[r.toA] + _pos[r.toB]) * 0.5f : _pos[r.toA];
            Vector3 chord = target - start; float L = chord.magnitude;
            Vector3 cd = L > 1e-6f ? chord / L : Vector3.right;
            // bulge sideways on screen, away from the middle of the reacting atoms
            Vector3 perp = Vector3.Cross(cd, viewL); perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.up;
            if (Vector3.Dot(perp, (start + target) * 0.5f - centroid) < 0f) perp = -perp;
            // ends: a lone pair sits on the atom's surface on the bulge side; a bond end sits just off the stick
            if (r.fromB < 0) start += (cd * 0.5f + perp).normalized * atoms[r.fromA].r * 1.6f;
            else start += perp * 0.022f;
            Vector3 end = r.toB < 0 ? target + (perp - cd * 0.6f).normalized * atoms[r.toA].r * 1.5f : target + perp * 0.022f;
            chord = end - start; L = chord.magnitude; cd = L > 1e-6f ? chord / L : cd;
            float h = Mathf.Max(0.5f * L, 0.08f);
            p0 = start; p3 = end;
            p1 = p0 + cd * (0.2f * L) + perp * h; p2 = p3 - cd * (0.2f * L) + perp * h;
            Vector3 tEnd = ChMeshes.BezierTangent(p0, p1, p2, p3, 1f).normalized;
            p3 -= tEnd * arrowHeadLength; p2 -= tEnd * arrowHeadLength * 0.5f;
        }

        /// <summary>Fallback arrow without Shapes: a tube along the curve up to fraction f, and a cone head.</summary>
        void DrawArrow(ArrowRec r, float f, Vector3 viewL, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            _pts.Clear();
            int N = 20;
            for (int i = 0; i <= N; i++) _pts.Add(ChMeshes.Bezier(p0, p1, p2, p3, f * i / N));
            var mesh = r.tube.sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = r.tube.name }; mesh.MarkDynamic(); r.tube.sharedMesh = mesh; }
            TubeMesh.Build(_pts.ToArray(), arrowRadius, 10, 2, mesh);
            Vector3 tip = ChMeshes.Bezier(p0, p1, p2, p3, f), tan = ChMeshes.BezierTangent(p0, p1, p2, p3, f);
            float hs = Mathf.Clamp01(f / 0.2f);
            r.head.localPosition = tip;
            if (tan.sqrMagnitude > 1e-10f) r.head.localRotation = Quaternion.LookRotation(tan.normalized, viewL);
            r.head.localScale = new Vector3(arrowHeadRadius * hs, arrowHeadRadius * hs, arrowHeadLength * hs);
        }
    }
}
