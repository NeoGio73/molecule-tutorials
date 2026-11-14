// HbPanelOverlay.cs — 2D panels (ChemDraw figures, the binding curve) that slide in over the 3D scene
// during set stretches of the shot sequence. The canvas is drawn by the scene camera (Screen Space - Camera),
// so the flat video recorder captures it; the student UI stays on its own overlay canvas and is not recorded.
// The cards are drawn by a URP overlay camera stacked on the scene camera, with post-processing off, so the
// tone mapping and bloom that suit the molecule do not turn the white cards gray.
// Panels show only while the sequence is driving the camera (replay); in free look they are hidden.
// The card (Nov 8, 2026): a rounded white card with a dark title strip (a small kicker line over the title) and the
// drawing below. The drawings no longer carry their own titles; the strip does, from Titles below unless a panel
// sets its own kicker and title.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace HbTutorial
{
    public class HbPanelOverlay : MonoBehaviour
    {
        [System.Serializable]
        public class Panel
        {
            public string label;
            public Texture2D image;
            public float start, end;                 // sequence seconds; fades happen inside [start, end]
            [Range(0.2f, 0.6f)] public float width = 0.36f;   // fraction of screen width
            public bool left;                        // dock on the left instead of the right
            public string kicker;                    // optional: the small line above the title (default from Titles)
            public string title;                     // optional: the card title; <sup> and <sub> allowed (default from Titles)
        }

        /// <summary>Card strip text per panel image (file name without a trailing _v2): kicker, title.</summary>
        public static readonly Dictionary<string, string[]> Titles = new Dictionary<string, string[]>
        {
            { "ch_p01_free_enzyme", new[] { "Mechanism · Lehninger Fig. 6-27", "Free enzyme" } },
            { "ch_p02_hydrophobic_pocket", new[] { "Substrate specificity", "The hydrophobic pocket" } },
            { "ch_p03_es_complex", new[] { "Mechanism · Lehninger Fig. 6-27", "ES complex" } },
            { "ch_p04_intermediate_acylation", new[] { "Mechanism · Lehninger Fig. 6-27", "Short-lived intermediate (acylation)" } },
            { "ch_p05_acyl_enzyme", new[] { "Mechanism · Lehninger Fig. 6-27", "Acyl-enzyme intermediate" } },
            { "ch_p06_acyl_enzyme_water", new[] { "Mechanism · Lehninger Fig. 6-27", "Acyl-enzyme intermediate" } },
            { "ch_p07_intermediate_deacylation", new[] { "Mechanism · Lehninger Fig. 6-27", "Short-lived intermediate (deacylation)" } },
            { "ch_p07b_enzyme_product2", new[] { "Mechanism · Lehninger Fig. 6-27", "Enzyme–product 2 complex" } },
            { "ch_p08_burst", new[] { "Kinetics · Lehninger Fig. 6-25", "Pre-steady-state burst" } },
            { "ch_p09_activation", new[] { "Zymogen activation · Lehninger Fig. 6-42", "Activation of chymotrypsinogen" } },
            { "hb_p01_heme_top", new[] { "The heme", "Heme b (Fe-protoporphyrin IX)" } },
            { "hb_p02_heme_side", new[] { "Oxygen binding", "The iron moves into the heme plane" } },
            { "hb_p04_bohr_pair", new[] { "The Bohr effect", "His<sup>146</sup>β–Asp<sup>94</sup>β ion pair" } },
            { "hb_p07_binding_curve", new[] { "Cooperativity", "O<sub>2</sub> binding: hemoglobin and myoglobin" } },
            { "hb_p08_ion_network", new[] { "Lehninger Fig. 5-9", "T-state ion pairs, one interface" } },
        };

        public Camera cam;
        public List<Panel> panels = new List<Panel>();
        public float fade = 0.6f;                    // seconds
        public float margin = 0.03f;                 // fraction of screen width from the edge
        public float slide = 60f;                    // reference pixels the card travels while fading in
        public bool show = true;                     // turn off for the 360 export
        [Range(0f, 0.45f)] public float reframe = 0.3f;   // how far the subject slides away from the card (fraction of half-width)
        public string layerName = "Panels";
        // Video layout: card on the side, subject reframed. Interactive layout (default, for the WebGL twin): card
        // in the top-left corner, clear of the student controls (top right) and replay buttons (bottom left), no reframing.
        public bool videoLayout = false;
        [Range(0.2f, 0.6f)] public float twinWidth = 0.42f;    // widest card in the interactive layout (wide panels)
        [Range(0.3f, 0.8f)] public float twinMaxHeight = 0.62f; // tallest card; keeps it above the replay buttons
        public Color stripColor = new Color(0.094f, 0.173f, 0.290f);
        public Color accentColor = new Color(0.43f, 0.67f, 1f);

        const float RefW = 1920f, RefH = 1080f, Pad = 22f, Strip = 74f, Radius = 16f;
        Canvas _canvas; RectTransform _card, _imgRT; RawImage _img; CanvasGroup _group; Camera _panelCam;
        GameObject _stripRoot; TextMeshProUGUI _kicker, _title; Panel _shown;
        float _shift;
        /// <summary>Signed sideways reframing for the scene camera: +1 = card fully up on the right.</summary>
        public float Shift { get { return _shift; } }

        void Awake() { Build(); UpdateAt(-1f); }

        void Build()
        {
            if (_canvas != null) return;
            if (cam == null) cam = Camera.main;
            int layer = LayerMask.NameToLayer(layerName); if (layer < 0) layer = 31;
            // overlay camera: renders only the panel layer, after the scene camera, without post-processing
            var pc = new GameObject("PanelCamera", typeof(Camera));
            pc.transform.SetParent(cam.transform, false);
            _panelCam = pc.GetComponent<Camera>();
            _panelCam.cullingMask = 1 << layer; _panelCam.nearClipPlane = 0.05f; _panelCam.farClipPlane = 10f;
            var pcd = _panelCam.GetUniversalAdditionalCameraData();
            pcd.renderType = CameraRenderType.Overlay; pcd.renderPostProcessing = false; pcd.renderShadows = false;
            var bd = cam.GetUniversalAdditionalCameraData();
            if (!bd.cameraStack.Contains(_panelCam)) bd.cameraStack.Add(_panelCam);
            cam.cullingMask &= ~(1 << layer);

            var go = new GameObject("PanelCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _panelCam;
            _canvas.planeDistance = 1f;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 1f;

            var round = RoundedSprite();
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Shadow));
            card.transform.SetParent(go.transform, false);
            _card = card.GetComponent<RectTransform>();
            var bg = card.GetComponent<Image>(); bg.sprite = round; bg.type = Image.Type.Sliced; bg.color = Color.white; bg.raycastTarget = false;
            var sh = card.GetComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.35f); sh.effectDistance = new Vector2(4f, -4f);
            _group = card.GetComponent<CanvasGroup>(); _group.interactable = false; _group.blocksRaycasts = false;

            // title strip: rounded on top; a white band hides its rounded bottom, and a thin accent line closes it
            _stripRoot = new GameObject("Strip", typeof(RectTransform));
            _stripRoot.transform.SetParent(card.transform, false);
            Stretch(_stripRoot.GetComponent<RectTransform>(), 0f, 0f);
            var strip = Box(_stripRoot.transform, "Band", stripColor, 0f, Strip + Radius); strip.sprite = round; strip.type = Image.Type.Sliced;
            Box(_stripRoot.transform, "Cover", Color.white, Strip, Radius);
            Box(_stripRoot.transform, "Accent", accentColor, Strip - 3f, 3f);
            var font = TMP_Settings.defaultFontAsset;
            _kicker = Label(_stripRoot.transform, "Kicker", font, 18f, accentColor, 9f, 24f); _kicker.characterSpacing = 3f;
            _title = Label(_stripRoot.transform, "Title", font, 27f, Color.white, 33f, 36f);
            _title.enableAutoSizing = true; _title.fontSizeMin = 16f; _title.fontSizeMax = 27f;

            var im = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            im.transform.SetParent(card.transform, false);
            _img = im.GetComponent<RawImage>(); _img.raycastTarget = false;
            _imgRT = im.GetComponent<RectTransform>();
            _imgRT.anchorMin = Vector2.zero; _imgRT.anchorMax = Vector2.one;
            _imgRT.offsetMin = new Vector2(Pad, Pad); _imgRT.offsetMax = new Vector2(-Pad, -Pad);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static void Stretch(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top); rt.sizeDelta = new Vector2(0f, height);
        }

        static Image Box(Transform parent, string name, Color col, float top, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>(), top, height);
            var img = go.GetComponent<Image>(); img.color = col; img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset font, float size, Color col, float top, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); Stretch(rt, top, height);
            rt.offsetMin = new Vector2(20f, rt.offsetMin.y); rt.offsetMax = new Vector2(-20f, rt.offsetMax.y);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size; t.fontStyle = FontStyles.Bold; t.color = col; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.BottomLeft;
            return t;
        }

        /// <summary>A white rounded rectangle, 9-sliced, made at run time (nothing to import or strip from the build).</summary>
        static Sprite RoundedSprite()
        {
            const int n = 64; float r = Radius;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(r - d + 0.5f)));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                 new Vector4(r + 2f, r + 2f, r + 2f, r + 2f));
        }

        static string Fmt(string s)
        {
            return (s ?? "").Replace("<sup>", "<voffset=0.42em><size=72%>").Replace("</sup>", "</size></voffset>")
                            .Replace("<sub>", "<voffset=-0.12em><size=72%>").Replace("</sub>", "</size></voffset>");
        }

        static string[] StripText(Panel p)
        {
            if (!string.IsNullOrEmpty(p.title)) return new[] { p.kicker ?? "", p.title };
            string key = p.image != null ? p.image.name : p.label;
            if (key != null && key.EndsWith("_v2")) key = key.Substring(0, key.Length - 3);
            string[] kt;
            return key != null && Titles.TryGetValue(key, out kt) ? kt : null;
        }

        /// <summary>Show the panel active at sequence time tm (negative = hide everything).</summary>
        public void UpdateAt(float tm)
        {
            if (_canvas == null) { _shift = 0f; return; }
            Panel p = null;
            if (show && tm >= 0f)
                foreach (var q in panels)
                    if (q.image != null && tm >= q.start && tm <= q.end) { p = q; break; }
            if (p == null) { _canvas.enabled = false; _shift = 0f; return; }
            _canvas.enabled = true;

            float a = Mathf.Clamp01(Mathf.Min((tm - p.start) / fade, (p.end - tm) / fade));
            float s = a * a * (3f - 2f * a);
            _group.alpha = s;
            _shift = videoLayout ? s * (p.left ? -1f : 1f) : 0f;

            var kt = StripText(p);
            float top = kt != null ? Strip + Pad * 0.8f : Pad;          // room above the drawing
            if (_shown != p)
            {
                _shown = p;
                _img.texture = p.image;
                _stripRoot.SetActive(kt != null);
                if (kt != null) { _kicker.text = kt[0].ToUpperInvariant(); _title.text = Fmt(kt[1]); }
                _imgRT.offsetMax = new Vector2(-Pad, -top);
            }

            // size: requested width, aspect from the image, capped by the height limit
            float w = (videoLayout ? p.width : twinWidth) * RefW, aspect = (float)p.image.width / p.image.height;
            float h = (w - 2 * Pad) / aspect + top + Pad;
            float hMax = (videoLayout ? 0.8f : twinMaxHeight) * RefH;
            // the canvas scales by height; convert widths to reference units at the current aspect
            float refWide = RefH * (cam != null ? cam.aspect : 16f / 9f);
            if (!videoLayout && refWide < RefH) hMax = Mathf.Min(hMax, 0.40f * RefH);      // portrait: leave the scene the lower part
            if (h > hMax) { h = hMax; w = (h - top - Pad) * aspect + 2 * Pad; }
            float wMax = refWide * (1f - 2f * margin);                                      // never wider than the screen
            if (w > wMax) { w = wMax; h = (w - 2 * Pad) / aspect + top + Pad; }
            _card.sizeDelta = new Vector2(w, h);
            float m = margin * refWide, x = m + (1f - s) * slide;
            float tabs = !videoLayout && CompactLayout.Compact && Screen.height > 0 ? CompactLayout.TabsBottomPx * RefH / Screen.height : 0f;
            if (videoLayout)
            {
                float side = p.left ? 0f : 1f;
                _card.anchorMin = _card.anchorMax = new Vector2(side, 0.5f);
                _card.pivot = new Vector2(side, 0.5f);
                _card.anchoredPosition = new Vector2(p.left ? x : -x, 0f);
            }
            else
            {
                _card.anchorMin = _card.anchorMax = new Vector2(0f, 1f);
                _card.pivot = new Vector2(0f, 1f);
                _card.anchoredPosition = new Vector2(x - slide * (1f - s) * 2f, -m - tabs);   // slides in from the left; below the compact tabs
            }
        }
    }
}
