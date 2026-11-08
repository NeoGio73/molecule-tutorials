// ChMeshes.cs — small procedural meshes for the chymotrypsin scene: an arrowhead cone, a ring and a bar for the
// circled formal charges. Built once per Build and kept on the objects that use them (saved with the scene).
using UnityEngine;

namespace ChTutorial
{
    public static class ChMeshes
    {
        /// <summary>Cone along +Z: base circle of radius 1 at z = 0, tip at z = 1.</summary>
        public static Mesh Cone(int sides = 16)
        {
            var m = new Mesh { name = "ch_cone" };
            var v = new Vector3[sides * 2 + 2]; var n = new Vector3[v.Length]; var t = new int[sides * 6];
            int tip = sides * 2, baseC = tip + 1;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[i] = p; n[i] = (p + new Vector3(0f, 0f, 0.5f)).normalized;          // side ring
                v[sides + i] = p; n[sides + i] = Vector3.back;                       // base ring
            }
            v[tip] = Vector3.forward; n[tip] = Vector3.forward; v[baseC] = Vector3.zero; n[baseC] = Vector3.back;
            int k = 0;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t[k++] = i; t[k++] = tip; t[k++] = j;
                t[k++] = sides + j; t[k++] = baseC; t[k++] = sides + i;
            }
            m.vertices = v; m.normals = n; m.triangles = t; m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat ring in the XY plane between radius inner and 1, facing -Z (toward a camera looking down +Z).</summary>
        public static Mesh Ring(float inner = 0.78f, int seg = 40)
        {
            var m = new Mesh { name = "ch_ring" };
            var v = new Vector3[seg * 2]; var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v[2 * i] = d * inner; v[2 * i + 1] = d;
            }
            int k = 0;
            for (int i = 0; i < seg; i++)
            {
                int a0 = 2 * i, a1 = 2 * i + 1, b0 = 2 * ((i + 1) % seg), b1 = b0 + 1;
                t[k++] = a0; t[k++] = b0; t[k++] = a1;
                t[k++] = a1; t[k++] = b0; t[k++] = b1;
            }
            m.vertices = v; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit quad in the XY plane centred on the origin (1 wide, 1 tall), facing -Z.</summary>
        public static Mesh Quad()
        {
            var m = new Mesh { name = "ch_quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A point on a cubic Bezier curve and its tangent.</summary>
        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }
        public static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }
    }
}
