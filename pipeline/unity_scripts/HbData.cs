// HbData.cs — data classes matching hemoglobin.json written by hb_prep.py.
// Plain JsonUtility: public fields, flat float arrays, no dictionaries.
using System;
using UnityEngine;

namespace HbTutorial
{
    [Serializable] public class HbMeta
    {
        public string units; public float unityScale = 0.1f; public bool mirrorZ = true;
        public string sourceT, sourceR, sourceS, generated; public string[] notes;
    }

    [Serializable] public class HbAtom
    {
        public string name, elem;
        public float[] T;   // x,y,z in angstrom (empty for R-only ligands)
        public float[] R;   // x,y,z in angstrom (empty for sickle-only residues)
    }

    [Serializable] public class HbChain
    {
        public string id, idR, role, label, kind; public int nres;
        public int[] resnum; public string[] resname;
        public float[] caT, caR;  // flat xyz, same length
    }

    [Serializable] public class HbHeme
    {
        public string chain, role; public HbAtom[] atoms; public int[] bonds;
        public int feIndex; public int[] planeN;
        public float feOffPlaneT, feOffPlaneR, feHisDistT, feHisDistR;
    }

    [Serializable] public class HbLigand { public string chain, role, name; public HbAtom[] atoms; public int[] bonds; }

    [Serializable] public class HbResidue
    {
        public string key, chain, role, resname; public int resnum; public string label, tag;
        public HbAtom[] atoms; public int[] bonds;
    }

    [Serializable] public class HbIonPair
    {
        public string name, side, fromKey, fromAtom, toKey, toAtom; public float distT, distR;
    }

    [Serializable] public class HbMetrics { public float rmsdAlpha1Beta1, rotationAlpha2Beta2Deg; }

    [Serializable] public class HbSickleChain { public string id, role, label, kind; public int[] resnum; public float[] ca; }
    [Serializable] public class HbSickleTetramer { public int index; public HbSickleChain[] chains; }
    [Serializable] public class HbSickleResidue
    {
        public int tetramer; public string chain, role, resname; public int resnum; public HbAtom[] atoms; public int[] bonds;
    }
    [Serializable] public class HbSickleContact
    {
        public int val6Tetramer; public string val6Chain; public int pocketTetramer; public string pocketChain, pocketResname;
        public int pocketResnum; public float minDist;
    }
    [Serializable] public class HbSickle
    {
        public string source; public HbSickleTetramer[] tetramers; public HbSickleResidue[] val6, pocket; public HbSickleContact[] contacts;
    }

    [Serializable] public class HbDataset
    {
        public HbMeta meta; public HbChain[] chains; public HbHeme[] hemes; public HbLigand[] ligands;
        public HbResidue[] residues; public HbIonPair[] ionPairs; public HbMetrics metrics; public HbSickle sickle;

        public static HbDataset Load(TextAsset json)
        {
            var d = JsonUtility.FromJson<HbDataset>(json.text);
            if (d == null || d.chains == null || d.chains.Length == 0)
                throw new Exception("hemoglobin.json did not parse; regenerate it with hb_prep.py");
            return d;
        }

        // Angstrom -> scene units, with the right-to-left-handed flip.
        public Vector3 ToUnity(float x, float y, float z)
        {
            float s = meta != null ? meta.unityScale : 0.1f;
            bool m = meta == null || meta.mirrorZ;
            return new Vector3(x * s, y * s, (m ? -z : z) * s);
        }
        /// <summary>One point from a 3-element array.</summary>
        public Vector3 ToUnity(float[] xyz) { return ToUnity(xyz[0], xyz[1], xyz[2]); }
        /// <summary>Point i from a flat x,y,z,x,y,z,... array.</summary>
        public Vector3 ToUnityAt(float[] flat, int i) { return ToUnity(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]); }
        /// <summary>Every point from a flat array.</summary>
        public Vector3[] ToUnityArray(float[] flat)
        {
            int n = flat.Length / 3; var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = ToUnityAt(flat, i);
            return v;
        }
    }
}
