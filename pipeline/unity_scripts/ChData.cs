// ChData.cs — data classes matching chymotrypsin.json written by chymo_prep.py.
// Plain JsonUtility: public fields, flat float arrays, no dictionaries.
using System;
using UnityEngine;

namespace ChTutorial
{
    [Serializable] public class ChMeta
    {
        public string units, frame, generated; public float unityScale = 0.1f; public bool mirrorZ = true;
        public string[] states; public string[] notes;
    }
    [Serializable] public class ChLandmarks { public float[] site, pocket, center; }
    [Serializable] public class ChTrace { public int[] resnum; public float[] ca; }
    [Serializable] public class ChZymogen { public int[] resnum; public float[] ca, caActive; public int[] inActive; }
    [Serializable] public class ChCtxAtom { public string name, elem; public float[] P; }
    /// <summary>Surface patch lining the hydrophobic pocket: flat vertices (angstrom) and triangles.</summary>
    [Serializable] public class ChSurface { public float[] v; public int[] t; }
    [Serializable] public class ChContext { public string key, resname, tag; public int resnum; public ChCtxAtom[] atoms; public int[] bonds; }
    /// <summary>A reacting atom: P holds one x,y,z per mechanism state; vis is 1 where the atom is on screen.</summary>
    [Serializable] public class ChAtom { public string name, elem, cls, res; public int[] vis; public float[] P; }
    /// <summary>order per state: 0 none, 0.5 hydrogen bond, 1 single, 2 double.</summary>
    [Serializable] public class ChBond { public int a, b; public float[] order; }
    [Serializable] public class ChCharge { public int atom; public int[] charge; }
    /// <summary>A curved arrow of one state: from an atom's lone pair (fromB = -1) or a bond, to an atom (toB = -1) or a bond.</summary>
    [Serializable] public class ChArrowDef { public int state, fromA, fromB, toA, toB; }

    [Serializable] public class ChDataset
    {
        public ChMeta meta; public ChLandmarks landmarks; public ChTrace[] trace; public ChZymogen zymogen;
        public ChContext[] context; public ChSurface pocketSurface; public ChAtom[] atoms; public ChBond[] bonds; public ChCharge[] charges; public ChArrowDef[] arrows;

        public int StateCount { get { return meta != null && meta.states != null && meta.states.Length > 0 ? meta.states.Length : 8; } }

        public static ChDataset Load(TextAsset json)
        {
            var d = JsonUtility.FromJson<ChDataset>(json.text);
            if (d == null || d.atoms == null || d.atoms.Length == 0)
                throw new Exception("chymotrypsin.json did not parse; regenerate it with chymo_prep.py");
            return d;
        }

        // Angstrom -> scene units, with the right-to-left-handed flip (same convention as hemoglobin).
        public Vector3 ToUnity(float x, float y, float z)
        {
            float s = meta != null ? meta.unityScale : 0.1f;
            bool m = meta == null || meta.mirrorZ;
            return new Vector3(x * s, y * s, (m ? -z : z) * s);
        }
        public Vector3 ToUnity(float[] xyz) { return ToUnity(xyz[0], xyz[1], xyz[2]); }
        public Vector3 ToUnityAt(float[] flat, int i) { return ToUnity(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]); }
        public Vector3[] ToUnityArray(float[] flat)
        {
            int n = flat.Length / 3; var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = ToUnityAt(flat, i);
            return v;
        }
    }
}
