// ChSceneBuilder.cs — builds the chymotrypsin scene from chymotrypsin.json (chymo_prep.py).
// Attach to an empty GameObject, assign the JSON TextAsset, then Inspector menu (⋮) -> Build.
// Everything it creates lives under this object, so Clear removes it cleanly. The enzyme is centred on the origin.
using System.Collections.Generic;
using UnityEngine;
using HbTutorial;

namespace ChTutorial
{
    public class ChSceneBuilder : MonoBehaviour
    {
        public TextAsset json;
        [Header("Look")]
        public float tubeRadius = 0.07f;
        public int tubeSides = 12;
        public int tubeSubdiv = 6;
        public float stickRadius = 0.013f;
        public float contextAtomScale = 0.8f;
        // chains A (1-13), B (16-146), C (149-245): muted, so the sticks read on top
        public Color chainA = new Color(0.93f, 0.62f, 0.30f), chainB = new Color(0.36f, 0.56f, 0.86f), chainC = new Color(0.42f, 0.76f, 0.74f);
        public Color pocketCarbon = new Color(0.45f, 0.78f, 0.45f), activationCarbon = new Color(0.95f, 0.62f, 0.30f);
        public Color pocketSurfaceColor = new Color(0.55f, 0.85f, 0.55f, 0.38f);

        [Header("Built objects (filled by Build)")]
        public Transform root;
        public ChMechanism mechanism;
        public List<ChainTube> chains = new List<ChainTube>();
        public ChainTube zymogen;
        public Transform pocketRoot, activationRoot, targets;
        public MeshRenderer pocketSurface;
        public ChLabels labels;

        [System.NonSerialized] public ChDataset data;

        [ContextMenu("Build")]
        public void Build()
        {
            Clear();
            if (json == null) { Debug.LogError("ChSceneBuilder: assign chymotrypsin.json first"); return; }
            data = ChDataset.Load(json);

            root = new GameObject("Chymotrypsin").transform;
            root.SetParent(transform, false);
            root.localPosition = -data.ToUnity(data.landmarks.center);

            // --- the enzyme as three chain tubes (alpha-chymotrypsin, 1YPH)
            var chainRoot = Child(root, "Chains");
            foreach (var t in data.trace)
            {
                int first = t.resnum[0], last = t.resnum[t.resnum.Length - 1];
                string label = first < 14 ? "A" : (first < 147 ? "B" : "C");
                var col = label == "A" ? chainA : (label == "B" ? chainB : chainC);
                var pts = data.ToUnityArray(t.ca);
                var tube = Child(chainRoot, "chain " + label + " (" + first + "-" + last + ")").gameObject.AddComponent<ChainTube>();
                tube.radius = tubeRadius; tube.sides = tubeSides; tube.subdiv = tubeSubdiv;
                tube.Init(pts, pts, col);
                chains.Add(tube);
            }

            // --- chymotrypsinogen for the coda: one tube that morphs from the zymogen (t = 0) to the active enzyme (t = 1)
            if (data.zymogen != null && data.zymogen.ca != null && data.zymogen.ca.Length > 0)
            {
                var zr = Child(root, "Zymogen");
                zymogen = zr.gameObject.AddComponent<ChainTube>();
                zymogen.radius = tubeRadius; zymogen.sides = tubeSides; zymogen.subdiv = tubeSubdiv;
                zymogen.Init(data.ToUnityArray(data.zymogen.ca), data.ToUnityArray(data.zymogen.caActive), new Color(0.75f, 0.72f, 0.80f));
                zr.gameObject.SetActive(false);
            }

            // --- context sticks: the hydrophobic pocket and the Ile16-Asp194 ion pair (static, 1YPH)
            var hbData = new HbDataset { meta = new HbMeta { unityScale = data.meta.unityScale, mirrorZ = data.meta.mirrorZ } };
            pocketRoot = Child(root, "Pocket"); activationRoot = Child(root, "Activation");
            foreach (var r in data.context)
            {
                var atomsHb = new HbAtom[r.atoms.Length];
                for (int i = 0; i < r.atoms.Length; i++) atomsHb[i] = new HbAtom { name = r.atoms[i].name, elem = r.atoms[i].elem, T = r.atoms[i].P, R = r.atoms[i].P };
                var parent = r.tag == "activation" ? activationRoot : pocketRoot;
                var sg = Child(parent, r.resname + r.resnum).gameObject.AddComponent<StickGroup>();
                sg.stickRadius = stickRadius * 0.85f; sg.atomScale = contextAtomScale; sg.clickable = true; sg.tag = r.tag;
                sg.Init(hbData, atomsHb, r.bonds, r.key, false, r.tag == "activation" ? activationCarbon : pocketCarbon);
            }
            activationRoot.gameObject.SetActive(false);
            pocketRoot.gameObject.SetActive(false);          // the surface stands for the pocket; the sticks are there to inspect

            // --- the hydrophobic pocket as a translucent surface
            if (data.pocketSurface != null && data.pocketSurface.v != null && data.pocketSurface.v.Length > 0)
            {
                var v = data.ToUnityArray(data.pocketSurface.v);
                var t = (int[])data.pocketSurface.t.Clone();
                if (data.meta.mirrorZ)                        // the flip reverses handedness, so reverse the winding to keep the front faces
                    for (int i = 0; i + 2 < t.Length; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
                var mesh = new Mesh { name = "pocket surface" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var centre = data.ToUnity(data.landmarks.pocket);
                var uv = new Vector2[v.Length];
                for (int i = 0; i < v.Length; i++) uv[i] = new Vector2((v[i] - centre).magnitude, 0f);   // for the edge fade
                mesh.vertices = v; mesh.triangles = t; mesh.uv = uv; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = Child(root, "PocketSurface").gameObject;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                pocketSurface = go.AddComponent<MeshRenderer>();
                var rim = Shader.Find("ChTutorial/RimSurface");
                if (rim != null)
                {
                    var pm = new Material(rim) { name = "ch_pocket_rim" };
                    pm.SetColor("_BaseColor", pocketSurfaceColor);
                    pocketSurface.sharedMaterial = pm;
                }
                else pocketSurface.sharedMaterial = HbStyle.Mat(pocketSurfaceColor, true, 0.15f);
                pocketSurface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // --- the reacting atoms
            var mr = Child(root, "Mechanism");
            mechanism = mr.gameObject.AddComponent<ChMechanism>();
            mechanism.stickRadius = stickRadius;
            mechanism.viewCamera = Camera.main;
            mechanism.Build(data);

            // --- camera targets
            targets = Child(root, "Targets");
            Child(targets, "center").position = root.TransformPoint(data.ToUnity(data.landmarks.center));
            Child(targets, "site").position = root.TransformPoint(data.ToUnity(data.landmarks.site));
            Child(targets, "pocket").position = root.TransformPoint(data.ToUnity(data.landmarks.pocket));
            var ne2 = mechanism.AtomTransform("His57:NE2"); var og = mechanism.AtomTransform("Ser195:OG"); var cg = mechanism.AtomTransform("Asp102:CG");
            if (ne2 && og && cg) Child(targets, "triad").position = (ne2.position + og.position + cg.position) / 3f;

            // --- labels (residue numbers as superscripts, Lehninger style)
            labels = Child(root, "Labels").gameObject.AddComponent<ChLabels>();
            labels.viewCamera = Camera.main; labels.centre = Target("site");
            TMPro.TMP_FontAsset font = TMPro.TMP_Settings.instance != null ? TMPro.TMP_Settings.defaultFontAsset : null;
            System.Func<string, Transform> A = n => mechanism.AtomTransform(n);
            labels.Add("Asp<sup>102</sup>", "triad", A("Asp102:CG"), Vector3.zero, font);
            labels.Add("His<sup>57</sup>", "triad", A("His57:CE1"), Vector3.zero, font);
            labels.Add("Ser<sup>195</sup>", "triad", A("Ser195:CB"), Vector3.zero, font);
            labels.Add("Gly<sup>193</sup> N–H", "hole", A("Gly193:N"), Vector3.zero, font);
            labels.Add("Ser<sup>195</sup> N–H", "hole", A("Ser195:N"), Vector3.zero, font);
            labels.Add("oxyanion hole", "hole", A("S:O2"), new Vector3(0f, -0.06f, 0f), font);
            labels.Add("hydrophobic pocket", "pocket", Target("pocket"), new Vector3(0f, -0.12f, 0f), font);
            labels.Add("Phe", "pocket", A("S:CP4"), Vector3.zero, font);
            labels.Add("scissile bond", "scissile", A("S:C2"), Vector3.zero, font, A("L:N"));
            labels.Add("H<sub>2</sub>O", "water", A("W:O"), Vector3.zero, font);

#if UNITY_EDITOR
            PersistMaterials(root);
#endif
            Debug.Log($"ChSceneBuilder: built {chains.Count} chain tubes, {data.context.Length} context residues, {data.atoms.Length} reacting atoms, " +
                      $"{data.bonds.Length} bonds, {data.arrows.Length} arrows over {data.StateCount} states");
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            chains.Clear(); root = null; mechanism = null; zymogen = null; pocketRoot = null; activationRoot = null; targets = null;
        }

        /// <summary>Fade the chain tubes (1 = opaque, 0 = hidden).</summary>
        public void SetChainsAlpha(float a)
        {
            foreach (var t in chains) if (t != null) t.SetAlpha(a);
        }

        float _pocketAlpha = -1f;
        /// <summary>Fade the pocket surface (1 = its normal translucency, 0 = hidden). Sets the surface's own material
        /// color: a property block's alpha is not honored on these transparent materials in this project.</summary>
        public void SetPocketAlpha(float a)
        {
            if (pocketSurface == null) return;
            a = Mathf.Clamp01(a);
            bool on = a > 0.004f;
            if (pocketSurface.enabled != on) pocketSurface.enabled = on;
            if (!on || Mathf.Abs(a - _pocketAlpha) < 0.002f) return;
            _pocketAlpha = a;
            var m = pocketSurface.sharedMaterial; if (m == null) return;
            if (m.HasProperty("_Alpha")) { m.SetFloat("_Alpha", a); return; }
            var c = pocketSurfaceColor; c.a *= a;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        }

        public Transform Target(string name) { return targets != null ? targets.Find(name) : null; }

#if UNITY_EDITOR
        // Materials made in code are not saved with the scene, so a built player would lose them (and strip the
        // shaders). Save one asset per distinct material and point every renderer at the asset.
        static void PersistMaterials(Transform root)
        {
            const string folder = "Assets/ChTutorial/Materials";
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/ChTutorial")) UnityEditor.AssetDatabase.CreateFolder("Assets", "ChTutorial");
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/ChTutorial", "Materials");
            var map = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || UnityEditor.AssetDatabase.Contains(m)) continue;
                if (!map.TryGetValue(m, out var asset))
                {
                    string safe = string.IsNullOrEmpty(m.name) ? "mat" : m.name.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
                    if (!safe.StartsWith("hb_") && !safe.StartsWith("ch_")) safe = "ch_line_" + ColorUtility.ToHtmlStringRGBA(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white);
                    string path = folder + "/" + safe + ".mat";
                    asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (asset == null) { UnityEditor.AssetDatabase.CreateAsset(m, path); asset = m; }
                    else { asset.CopyPropertiesFromMaterial(m); asset.shader = m.shader; asset.renderQueue = m.renderQueue; asset.shaderKeywords = m.shaderKeywords; UnityEditor.EditorUtility.SetDirty(asset); }
                    map[m] = asset;
                }
                r.sharedMaterial = asset;
            }
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
