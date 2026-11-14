// HbTutorialController.cs — the interactive twin: checkpoints (Track A, BCH 3023) that pause the
// Timeline, a T<->R slider, visibility toggles and replay buttons. All UI is built at runtime
// with uGUI, so the scene needs no prefab wiring beyond assigning the references below.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HbTutorial
{
    public class HbTutorialController : MonoBehaviour
    {
        public enum Track { A_BCH3023, B_CHEM301 }

        [System.Serializable]
        public class Checkpoint
        {
            public Track track;
            public string afterShot;          // label only
            public float time;                // seconds on the timeline; 0 = fire when Trigger(name) is called
            public string question;
            public string[] options;          // empty => click a residue instead
            public int correct;               // index into options, or -1 for click questions
            public string clickKey;           // residue key for click questions, e.g. "beta1:92"
            public string wrongFeedback;
            [System.NonSerialized] public bool done;
            [System.NonSerialized] public int wrongCount;
        }

        [System.Serializable]
        public class Shot { public string label; public float time; }

        [Header("References")]
        public HbShotSequence sequence;
        public HbMorphController morph;
        public OrbitCamera orbitCamera;
        public Camera cam;

        [Header("Content")]
        public Track track = Track.A_BCH3023;
        public List<Checkpoint> checkpoints = new List<Checkpoint>();
        public List<Shot> shotsA = new List<Shot>();
        public List<Shot> shotsB = new List<Shot>();

        [Header("UI")]
        public bool buildUI = true;
        public int fontSize = 18;

        // runtime UI
        Canvas _canvas; GameObject _panel; TextMeshProUGUI _question, _feedback; Button[] _optionButtons; Button _continue;
        Slider _tSlider; TextMeshProUGUI _tLabel; Transform _shotBar; TextMeshProUGUI _trackLabel;
        [System.NonSerialized] Checkpoint _active; bool _awaitingClick;   // NonSerialized: a domain reload would restore it as an empty checkpoint
        TMP_FontAsset _font;

        void Reset() { PopulateDefaults(); }

        [ContextMenu("Populate default checkpoints and shots")]
        public void PopulateDefaults()
        {
            checkpoints = new List<Checkpoint>
            {
                new Checkpoint { track = Track.A_BCH3023, afterShot = "2", time = 45f,
                    question = "Click the residue bonded directly to the iron.", options = new string[0], correct = -1,
                    clickKey = "beta1:92", wrongFeedback = "That one is on the oxygen side. The iron's protein anchor is below the heme." },
                new Checkpoint { track = Track.A_BCH3023, afterShot = "4", time = 110f,
                    question = "After one subunit binds O<sub>2</sub>, the empty hemes on the other subunits have:",
                    options = new[] { "Higher affinity", "Lower affinity", "Unchanged affinity" }, correct = 0,
                    wrongFeedback = "The ion pairs broke and the tetramer is in R. R is the high-affinity state." },
                new Checkpoint { track = Track.A_BCH3023, afterShot = "6", time = 160f,
                    question = "In working muscle, extra protons will:",
                    options = new[] { "Favor T and release O<sub>2</sub>", "Favor R and bind O<sub>2</sub>" }, correct = 0,
                    wrongFeedback = "Protonated His<sup>146</sup> re-forms its ion pair with Asp<sup>94</sup>, which stabilizes T." },
                new Checkpoint { track = Track.B_CHEM301, afterShot = "1", time = 20f,
                    question = "How many oxygen molecules can one hemoglobin carry at once?",
                    options = new[] { "1", "2", "4", "8" }, correct = 2,
                    wrongFeedback = "One per heme, and there are four hemes." },
                new Checkpoint { track = Track.B_CHEM301, afterShot = "8", time = 95f,
                    question = "The sickle mutation swaps glutamate for valine. Valine is:",
                    options = new[] { "Charged", "Greasy (nonpolar)" }, correct = 1,
                    wrongFeedback = "Glutamate is the charged one. Valine avoids water, which is the whole problem." },
                new Checkpoint { track = Track.B_CHEM301, afterShot = "9", time = 140f,
                    question = "Where are red cells most likely to sickle?",
                    options = new[] { "In the lungs", "In the tissues" }, correct = 1,
                    wrongFeedback = "Fibers form in the low-oxygen shape, and oxygen is lowest in the tissues." },
            };
            shotsA = new List<Shot>
            {
                new Shot { label = "1 Establishing", time = 0f }, new Shot { label = "2 Into one subunit", time = 20f },
                new Shot { label = "3 Oxygen binds", time = 45f }, new Shot { label = "4 The interface", time = 70f },
                new Shot { label = "5 Central cavity", time = 110f }, new Shot { label = "6 Bohr effect", time = 130f },
                new Shot { label = "7 Back to the curve", time = 160f },
            };
            shotsB = new List<Shot>
            {
                new Shot { label = "1 Establishing", time = 0f }, new Shot { label = "2 The heme", time = 20f },
                new Shot { label = "3 Oxygen binds", time = 40f }, new Shot { label = "8 The mutation", time = 65f },
                new Shot { label = "9 The fiber", time = 95f },
            };
        }

        void Start()
        {
            if (checkpoints.Count == 0) PopulateDefaults();
            track = Track.A_BCH3023;                 // the only track now
            if (!cam) cam = Camera.main;
            if (buildUI) BuildUI();
            SetTrack((int)track);
            // start in playback if the sequence plays on start, else in free look
            bool playing = sequence != null && sequence.playOnStart;
            if (sequence) sequence.active = playing;
            if (orbitCamera) orbitCamera.enabled = !playing;
        }

        void Update()
        {
            if (sequence != null && _active == null && sequence.playing)
            {
                foreach (var c in checkpoints)
                    if (!c.done && c.track == track && c.time > 0f && sequence.time >= c.time) { Show(c); break; }
            }
            if (_awaitingClick) PollClick();
            if (_tSlider != null && morph != null)
            {
                if (Mathf.Abs(_tSlider.value - morph.t) > 0.001f && !_sliderHeld) _tSlider.SetValueWithoutNotify(morph.t);
                if (_tLabel) _tLabel.text = morph.t < 0.02f ? "T state" : morph.t > 0.98f ? "R state" : $"T→R  {morph.t:0.00}";
            }
        }
        bool _sliderHeld;

        // ---------------------------------------------------------------- flow
        public void SetTrack(int i)
        {
            track = (Track)i;
            foreach (var c in checkpoints) { c.done = false; c.wrongCount = 0; }
            if (morph) morph.showSickle = track == Track.B_CHEM301 && morph.showSickle;
            RebuildShotBar();
            if (_trackLabel) _trackLabel.text = track == Track.A_BCH3023 ? "Track A: BCH 3023" : "Track B: CHEM301";
        }

        /// <summary>Fire a checkpoint by shot label from a Timeline Signal, for checkpoints with time = 0.</summary>
        public void Trigger(string afterShot)
        {
            foreach (var c in checkpoints) if (!c.done && c.track == track && c.afterShot == afterShot) { Show(c); return; }
        }

        public void ReplayShot(float time)
        {
            if (sequence == null) return;
            _active = null; HidePanel();
            // re-arm checkpoints from here on; skip earlier ones (the student jumped past them)
            foreach (var c in checkpoints) if (c.track == track) { c.done = c.time < time; c.wrongCount = 0; }
            sequence.Seek(time); sequence.Play();
            if (orbitCamera) orbitCamera.enabled = false;
        }

        public void FreeLook()
        {
            if (sequence != null) { sequence.Pause(); sequence.active = false; }
            if (orbitCamera) { orbitCamera.enabled = true; orbitCamera.SyncFromCamera(); }
        }

        void Show(Checkpoint c)
        {
            _active = c;
            if (sequence != null) sequence.Pause();
            if (_panel == null) return;
            _panel.SetActive(true);
            _question.text = Fmt(CompactLayout.Words(c.question));
            _feedback.text = "";
            _continue.gameObject.SetActive(false);
            bool click = c.options == null || c.options.Length == 0;
            _awaitingClick = click;
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                bool on = !click && i < c.options.Length;
                _optionButtons[i].gameObject.SetActive(on);
                if (on) _optionButtons[i].GetComponentInChildren<TextMeshProUGUI>().text = Fmt(c.options[i]);
            }
            if (click) { if (sequence) sequence.active = false; if (orbitCamera) { orbitCamera.enabled = true; orbitCamera.SyncFromCamera(); } }
        }

        void Answer(int i)
        {
            if (_active == null) return;
            if (i == _active.correct) Correct();
            else Wrong();
        }

        void Correct()
        {
            _feedback.text = "Correct.";
            _active.done = true; _awaitingClick = false;
            foreach (var b in _optionButtons) b.gameObject.SetActive(false);
            _continue.gameObject.SetActive(true);
        }

        void Wrong()
        {
            _active.wrongCount++;
            if (_active.wrongCount >= 2)
            {
                _feedback.text = Fmt(CompactLayout.Words(_active.wrongFeedback));
                _active.done = true; _awaitingClick = false;
                foreach (var b in _optionButtons) b.gameObject.SetActive(false);
                _continue.gameObject.SetActive(true);
            }
            else _feedback.text = "Not quite. Try once more.";
        }

        void Continue()
        {
            HidePanel();
            _active = null;
            if (sequence != null) { sequence.Play(); if (orbitCamera) orbitCamera.enabled = false; }
        }

        void HidePanel() { if (_panel) _panel.SetActive(false); _awaitingClick = false; }

        void PollClick()
        {
            bool down = false; Vector2 pos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) { down = true; pos = Mouse.current.position.ReadValue(); }
            else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) { down = true; pos = Touchscreen.current.primaryTouch.position.ReadValue(); }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) { down = true; pos = Input.mousePosition; }
#endif
            if (!down || cam == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            var ray = cam.ScreenPointToRay(pos);
            if (Physics.Raycast(ray, out var hit, 500f))
            {
                var sg = hit.collider.GetComponentInParent<StickGroup>();
                if (sg != null)
                {
                    if (sg.key == _active.clickKey) Correct();
                    else { var nm = sg.name; int cut = nm.IndexOf(" ["); if (cut > 0) nm = nm.Substring(0, cut); _feedback.text = CompactLayout.Words("You clicked ") + nm + "."; Wrong(); }
                }
            }
        }

        // ---------------------------------------------------------------- UI
        void BuildUI()
        {
            _font = TMP_Settings.defaultFontAsset;   // TextMeshPro: superscript residue numbers render in WebGL (the legacy font lacks most superscript digits)
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }
            var cgo = new GameObject("TutorialCanvas");
            _canvas = cgo.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900);
            cgo.AddComponent<GraphicRaycaster>();

            // --- top-right control strip
            var strip = Panel(cgo.transform, "Controls", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(300, 330), new Color(0, 0, 0, 0.55f));
            var vl = strip.AddComponent<VerticalLayoutGroup>(); vl.padding = new RectOffset(10, 10, 10, 10); vl.spacing = 6; vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true;
            _tLabel = Label(strip.transform, "T state", 16);
            _tSlider = SliderRow(strip.transform, v => { if (morph) morph.t = v; });
            var sliderEvents = _tSlider.gameObject.AddComponent<SliderHold>(); sliderEvents.onHeld = h => _sliderHeld = h;
            ToggleRow(strip.transform, "Chains", true, v => { if (morph) morph.showChains = v; });
            ToggleRow(strip.transform, "T-state ghost", false, v => { if (morph) morph.showGhost = v; });
            ToggleRow(strip.transform, "Hemes", true, v => { if (morph) morph.showHemes = v; });
            ToggleRow(strip.transform, "Residues", true, v => { if (morph) morph.showResidues = v; });
            ToggleRow(strip.transform, "Ion pairs", true, v => { if (morph) morph.showIonPairs = v; });
            ToggleRow(strip.transform, "O<sub>2</sub>", true, v => { if (morph) morph.showO2 = v; });
            ToggleRow(strip.transform, "Sickle fiber", false, v => { if (morph) morph.showSickle = v; });
            // one track only (BCH 3023): the CHEM301 track was dropped Nov 6, so no track buttons
            ButtonRow(strip.transform, "Free look (pause)", FreeLook);

            // --- bottom-left shot bar
            var bar = Panel(cgo.transform, "Shots", new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 10), new Vector2(260, 300), new Color(0, 0, 0, 0.55f));
            var bl = bar.AddComponent<VerticalLayoutGroup>(); bl.padding = new RectOffset(10, 10, 10, 10); bl.spacing = 4; bl.childForceExpandHeight = false; bl.childControlHeight = true; bl.childControlWidth = true;
            Label(bar.transform, "Replay a shot", 16);
            _shotBar = bar.transform;
            RebuildShotBar();

            // --- checkpoint panel (bottom centre)
            _panel = Panel(cgo.transform, "Checkpoint", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(760, 230), new Color(0.08f, 0.08f, 0.1f, 0.92f));
            var pl = _panel.AddComponent<VerticalLayoutGroup>(); pl.padding = new RectOffset(16, 16, 12, 12); pl.spacing = 8; pl.childForceExpandHeight = false; pl.childControlHeight = true; pl.childControlWidth = true;
            _question = Label(_panel.transform, "", fontSize + 2);
            _optionButtons = new Button[4];
            for (int i = 0; i < 4; i++) { int idx = i; _optionButtons[i] = ButtonRow(_panel.transform, "", () => Answer(idx)); }
            _feedback = Label(_panel.transform, "", fontSize); _feedback.color = new Color(1f, 0.85f, 0.4f);
            _continue = ButtonRow(_panel.transform, "Continue", Continue);
            _panel.SetActive(false);

            // phones and small windows: CSS-pixel sizing, folded controls, full-width checkpoint box (CompactLayout.cs)
            cgo.AddComponent<CompactLayout>().Init(scaler, strip.GetComponent<RectTransform>(), bar.GetComponent<RectTransform>(), _panel.GetComponent<RectTransform>(), cam);
        }

        /// <summary>From the page (index.html): device pixel ratio and whether the main pointer is a finger.</summary>
        public void SetDisplay(string s) { CompactLayout.SetDisplay(s); }

        void RebuildShotBar()
        {
            if (_shotBar == null) return;
            for (int i = _shotBar.childCount - 1; i >= 1; i--) Destroy(_shotBar.GetChild(i).gameObject);
            var shots = track == Track.A_BCH3023 ? shotsA : shotsB;
            foreach (var s in shots) { float t = s.time; ButtonRow(_shotBar, s.label, () => ReplayShot(t)); }
        }

        GameObject Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color col)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.AddComponent<Image>(); img.color = col;
            var fit = go.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }
        /// <summary>TMP's own &lt;sup&gt; follows the font's superscript size, which is too small at UI sizes; use a larger raised size.</summary>
        static string Fmt(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("<sup>", "<voffset=0.42em><size=72%>").Replace("</sup>", "</size></voffset>")
                    .Replace("<sub>", "<voffset=-0.12em><size=72%>").Replace("</sub>", "</size></voffset>");
        }

        TextMeshProUGUI Label(Transform parent, string text, int size)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>(); if (_font) t.font = _font; t.fontSize = size; t.text = Fmt(text); t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal; t.richText = true;
            var le = go.AddComponent<LayoutElement>(); le.minHeight = size + 8;
            return t;
        }
        Button ButtonRow(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button"); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0.25f, 0.3f, 0.4f, 0.95f);
            var b = go.AddComponent<Button>(); b.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>(); le.minHeight = fontSize + 14;
            var t = Label(go.transform, text, fontSize); t.alignment = TextAlignmentOptions.Center;
            var rt = t.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(8, 0); rt.offsetMax = new Vector2(-8, 0);
            return b;
        }
        Toggle ToggleRow(Transform parent, string text, bool value, UnityEngine.Events.UnityAction<bool> onChange)
        {
            var go = new GameObject("Toggle " + text); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.minHeight = fontSize + 8;
            var tg = go.AddComponent<Toggle>();
            var bg = new GameObject("Background"); bg.transform.SetParent(go.transform, false);
            var bgi = bg.AddComponent<Image>(); bgi.color = new Color(0.9f, 0.9f, 0.9f);
            var brt = bg.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(18, 18); brt.anchoredPosition = new Vector2(12, 0);
            var ck = new GameObject("Checkmark"); ck.transform.SetParent(bg.transform, false);
            var cki = ck.AddComponent<Image>(); cki.color = new Color(0.2f, 0.5f, 0.9f);
            var crt = ck.GetComponent<RectTransform>(); crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = new Vector2(3, 3); crt.offsetMax = new Vector2(-3, -3);
            tg.targetGraphic = bgi; tg.graphic = cki; tg.isOn = value; tg.onValueChanged.AddListener(onChange);
            var t = Label(go.transform, text, fontSize - 2); var trt = t.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(30, 0); trt.offsetMax = Vector2.zero; t.alignment = TextAlignmentOptions.MidlineLeft;
            return tg;
        }
        Slider SliderRow(Transform parent, UnityEngine.Events.UnityAction<float> onChange)
        {
            var go = new GameObject("Slider"); go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>(); le.minHeight = 22;
            var s = go.AddComponent<Slider>(); s.minValue = 0; s.maxValue = 1;
            var bg = new GameObject("Background"); bg.transform.SetParent(go.transform, false);
            var bgi = bg.AddComponent<Image>(); bgi.color = new Color(0.3f, 0.3f, 0.35f);
            var brt = bg.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(0, 0.35f); brt.anchorMax = new Vector2(1, 0.65f); brt.offsetMin = brt.offsetMax = Vector2.zero;
            var fa = new GameObject("Fill Area"); fa.transform.SetParent(go.transform, false);
            var fart = fa.AddComponent<RectTransform>(); fart.anchorMin = new Vector2(0, 0.35f); fart.anchorMax = new Vector2(1, 0.65f); fart.offsetMin = new Vector2(8, 0); fart.offsetMax = new Vector2(-8, 0);
            var fill = new GameObject("Fill"); fill.transform.SetParent(fa.transform, false);
            var fi = fill.AddComponent<Image>(); fi.color = new Color(0.95f, 0.6f, 0.2f);
            var frt = fill.GetComponent<RectTransform>(); frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            var ha = new GameObject("Handle Slide Area"); ha.transform.SetParent(go.transform, false);
            var hart = ha.AddComponent<RectTransform>(); hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one; hart.offsetMin = new Vector2(8, 0); hart.offsetMax = new Vector2(-8, 0);
            var h = new GameObject("Handle"); h.transform.SetParent(ha.transform, false);
            var hi = h.AddComponent<Image>(); hi.color = Color.white;
            var hrt = h.GetComponent<RectTransform>(); hrt.sizeDelta = new Vector2(16, 0); hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(0, 1);
            s.fillRect = frt; s.handleRect = hrt; s.targetGraphic = hi; s.direction = Slider.Direction.LeftToRight;
            s.onValueChanged.AddListener(onChange);
            return s;
        }
    }

    /// <summary>Tells the controller when the user is holding the slider, so Update does not fight the drag.</summary>
    public class SliderHold : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        public System.Action<bool> onHeld;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { onHeld?.Invoke(true); }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { onHeld?.Invoke(false); }
    }
}
