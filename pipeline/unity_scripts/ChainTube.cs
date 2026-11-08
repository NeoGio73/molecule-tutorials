// ChainTube.cs — a smooth tube through the C-alpha trace of one chain, morphing between T and R.
using UnityEngine;

namespace HbTutorial
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ChainTube : HbMorphable
    {
        public Vector3[] ptsT, ptsR;      // control points (scene units)
        public float radius = 0.09f;
        public int sides = 10;
        public int subdiv = 5;            // samples per control segment
        public bool isStatic;             // ghost copies never morph
        public Color color = Color.gray;  // serialized so materials can be rebuilt after a domain reload
        // Saved transparent URP Lit material used while the tube fades (its color comes from a property block). It has to be a
        // saved asset the scene references: a material made at runtime needs the transparent shader variant, which a
        // WebGL build strips when no saved material uses it (the tube then draws opaque).
        public Material transparentMaterial;

        Mesh _mesh;
        Material _opaque, _transp;
        MaterialPropertyBlock _mpb;
        float _alpha = 1f;
        Vector3[] _work;
        float _lastT = -1f;

        public void Init(Vector3[] T, Vector3[] R, Color color, bool ghost = false)
        {
            ptsT = T; ptsR = R; isStatic = ghost; this.color = color;
            _mesh = new Mesh { name = name + "_tube" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            GetComponent<MeshRenderer>().sharedMaterial = ghost ? HbStyle.Mat(color, true) : HbStyle.Mat(color);
#if UNITY_EDITOR
            if (transparentMaterial == null) transparentMaterial = EditorTransparentMaterial();
#endif
            if (ghost) { var r = GetComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            SetT(0f);
        }

        /// <summary>0 = hidden, 1 = opaque, between = translucent (used to fade the chains out around a close-up).</summary>
        public void SetAlpha(float a)
        {
            a = Mathf.Clamp01(a);
            var mr = GetComponent<MeshRenderer>();
            if (isStatic) return;
            if (_opaque == null) _opaque = (mr.sharedMaterial != null && mr.sharedMaterial != _transp) ? mr.sharedMaterial : HbStyle.Mat(color);
            if (_transp == null) _transp = transparentMaterial != null ? transparentMaterial : HbStyle.Mat(new Color(color.r, color.g, color.b, 0.5f), true);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _alpha = a;
            if (a >= 0.995f) { mr.sharedMaterial = _opaque; mr.SetPropertyBlock(null); mr.enabled = true; }
            else if (a <= 0.005f) { mr.enabled = false; }
            else
            {
                mr.sharedMaterial = _transp;
                _mpb.SetColor("_BaseColor", new Color(color.r, color.g, color.b, a));
                mr.SetPropertyBlock(_mpb);
                mr.enabled = true;
            }
        }

#if UNITY_EDITOR
        public const string TransparentAssetPath = "Assets/HbTutorial/Materials/ChainTransparent.mat";

        /// <summary>The shared transparent chain material asset (created on first use).</summary>
        public static Material EditorTransparentMaterial()
        {
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(TransparentAssetPath);
            if (m != null) return m;
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/HbTutorial/Materials")) UnityEditor.AssetDatabase.CreateFolder("Assets/HbTutorial", "Materials");
            m = new Material(HbStyle.Mat(new Color(1f, 1f, 1f, 0.5f), true)) { name = "ChainTransparent" };
            UnityEditor.AssetDatabase.CreateAsset(m, TransparentAssetPath);
            return m;
        }
#endif

        public override void SetT(float t)
        {
            if (ptsT == null || ptsR == null) return;
            if (isStatic) t = 0f;
            if (_mesh == null)   // after a domain reload the mesh reference is gone; reuse or recreate
            {
                var mf = GetComponent<MeshFilter>();
                _mesh = mf.sharedMesh;
                if (_mesh == null) { _mesh = new Mesh { name = name + "_tube" }; _mesh.MarkDynamic(); mf.sharedMesh = _mesh; }
                _lastT = -1f;
            }
            if (Mathf.Approximately(t, _lastT)) return;
            _lastT = t;
            int n = Mathf.Min(ptsT.Length, ptsR.Length);
            if (_work == null || _work.Length != n) _work = new Vector3[n];
            for (int i = 0; i < n; i++) _work[i] = Vector3.LerpUnclamped(ptsT[i], ptsR[i], t);
            TubeMesh.Build(_work, radius, sides, subdiv, _mesh);
        }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }
    }

    /// <summary>Catmull-Rom spline tube with parallel-transport frames (no twist).</summary>
    public static class TubeMesh
    {
        public static void Build(Vector3[] pts, float radius, int sides, int subdiv, Mesh mesh)
        {
            int n = pts.Length;
            if (n < 2) { mesh.Clear(); return; }
            int segs = (n - 1) * subdiv + 1;
            var centers = new Vector3[segs];
            var tangents = new Vector3[segs];
            int k = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, n - 1)];
                for (int s = 0; s < subdiv; s++)
                {
                    float u = s / (float)subdiv;
                    centers[k] = CatmullRom(p0, p1, p2, p3, u);
                    tangents[k] = CatmullRomTangent(p0, p1, p2, p3, u).normalized;
                    k++;
                }
            }
            centers[k] = pts[n - 1];
            tangents[k] = (pts[n - 1] - pts[n - 2]).normalized;

            // parallel transport
            var normals = new Vector3[segs];
            Vector3 nrm = Vector3.Cross(tangents[0], Mathf.Abs(tangents[0].y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            normals[0] = nrm;
            for (int i = 1; i < segs; i++)
            {
                Vector3 b = Vector3.Cross(tangents[i - 1], tangents[i]);
                if (b.sqrMagnitude > 1e-10f)
                {
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(tangents[i - 1], tangents[i]), -1f, 1f));
                    nrm = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, b.normalized) * nrm;
                }
                nrm = (nrm - Vector3.Dot(nrm, tangents[i]) * tangents[i]).normalized;
                normals[i] = nrm;
            }

            int vcount = segs * sides + 2;
            var verts = new Vector3[vcount];
            var vnorm = new Vector3[vcount];
            var uv = new Vector2[vcount];
            for (int i = 0; i < segs; i++)
            {
                Vector3 bin = Vector3.Cross(tangents[i], normals[i]);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    Vector3 dir = Mathf.Cos(a) * normals[i] + Mathf.Sin(a) * bin;
                    int vi = i * sides + j;
                    verts[vi] = centers[i] + dir * radius;
                    vnorm[vi] = dir;
                    uv[vi] = new Vector2(j / (float)sides, i / (float)(segs - 1));
                }
            }
            int capA = segs * sides, capB = capA + 1;
            verts[capA] = centers[0]; vnorm[capA] = -tangents[0];
            verts[capB] = centers[segs - 1]; vnorm[capB] = tangents[segs - 1];

            var tris = new int[(segs - 1) * sides * 6 + sides * 6];
            int ti = 0;
            for (int i = 0; i < segs - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j, b = i * sides + (j + 1) % sides, c = a + sides, d = b + sides;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            for (int j = 0; j < sides; j++)
            {
                tris[ti++] = capA; tris[ti++] = (j + 1) % sides; tris[ti++] = j;
                int baseB = (segs - 1) * sides;
                tris[ti++] = capB; tris[ti++] = baseB + j; tris[ti++] = baseB + (j + 1) % sides;
            }

            mesh.Clear();
            mesh.vertices = verts; mesh.normals = vnorm; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
        static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            return 0.5f * ((-p0 + p2) + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t + 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2);
        }
    }
}
