// HbSceneBuilder.cs — builds the whole hemoglobin hierarchy from hemoglobin.json.
// Attach to an empty GameObject, assign the JSON TextAsset, then Inspector menu (⋮) -> Build.
// Everything it creates lives under this object, so Clear removes it cleanly.
using System.Collections.Generic;
using UnityEngine;

namespace HbTutorial
{
    public class HbSceneBuilder : MonoBehaviour
    {
        public TextAsset json;
        [Header("Look")]
        public float tubeRadius = 0.075f;
        public int tubeSides = 12;
        public int tubeSubdiv = 6;
        public float stickRadius = 0.016f;
        public float atomScale = 1.0f;
        public float hemeAtomScale = 1.15f;

        [Header("Built objects (filled by Build)")]
        public HbMorphController controller;
        public Transform targets;

        [System.NonSerialized] public HbDataset data;
        readonly Dictionary<string, StickGroup> _residueGroups = new Dictionary<string, StickGroup>();

        [ContextMenu("Build")]
        public void Build()
        {
            Clear();
            if (json == null) { Debug.LogError("HbSceneBuilder: assign hemoglobin.json first"); return; }
            data = HbDataset.Load(json);

            var root = new GameObject("Hemoglobin").transform;
            root.SetParent(transform, false);
            controller = root.gameObject.AddComponent<HbMorphController>();

            // --- chains and their T-state ghosts
            var chains = Child(root, "Chains"); var ghost = Child(root, "Ghost");
            foreach (var c in data.chains)
            {
                var T = data.ToUnityArray(c.caT); var R = data.ToUnityArray(c.caR);
                var col = HbStyle.ChainColor.TryGetValue(c.role, out var cc) ? cc : Color.gray;
                var tube = Child(chains, c.label + " (" + c.role + ")").gameObject.AddComponent<ChainTube>();
                tube.radius = tubeRadius; tube.sides = tubeSides; tube.subdiv = tubeSubdiv;
                tube.Init(T, R, col);
                var g = Child(ghost, c.label + " ghost").gameObject.AddComponent<ChainTube>();
                g.radius = tubeRadius * 1.02f; g.sides = tubeSides; g.subdiv = tubeSubdiv;
                g.Init(T, R, HbStyle.GhostColor, ghost: true);
            }
            ghost.gameObject.SetActive(false);

            // --- hemes
            var hemes = Child(root, "Hemes");
            var hemeCarbon = new Color(0.30f, 0.30f, 0.32f);
            foreach (var h in data.hemes)
            {
                var sg = Child(hemes, "heme " + h.role).gameObject.AddComponent<StickGroup>();
                sg.stickRadius = stickRadius; sg.atomScale = hemeAtomScale; sg.tag = "heme"; sg.exaggerateOnly = "FE";
                sg.Init(data, h.atoms, h.bonds, "heme:" + h.role, false, hemeCarbon);
                _residueGroups["heme:" + h.role] = sg;
            }

            // --- O2 (R only)
            var o2 = Child(root, "O2");
            foreach (var l in data.ligands)
            {
                var sg = Child(o2, l.name + " " + l.role).gameObject.AddComponent<StickGroup>();
                sg.stickRadius = stickRadius; sg.atomScale = hemeAtomScale;
                sg.Init(data, l.atoms, l.bonds, "o2:" + l.role, rOnly: true);
            }

            // --- named residues, carbons tinted by chain
            var residues = Child(root, "Residues");
            foreach (var r in data.residues)
            {
                var tint = HbStyle.ChainColor.TryGetValue(r.role, out var cc) ? Color.Lerp(cc, Color.gray, 0.35f) : Color.gray;
                var sg = Child(residues, r.label + " [" + r.tag + "]").gameObject.AddComponent<StickGroup>();
                sg.stickRadius = stickRadius; sg.atomScale = atomScale; sg.clickable = true; sg.tag = r.tag;
                sg.Init(data, r.atoms, r.bonds, r.key, false, tint);
                _residueGroups[r.key] = sg;
            }

            // --- ion pairs
            var pairs = Child(root, "IonPairs");
            foreach (var p in data.ionPairs)
            {
                if (!_residueGroups.TryGetValue(p.fromKey, out var ga) || !_residueGroups.TryGetValue(p.toKey, out var gb)) continue;
                var a = ga.AtomTransform(p.fromAtom); var b = gb.AtomTransform(p.toAtom);
                if (a == null || b == null) continue;
                var go = Child(pairs, p.name + " (" + p.side + ")").gameObject;
                go.AddComponent<LineRenderer>();
                go.AddComponent<IonPairLine>().Init(p, a, b);
            }

            // --- Fe–His F8 coordination bonds (permanent links, drawn under Hemes so they toggle with them)
            foreach (var h in data.hemes)
            {
                string hisKey = h.role + ":" + (h.role.StartsWith("alpha") ? 87 : 92);
                if (!_residueGroups.TryGetValue("heme:" + h.role, out var hg) || !_residueGroups.TryGetValue(hisKey, out var hisg)) continue;
                var fe = hg.AtomTransform("FE"); var ne2 = hisg.AtomTransform("NE2");
                if (fe == null || ne2 == null) continue;
                hisg.followExaggerationOf = hg; hisg.followAtom = "FE";
                var go = Child(hemes, "Fe–His F8 " + h.role).gameObject;
                go.AddComponent<LineRenderer>();
                var link = go.AddComponent<IonPairLine>();
                link.breaks = false; link.color = new Color(0.85f, 0.85f, 0.9f); link.widthT = 0.018f;
                link.Init(new HbIonPair { name = "Fe–His F8", side = h.role, distT = h.feHisDistT, distR = h.feHisDistR }, fe, ne2);
            }

            // --- sickle (off by default)
            var sickle = Child(root, "Sickle");
            if (data.sickle != null && data.sickle.tetramers != null && data.sickle.tetramers.Length > 0)
            {
                foreach (var tet in data.sickle.tetramers)
                {
                    var tr = Child(sickle, "Tetramer " + tet.index);
                    foreach (var c in tet.chains)
                    {
                        var pts = data.ToUnityArray(c.ca);
                        var col = HbStyle.ChainColor.TryGetValue(c.role, out var cc) ? cc : Color.gray;
                        if (tet.index > 0) col = Color.Lerp(col, Color.white, 0.35f);
                        var tube = Child(tr, c.label + " (" + c.id + ")").gameObject.AddComponent<ChainTube>();
                        tube.radius = tubeRadius; tube.sides = tubeSides; tube.subdiv = tubeSubdiv;
                        tube.Init(pts, pts, col);
                        tube.isStatic = true;
                    }
                }
                var v6 = Child(sickle, "Val6");
                foreach (var r in data.sickle.val6)
                {
                    var sg = Child(v6, r.resname + r.resnum + " " + r.chain + " (tet " + r.tetramer + ")").gameObject.AddComponent<StickGroup>();
                    sg.stickRadius = stickRadius; sg.atomScale = atomScale * 1.3f;
                    sg.Init(data, r.atoms, r.bonds, "sickle:val6:" + r.chain, false, new Color(0.95f, 0.85f, 0.2f));
                }
                var pocket = Child(sickle, "Pocket");
                foreach (var r in data.sickle.pocket)
                {
                    var sg = Child(pocket, r.resname + r.resnum + " " + r.chain + " (tet " + r.tetramer + ")").gameObject.AddComponent<StickGroup>();
                    sg.stickRadius = stickRadius; sg.atomScale = atomScale;
                    sg.Init(data, r.atoms, r.bonds, "sickle:pocket:" + r.chain + ":" + r.resnum, false, new Color(0.4f, 0.75f, 0.4f));
                }
            }
            sickle.gameObject.SetActive(false);

            // --- camera targets for the shot list
            targets = Child(root, "Targets");
            foreach (var h in data.hemes)
            {
                var fe = h.atoms[h.feIndex];
                Child(targets, "heme " + h.role).position = root.TransformPoint(data.ToUnity(fe.T));
            }
            Child(targets, "cavity (centre)").position = root.position;
            if (_residueGroups.TryGetValue("beta1:146", out var his) && _residueGroups.TryGetValue("beta1:94", out var asp))
            {
                var a = his.AtomTransform("NE2"); var b = asp.AtomTransform("CG");
                if (a && b) Child(targets, "bohr beta1").position = (a.position + b.position) * 0.5f;
            }
            if (_residueGroups.TryGetValue("alpha1:141", out var arg) && _residueGroups.TryGetValue("alpha2:126", out var asp2))
            {
                var a = arg.AtomTransform("CZ"); var b = asp2.AtomTransform("CG");
                if (a && b) Child(targets, "interface a1b2").position = (a.position + b.position) * 0.5f;
            }

            controller.chainsRoot = chains; controller.ghostRoot = ghost; controller.hemesRoot = hemes;
            controller.o2Root = o2; controller.residuesRoot = residues; controller.ionPairsRoot = pairs; controller.sickleRoot = sickle;
            controller.Rescan(); controller.Apply();
#if UNITY_EDITOR
            PersistMaterials(root);
#endif

            Debug.Log($"HbSceneBuilder: built {data.chains.Length} chains, {data.hemes.Length} hemes, {data.residues.Length} residues, " +
                      $"{data.ionPairs.Length} ion pairs; alpha2beta2 rotation {data.metrics.rotationAlpha2Beta2Deg:F1} deg");
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            _residueGroups.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            controller = null; targets = null;
        }

#if UNITY_EDITOR
        // Materials made in code are not saved with the scene, so a built player would lose them (and strip the
        // shaders). Save one asset per distinct material and point every renderer at the asset.
        static void PersistMaterials(Transform root)
        {
            const string folder = "Assets/HbTutorial/Materials";
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/HbTutorial")) UnityEditor.AssetDatabase.CreateFolder("Assets", "HbTutorial");
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/HbTutorial", "Materials");
            var map = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null) continue;
                if (UnityEditor.AssetDatabase.Contains(m)) continue;
                if (!map.TryGetValue(m, out var asset))
                {
                    string safe = string.IsNullOrEmpty(m.name) ? "mat" : m.name.Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
                    if (!safe.StartsWith("hb_")) safe = "hb_line_" + ColorUtility.ToHtmlStringRGBA(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white);
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
