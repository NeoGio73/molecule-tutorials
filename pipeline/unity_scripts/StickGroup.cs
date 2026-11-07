// StickGroup.cs — ball-and-stick for one residue, heme or ligand, morphing between T and R.
// Atoms that exist only in R (O2) scale in over the last part of the morph.
using System.Collections.Generic;
using UnityEngine;

namespace HbTutorial
{
    public class StickGroup : HbMorphable
    {
        public string key;                 // e.g. "beta1:146", "heme:alpha1", "o2:alpha1"
        public string tag;                 // residue tag from the JSON (proximal_his, distal_his, ionpair) or ""
        public bool appearInR;             // scale in as t -> 1
        public float exaggeration = 1f;    // multiplies the T->R displacement (3x for the Fe shot)
        public string exaggerateOnly = ""; // empty = every atom in the group, else just this atom name
        public StickGroup followExaggerationOf;   // e.g. the proximal His rides along with its heme's Fe
        public string followAtom = "FE";
        public bool clickable;             // adds sphere colliders so the twin can raycast residues
        public float stickRadius = 0.06f;
        public float atomScale = 1.0f;

        [System.Serializable] public class AtomNode { public string name, elem; public Vector3 T, R; public Transform tf; }
        public List<AtomNode> atoms = new List<AtomNode>();
        public int[] bonds = new int[0];
        public List<Transform> _bondTfs = new List<Transform>();          // serialized so it survives domain reloads
        Dictionary<string, AtomNode> _byName;                             // rebuilt lazily after a reload

        Dictionary<string, AtomNode> ByName
        {
            get
            {
                if (_byName == null || _byName.Count != atoms.Count)
                {
                    _byName = new Dictionary<string, AtomNode>();
                    foreach (var a in atoms) _byName[a.name] = a;
                }
                return _byName;
            }
        }

        public void Init(HbDataset data, HbAtom[] atomList, int[] bondList, string groupKey, bool rOnly = false, Color? carbonTint = null)
        {
            key = groupKey; appearInR = rOnly; bonds = bondList ?? new int[0];
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            atoms.Clear(); _byName = null; _bondTfs.Clear();

            foreach (var a in atomList)
            {
                var node = new AtomNode { name = a.name, elem = a.elem };
                bool hasT = a.T != null && a.T.Length == 3, hasR = a.R != null && a.R.Length == 3;
                node.T = hasT ? data.ToUnity(a.T) : (hasR ? data.ToUnity(a.R) : Vector3.zero);
                node.R = hasR ? data.ToUnity(a.R) : node.T;
                var go = new GameObject(a.name);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = HbStyle.Sphere;
                var mr = go.AddComponent<MeshRenderer>();
                Color col = (a.elem == "C" && carbonTint.HasValue) ? carbonTint.Value : HbStyle.ElementColor(a.elem);
                mr.sharedMaterial = HbStyle.Mat(col);
                float r = HbStyle.ElementRadius(a.elem) * atomScale;
                go.transform.localScale = Vector3.one * (r * 2f);
                if (clickable) { var sc = go.AddComponent<SphereCollider>(); sc.radius = 1.5f; }
                node.tf = go.transform;
                atoms.Add(node);
            }
            for (int i = 0; i + 1 < bonds.Length; i += 2)
            {
                var go = new GameObject("bond_" + atoms[bonds[i]].name + "_" + atoms[bonds[i + 1]].name);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = HbStyle.Cylinder;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = HbStyle.Mat(new Color(0.8f, 0.8f, 0.8f));
                _bondTfs.Add(go.transform);
            }
            SetT(0f);
        }

        public Transform AtomTransform(string atomName)
        {
            return ByName.TryGetValue(atomName, out var n) ? n.tf : null;
        }
        public AtomNode Node(string atomName)
        {
            return ByName.TryGetValue(atomName, out var n) ? n : null;
        }

        /// <summary>The extra offset another group's exaggerated atom has beyond its true position at this t.</summary>
        Vector3 FollowOffset(float t)
        {
            if (followExaggerationOf == null) return Vector3.zero;
            var src = followExaggerationOf.Node(followAtom);
            if (src == null) return Vector3.zero;
            return (src.T - src.R) * ((1f - t) * (followExaggerationOf.exaggeration - 1f));
        }

        public override void SetT(float t)
        {
            float vis = appearInR ? Mathf.Clamp01((t - 0.6f) / 0.4f) : 1f;
            Vector3 extra = FollowOffset(t);
            for (int i = 0; i < atoms.Count; i++)
            {
                var a = atoms[i];
                if (a.tf == null) continue;
                // exaggeration scales the T-state offset from R, so the atom starts k times further out and settles at the true R position
                float k = (string.IsNullOrEmpty(exaggerateOnly) || a.name == exaggerateOnly) ? exaggeration : 1f;
                a.tf.localPosition = a.R + (a.T - a.R) * ((1f - t) * k) + extra;
                float r = HbStyle.ElementRadius(a.elem) * atomScale * vis;
                a.tf.localScale = Vector3.one * (r * 2f);
                a.tf.gameObject.SetActive(vis > 0.001f);
            }
            for (int b = 0, i = 0; i + 1 < bonds.Length; i += 2, b++)
            {
                if (b >= _bondTfs.Count) break;
                var p = atoms[bonds[i]].tf.localPosition; var q = atoms[bonds[i + 1]].tf.localPosition;
                var tf = _bondTfs[b];
                Vector3 d = q - p; float len = d.magnitude;
                tf.localPosition = (p + q) * 0.5f;
                if (len > 1e-5f) tf.localRotation = Quaternion.FromToRotation(Vector3.up, d);
                float w = stickRadius * 2f * vis;
                tf.localScale = new Vector3(w, len * 0.5f, w);   // Unity cylinder is 2 units tall
                tf.gameObject.SetActive(vis > 0.001f);
            }
        }
    }
}
