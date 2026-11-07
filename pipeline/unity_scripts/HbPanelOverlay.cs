// HbPanelOverlay.cs — 2D panels (ChemDraw figures, the binding curve) that slide in over the 3D scene
// during set stretches of the shot sequence. The canvas is drawn by the scene camera (Screen Space - Camera),
// so the flat video recorder captures it; the student UI stays on its own overlay canvas and is not recorded.
// The cards are drawn by a URP overlay camera stacked on the scene camera, with post-processing off, so the
// tone mapping and bloom that suit the molecule do not turn the white cards gray.
// Panels show only while the sequence is driving the camera (replay); in free look they are hidden.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

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
        }

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
        [Range(0.2f, 0.5f)] public float twinWidth = 0.30f;   // card width in the interactive layout
        [Range(0.3f, 0.8f)] public float twinMaxHeight = 0.5f; // keeps the card above the replay buttons

        const float RefW = 1920f, RefH = 1080f, Pad = 22f;
        Canvas _canvas; RectTransform _card; RawImage _img; CanvasGroup _group; Camera _panelCam;
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

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Shadow));
            card.transform.SetParent(go.transform, false);
            _card = card.GetComponent<RectTransform>();
            var bg = card.GetComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.97f); bg.raycastTarget = false;
            var sh = card.GetComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.35f); sh.effectDistance = new Vector2(4f, -4f);
            _group = card.GetComponent<CanvasGroup>(); _group.interactable = false; _group.blocksRaycasts = false;

            var im = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            im.transform.SetParent(card.transform, false);
            _img = im.GetComponent<RawImage>(); _img.raycastTarget = false;
            var irt = im.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(Pad, Pad); irt.offsetMax = new Vector2(-Pad, -Pad);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
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
            if (_img.texture != p.image) _img.texture = p.image;

            // size: requested width, aspect from the image, capped at 80% of screen height
            float w = (videoLayout ? p.width : twinWidth) * RefW, aspect = (float)p.image.width / p.image.height;
            float h = (w - 2 * Pad) / aspect + 2 * Pad;
            float hMax = (videoLayout ? 0.8f : twinMaxHeight) * RefH;
            if (h > hMax) { h = hMax; w = (h - 2 * Pad) * aspect + 2 * Pad; }
            _card.sizeDelta = new Vector2(w, h);
            // the canvas scales by height; convert the width margin to reference units at the current aspect
            float refWide = RefH * (cam != null ? cam.aspect : 16f / 9f);
            float m = margin * refWide, x = m + (1f - s) * slide;
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
                _card.anchoredPosition = new Vector2(x - slide * (1f - s) * 2f, -m);   // slides in from the left
            }
        }
    }
}
