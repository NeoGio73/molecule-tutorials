// HbStyle.cs — colors, materials and shared meshes for the hemoglobin scene.
using System.Collections.Generic;
using UnityEngine;

namespace HbTutorial
{
    public static class HbStyle
    {
        // Chain colors: alpha1, beta1, alpha2, beta2 (muted so sticks and lines read on top).
        public static readonly Dictionary<string, Color> ChainColor = new Dictionary<string, Color>
        {
            { "alpha1", new Color(0.93f, 0.55f, 0.20f) },   // orange
            { "beta1",  new Color(0.25f, 0.55f, 0.90f) },   // blue
            { "alpha2", new Color(0.95f, 0.78f, 0.35f) },   // gold
            { "beta2",  new Color(0.45f, 0.80f, 0.85f) },   // teal
        };
        public static readonly Color GhostColor = new Color(0.8f, 0.8f, 0.85f, 0.18f);
        public static readonly Color IonPairColor = new Color(1.0f, 0.95f, 0.35f);
        public static readonly Color IonPairBrokenColor = new Color(1.0f, 0.3f, 0.2f);

        public static Color ElementColor(string elem)
        {
            switch (elem)
            {
                case "C":  return new Color(0.55f, 0.55f, 0.55f);
                case "N":  return new Color(0.20f, 0.30f, 1.00f);
                case "O":  return new Color(0.95f, 0.15f, 0.15f);
                case "S":  return new Color(0.95f, 0.85f, 0.20f);
                case "FE": return new Color(0.95f, 0.50f, 0.10f);
                default:   return new Color(0.75f, 0.45f, 0.85f);
            }
        }
        // Ball-and-stick radii in scene units (1 unit = 10 Å at the default scale).
        public static float ElementRadius(string elem)
        {
            switch (elem) { case "FE": return 0.055f; case "S": return 0.045f; case "C": return 0.034f; default: return 0.038f; }
        }

        static Mesh _sphere, _cylinder;
        public static Mesh Sphere { get { if (_sphere == null) _sphere = PrimitiveMesh(PrimitiveType.Sphere); return _sphere; } }
        public static Mesh Cylinder { get { if (_cylinder == null) _cylinder = PrimitiveMesh(PrimitiveType.Cylinder); return _cylinder; } }

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return m;
        }

        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        // One material per (color, transparency, emission) so batching stays sane.
        public static Material Mat(Color c, bool transparent = false, float emission = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + (transparent ? "T" : "O") + emission.ToString("F2");
            if (_mats.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            bool urp = shader != null;
            if (!urp) shader = Shader.Find("Standard");
            m = new Material(shader) { name = "hb_" + key };
            if (urp)
            {
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0.35f);
                if (transparent)
                {
                    // Straight alpha blending for URP Lit; URP's validator otherwise flips this to premultiplied
                    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (m.HasProperty("_SrcBlendAlpha")) { m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
                    m.SetFloat("_ZWrite", 0f);
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.SetShaderPassEnabled("ShadowCaster", false);
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
            }
            else
            {
                m.color = c;
                if (transparent)
                {
                    m.SetFloat("_Mode", 3f);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON");
                    m.renderQueue = 3000;
                }
                if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
            }
            _mats[key] = m;
            return m;
        }

        public static Material LineMat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            return m;
        }
    }
}
