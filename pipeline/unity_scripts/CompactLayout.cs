// CompactLayout.cs — the tutorial interface on phones and small windows.
// Desktop (mouse, window at least 900 × 500 CSS px): the original layout, untouched.
// Compact (touch screen, or a small window): the interface is sized in CSS pixels, so text reads at phone size; the
// control strip and the shot list fold behind two buttons at the top left (one open at a time); the checkpoint box
// spans the bottom of the screen; buttons and toggles are at least 40 px tall; notes (a residue's role) show at the top.
// In any window narrower than 16:9 the camera keeps the shot's horizontal field of view, so a portrait phone still
// sees the whole subject. The page tells Unity the device pixel ratio and whether the main pointer is a finger
// (index.html → SetDisplay on the Tutorial object).
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HbTutorial
{
    [DefaultExecutionOrder(1000)]                       // after the shot sequences, which set the camera's field of view
    public class CompactLayout : MonoBehaviour
    {
        public static float DevicePixelRatio = 1f;
        public static bool Touch;
        public static bool Compact { get; private set; }
        public static float TabsBottomPx { get; private set; }      // device pixels from the top; 0 on the desktop layout
        static CompactLayout _instance;

        public float rowHeight = 40f;                    // compact minimum height of buttons and toggles (CSS px)
        public float designAspect = 16f / 9f;            // the aspect the shots were framed for
        public float maxFov = 100f;

        CanvasScaler _scaler; Camera _cam;
        RectTransform _strip, _bar, _panel;
        CanvasScaler.ScaleMode _mode0; Vector2 _ref0; float _match0, _factor0;
        struct RectState { public Vector2 aMin, aMax, pivot, pos, size; }
        readonly Dictionary<RectTransform, RectState> _rects = new Dictionary<RectTransform, RectState>();
        readonly Dictionary<LayoutElement, float> _minH = new Dictionary<LayoutElement, float>();
        readonly Dictionary<GridLayoutGroup, Vector2> _cells = new Dictionary<GridLayoutGroup, Vector2>();
        GameObject _tabs, _noteBox; TextMeshProUGUI _controlsLabel, _shotsLabel, _note; float _noteUntil;
        int _open;                                       // compact only: 0 nothing, 1 controls, 2 shots
        bool _dirty = true; Vector2Int _screen; float _dpr = -1f; bool _touch;
        float _fovBase = -1f, _fovOut = -1f;
        readonly Dictionary<Image, Color> _bg0 = new Dictionary<Image, Color>();   // desktop backgrounds of the strip and the shot list

        // ------------------------------------------------------------------ set-up and messages
        public void Init(CanvasScaler scaler, RectTransform strip, RectTransform bar, RectTransform panel, Camera cam)
        {
            _instance = this;
            _scaler = scaler; _strip = strip; _bar = bar; _panel = panel; _cam = cam;
            _mode0 = scaler.uiScaleMode; _ref0 = scaler.referenceResolution; _match0 = scaler.matchWidthOrHeight; _factor0 = scaler.scaleFactor;
            foreach (var rt in new[] { strip, bar, panel }) if (rt != null) _rects[rt] = Save(rt);
            foreach (var rt in new[] { strip, bar }) { var img = rt != null ? rt.GetComponent<Image>() : null; if (img != null) _bg0[img] = img.color; }
            BuildTabs();
            _dirty = true;
        }

        /// <summary>From the page: "devicePixelRatio;1 if the main pointer is coarse (a finger), else 0".</summary>
        public static void SetDisplay(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var parts = s.Split(';');
            if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dpr) && dpr > 0.5f && dpr < 8f)
                DevicePixelRatio = dpr;
            if (parts.Length > 1) Touch = parts[1].Trim() == "1";
            if (_instance != null) _instance._dirty = true;
        }

        /// <summary>On a touch screen, "click" becomes "tap" (Click → Tap, clicked → tapped, clicking → tapping).</summary>
        public static string Words(string s)
        {
            if (!Touch || string.IsNullOrEmpty(s)) return s;
            return Regex.Replace(s, @"\b([Cc])lick(ed|ing|s)?\b", m =>
            {
                string t = m.Groups[1].Value == "C" ? "Tap" : "tap", end = m.Groups[2].Value;
                return t + (end == "ed" ? "ped" : end == "ing" ? "ping" : end);
            });
        }

        /// <summary>A short note at the top of the screen in the compact layout (the desktop shows it in the control strip).</summary>
        public static void Note(string rich)
        {
            if (_instance == null || !Compact || _instance._note == null) return;
            _instance._note.text = rich; _instance._noteBox.SetActive(true); _instance._noteUntil = Time.unscaledTime + 7f;
        }

        /// <summary>Opens the control strip in the compact layout (for example, when a checkpoint opens the side-chain picker).</summary>
        public static void OpenControls()
        {
            if (_instance == null || !Compact) return;
            _instance._open = 1; _instance._dirty = true;
        }

        // ------------------------------------------------------------------ every frame
        void LateUpdate()
        {
            var scr = new Vector2Int(Screen.width, Screen.height);
            if (scr != _screen || !Mathf.Approximately(_dpr, DevicePixelRatio) || _touch != Touch) { _screen = scr; _dpr = DevicePixelRatio; _touch = Touch; _dirty = true; }
            if (_dirty && _scaler != null) { _dirty = false; Apply(); }
            if (_noteBox != null && _noteBox.activeSelf && Time.unscaledTime > _noteUntil) _noteBox.SetActive(false);
            KeepHorizontalFov();
        }

        void KeepHorizontalFov()
        {
            if (_cam == null) return;
            float f = _cam.fieldOfView;
            if (_fovOut < 0f || Mathf.Abs(f - _fovOut) > 0.001f) _fovBase = f;     // the sequence (or anything else) set it this frame
            float a = _cam.aspect, o = _fovBase;
            if (a > 0.05f && a < designAspect)
                o = Mathf.Min(2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(_fovBase * 0.5f * Mathf.Deg2Rad) * designAspect / a), maxFov);
            _cam.fieldOfView = o; _fovOut = o;
        }

        // ------------------------------------------------------------------ layouts
        void Apply()
        {
            float dpr = Mathf.Max(0.5f, DevicePixelRatio);
            float cssW = Screen.width / dpr, cssH = Screen.height / dpr;
            Compact = Touch || cssW < 900f || cssH < 500f;
            ScanNew();
            if (!Compact) { Restore(); return; }

            bool portrait = cssH > cssW;
            float needW = portrait ? 360f : 640f, needH = portrait ? 600f : 400f;
            float zoom = Mathf.Clamp(Mathf.Min(cssW / needW, cssH / needH), 0.6f, 1.25f);
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _scaler.scaleFactor = dpr * zoom;
            float W = Screen.width / _scaler.scaleFactor, H = Screen.height / _scaler.scaleFactor;

            SetRows(rowHeight);

            // the two tabs at the top left
            _tabs.SetActive(true);
            _controlsLabel.text = _open == 1 ? "Close" : "Controls";
            _shotsLabel.text = _open == 2 ? "Close" : "Shots";
            float top = 8f + rowHeight + 8f;
            TabsBottomPx = top * _scaler.scaleFactor;
            Place(_strip, _open == 1, top, W);
            Place(_bar, _open == 2, top, W);
            var side = _open == 1 ? _strip : _open == 2 ? _bar : null;
            FitRows(side, H - top - 8f);                         // a short landscape screen: shorter rows, so the list fits

            // checkpoint box: full width along the bottom (at most 740 wide)
            if (_panel != null)
            {
                float w = Mathf.Min(W - 16f, 740f), x = 0f;
                if (!portrait && side != null)                   // landscape with the controls open: the box goes beside them
                {
                    float sw = side.sizeDelta.x;
                    w = Mathf.Min(W - sw - 24f, 740f); x = (sw + 8f) * 0.5f;
                }
                _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0f); _panel.pivot = new Vector2(0.5f, 0f);
                _panel.anchoredPosition = new Vector2(x, 8f); _panel.sizeDelta = new Vector2(w, _panel.sizeDelta.y);
            }
            if (_noteBox != null)
            {
                var nrt = (RectTransform)_noteBox.transform;
                nrt.sizeDelta = new Vector2(Mathf.Min(W - 16f, 560f), nrt.sizeDelta.y);
                nrt.anchoredPosition = new Vector2(0f, -top);
            }
        }

        /// <summary>Minimum height of every button, toggle and grid row (grids keep their number of rows).</summary>
        void SetRows(float r)
        {
            foreach (var kv in _minH) if (kv.Key != null) kv.Key.minHeight = Mathf.Max(kv.Value, r);
            foreach (var kv in _cells)
            {
                var g = kv.Key; if (g == null) continue;
                g.cellSize = new Vector2(kv.Value.x, r);
                var le = g.GetComponent<LayoutElement>();
                if (le != null && _minH.TryGetValue(le, out var m0))
                {
                    float sp = g.spacing.y; int rows = Mathf.Max(1, Mathf.RoundToInt((m0 + sp) / (kv.Value.y + sp)));
                    le.minHeight = rows * r + (rows - 1) * sp;
                }
            }
        }

        /// <summary>Shrinks the rows (to 28 at the least) until the open list fits the height left below the tabs.</summary>
        void FitRows(RectTransform open, float avail)
        {
            if (open == null) return;
            for (float r = rowHeight; r >= 28f; r -= 2f)
            {
                SetRows(r);
                LayoutRebuilder.ForceRebuildLayoutImmediate(open);
                if (LayoutUtility.GetPreferredHeight(open) <= avail) return;
            }
        }

        void Place(RectTransform rt, bool show, float top, float W)
        {
            if (rt == null) return;
            rt.gameObject.SetActive(show);
            if (!show) return;
            var s0 = _rects[rt];
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -top);
            rt.sizeDelta = new Vector2(Mathf.Min(Mathf.Max(s0.size.x, 300f), W - 16f), rt.sizeDelta.y);
            // nearly opaque, so the panel card underneath does not show through the controls
            var img = rt.GetComponent<Image>(); if (img != null && _bg0.TryGetValue(img, out var c0)) img.color = new Color(0.08f, 0.08f, 0.1f, Mathf.Max(c0.a, 0.94f));
        }

        void Restore()
        {
            TabsBottomPx = 0f;
            _scaler.uiScaleMode = _mode0; _scaler.referenceResolution = _ref0; _scaler.matchWidthOrHeight = _match0; _scaler.scaleFactor = _factor0;
            foreach (var kv in _rects) { if (kv.Key == null) continue; Load(kv.Key, kv.Value); kv.Key.gameObject.SetActive(kv.Key != _panel || kv.Key.gameObject.activeSelf); }
            foreach (var kv in _minH) if (kv.Key != null) kv.Key.minHeight = kv.Value;
            foreach (var kv in _cells) if (kv.Key != null) kv.Key.cellSize = kv.Value;
            foreach (var kv in _bg0) if (kv.Key != null) kv.Key.color = kv.Value;
            if (_tabs != null) _tabs.SetActive(false);
            if (_noteBox != null) _noteBox.SetActive(false);
            _open = 0;
        }

        /// <summary>Remember the desktop size of any button, toggle or grid created since the last look (shot lists are rebuilt).</summary>
        void ScanNew()
        {
            foreach (var le in _scaler.GetComponentsInChildren<LayoutElement>(true))
            {
                if (_minH.ContainsKey(le) || le.transform.IsChildOf(_tabs.transform)) continue;
                bool isRow = le.GetComponent<Button>() != null || le.GetComponent<Toggle>() != null || le.GetComponent<GridLayoutGroup>() != null;
                if (isRow) _minH[le] = le.minHeight;
            }
            foreach (var g in _scaler.GetComponentsInChildren<GridLayoutGroup>(true))
                if (!_cells.ContainsKey(g)) _cells[g] = g.cellSize;
            var dead = new List<LayoutElement>(); foreach (var k in _minH.Keys) if (k == null) dead.Add(k); foreach (var k in dead) _minH.Remove(k);
        }

        static RectState Save(RectTransform rt) => new RectState { aMin = rt.anchorMin, aMax = rt.anchorMax, pivot = rt.pivot, pos = rt.anchoredPosition, size = rt.sizeDelta };
        static void Load(RectTransform rt, RectState s) { rt.anchorMin = s.aMin; rt.anchorMax = s.aMax; rt.pivot = s.pivot; rt.anchoredPosition = s.pos; rt.sizeDelta = s.size; }

        // ------------------------------------------------------------------ the compact-only widgets
        void BuildTabs()
        {
            var font = TMP_Settings.defaultFontAsset;
            _tabs = new GameObject("Compact tabs", typeof(RectTransform));
            _tabs.transform.SetParent(_scaler.transform, false);
            var trt = (RectTransform)_tabs.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(8f, -8f); trt.sizeDelta = new Vector2(2 * 118f + 8f, rowHeight);
            var hl = _tabs.AddComponent<HorizontalLayoutGroup>(); hl.spacing = 8f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = true;
            _controlsLabel = Tab("Controls", () => { _open = _open == 1 ? 0 : 1; _dirty = true; }, font);
            _shotsLabel = Tab("Shots", () => { _open = _open == 2 ? 0 : 2; _dirty = true; }, font);
            _tabs.SetActive(false);

            _noteBox = new GameObject("Compact note", typeof(RectTransform), typeof(Image));
            _noteBox.transform.SetParent(_scaler.transform, false);
            var nrt = (RectTransform)_noteBox.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 1f); nrt.pivot = new Vector2(0.5f, 1f); nrt.sizeDelta = new Vector2(360f, 10f);
            _noteBox.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            var vl = _noteBox.AddComponent<VerticalLayoutGroup>(); vl.padding = new RectOffset(12, 12, 8, 8); vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandHeight = false;
            var fit = _noteBox.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var go = new GameObject("Text", typeof(RectTransform)); go.transform.SetParent(_noteBox.transform, false);
            _note = go.AddComponent<TextMeshProUGUI>(); if (font) _note.font = font; _note.fontSize = 16; _note.color = Color.white;
            _note.textWrappingMode = TextWrappingModes.Normal; _note.richText = true; _note.raycastTarget = false;
            _noteBox.GetComponent<Image>().raycastTarget = false;
            _noteBox.SetActive(false);
        }

        TextMeshProUGUI Tab(string text, UnityEngine.Events.UnityAction onClick, TMP_FontAsset font)
        {
            var go = new GameObject("Tab " + text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_tabs.transform, false);
            go.GetComponent<Image>().color = new Color(0.18f, 0.43f, 0.81f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var t = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(go.transform, false);
            var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            if (font) t.font = font; t.fontSize = 17; t.color = Color.white; t.alignment = TextAlignmentOptions.Center; t.text = text;
            return t;
        }
    }
}
