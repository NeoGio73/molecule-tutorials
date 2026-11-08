// ChLabels.cs — 3D text labels for the chymotrypsin scene (residue names with superscript numbers, Lehninger style,
// and a few region names). Each label follows an anchor (a reacting atom or a target), sits a little outward from the
// middle of the active site as seen on screen, always faces the camera, and belongs to a group the shot keys fade in
// and out ("triad", "hole", "pocket", ...).
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace ChTutorial
{
    [ExecuteAlways]
    public class ChLabels : MonoBehaviour
    {
        [System.Serializable]
        public class Label
        {
            public string text;          // TextMeshPro rich text, e.g. "His<sup>57</sup>"
            public string group;
            public Transform anchor, anchor2;   // with anchor2 set, the label follows the midpoint (a bond)
            public Vector3 nudge;        // extra offset in camera space (x right, y up), scene units
            public TextMeshPro tmp;
        }

        public Camera viewCamera;
        public Transform centre;                     // labels are pushed away from this point on screen
        public float offset = 0.13f;                 // scene units from the anchor
        public float fontSize = 0.8f;
        public Color color = new Color(1f, 1f, 1f, 1f);
        public List<Label> labels = new List<Label>();
        public List<string> groupNames = new List<string>();
        public List<float> groupAlpha = new List<float>();

        public void Add(string text, string group, Transform anchor, Vector3 nudge, TMP_FontAsset font, Transform anchor2 = null)
        {
            var go = new GameObject(group + ": " + StripTags(text));
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = fontSize; t.alignment = TextAlignmentOptions.Center;
            t.color = color; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(2f, 0.4f);
            var overlay = Shader.Find("TextMeshPro/Distance Field Overlay");     // drawn on top, never hidden behind atoms
            var m = new Material(t.fontSharedMaterial) { name = "ch_label_overlay" };
            if (overlay != null) m.shader = overlay;
            m.SetColor("_FaceColor", new Color(2.2f, 2.2f, 2.2f, 1f));           // well above 1 so tone mapping leaves the text white
            m.SetFloat("_FaceDilate", 0.12f);
            m.SetFloat("_OutlineWidth", 0.3f); m.SetColor("_OutlineColor", Color.black); m.EnableKeyword("OUTLINE_ON");
            ShaderUtilities.UpdateShaderRatios(m);                         // TMP scales dilate/outline by these ratios
            t.fontSharedMaterial = m;
            labels.Add(new Label { text = text, group = group, anchor = anchor, anchor2 = anchor2, nudge = nudge, tmp = t });
            if (!groupNames.Contains(group)) { groupNames.Add(group); groupAlpha.Add(0f); }
        }

        static string StripTags(string s) { return System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", ""); }

        public void SetGroup(string group, float a)
        {
            int i = groupNames.IndexOf(group);
            if (i >= 0) groupAlpha[i] = Mathf.Clamp01(a);
        }
        public float GroupAlpha(string group) { int i = groupNames.IndexOf(group); return i >= 0 ? groupAlpha[i] : 0f; }

        void LateUpdate() { Apply(); }

        public void Apply()
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;
            Vector3 c = centre != null ? centre.position : transform.position;
            foreach (var l in labels)
            {
                if (l.tmp == null || l.anchor == null) continue;
                float a = GroupAlpha(l.group);
                // hide with the anchor (an atom that is not on screen in this state)
                if (!l.anchor.gameObject.activeInHierarchy || (l.anchor2 != null && !l.anchor2.gameObject.activeInHierarchy)) a = 0f;
                bool on = a > 0.004f;
                if (l.tmp.gameObject.activeSelf != on) l.tmp.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 p = l.anchor2 != null ? (l.anchor.position + l.anchor2.position) * 0.5f : l.anchor.position, d = p - c;
                d -= Vector3.Dot(d, cam.transform.forward) * cam.transform.forward;   // in the screen plane
                d = d.sqrMagnitude > 1e-8f ? d.normalized : cam.transform.up;
                l.tmp.transform.position = p + d * offset + cam.transform.right * l.nudge.x + cam.transform.up * l.nudge.y
                                           - cam.transform.forward * 0.05f;
                l.tmp.transform.rotation = cam.transform.rotation;
                var col = color; col.a = a; l.tmp.color = col;
            }
        }
    }
}
