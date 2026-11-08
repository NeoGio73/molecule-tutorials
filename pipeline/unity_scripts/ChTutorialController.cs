// ChTutorialController.cs — the interactive chymotrypsin twin (BCH 3023): the narrated sequence plays and pauses at
// the storyboard's checkpoints; a step slider walks the mechanism through Lehninger's eight states with the matching
// 2D panel; residues can be clicked for their role; the shot 3 checkpoint opens the side-chain picker; any shot can be
// replayed. All UI is built at runtime (uGUI + TextMeshPro, so superscript residue numbers render in WebGL).
// For recording the video, disable this component: the sequence then plays with the video layout.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using HbTutorial;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ChTutorial
{
    [DefaultExecutionOrder(100)]                     // after ChShotSequence, so the panel shown in explore mode sticks
    public class ChTutorialController : MonoBehaviour
    {
        [System.Serializable]
        public class Checkpoint
        {
            public string afterShot;
            public float time;
            [TextArea] public string question;
            public string[] options;
            public int correct;
            [TextArea] public string feedback;        // shown after a correct answer or the second miss
            [TextArea] public string tryIt;           // "In the scene" from the storyboard
            public bool picker;                       // opens the side-chain picker
            [System.NonSerialized] public bool done;
            [System.NonSerialized] public int wrongCount;
        }
        [System.Serializable] public class Shot { public string label; public float time; }

        [Header("References")]
        public ChShotSequence sequence;
        public ChSceneBuilder builder;
        public OrbitCamera orbitCamera;
        public Camera cam;
        public ChSideChainPicker picker;

        [Header("Content")]
        public List<Checkpoint> checkpoints = new List<Checkpoint>();
        public List<Shot> shots = new List<Shot>();
        public int fontSize = 18;

        static readonly string[] StateNames =
        {
            "Free enzyme", "Enzyme–substrate complex", "Short-lived intermediate (acylation)", "Acyl-enzyme intermediate",
            "Acyl-enzyme + water", "Short-lived intermediate (deacylation)", "Enzyme–product 2 complex", "Free enzyme",
        };
        // sequence times inside each panel's window, used to show a state's panel while exploring
        static readonly float[] StatePanelTime = { 40f, 100f, 140f, 170f, 188f, 203f, 213f, 40f };

        static readonly Dictionary<string, string> Roles = new Dictionary<string, string>
        {
            { "Ser195", "<b>Ser<sup>195</sup></b>: the nucleophile. Its O attacks the carbonyl carbon of the scissile bond, and the acyl group ends up as an ester on it." },
            { "His57", "<b>His<sup>57</sup></b>: the general base. It takes the proton from Ser<sup>195</sup> (and later from water), then gives it to the leaving group." },
            { "Asp102", "<b>Asp<sup>102</sup></b>: hydrogen-bonded to His<sup>57</sup>. When substrate binds, this becomes a low-barrier hydrogen bond, and the pK<sub>a</sub> of His<sup>57</sup> rises from about 7 to above 12." },
            { "Gly193", "<b>Gly<sup>193</sup></b>: its backbone N–H, with that of Ser<sup>195</sup>, forms the oxyanion hole." },
        };

        // runtime UI
        GameObject _panel, _pickerRow; TextMeshProUGUI _question, _feedback, _tryIt, _stateLabel, _info, _schematic;
        Button[] _optionButtons; Button _continue;
        Slider _stepSlider; bool _sliderHeld, _exploring;
        Toggle _arrowsToggle, _labelsToggle, _pocketToggle, _chainsToggle;
        [System.NonSerialized] Checkpoint _active;   // NonSerialized: a domain reload would otherwise restore it as an empty checkpoint
        TMP_FontAsset _font;

        void Reset() { PopulateDefaults(); }

        [ContextMenu("Populate default checkpoints and shots")]
        public void PopulateDefaults()
        {
            checkpoints = new List<Checkpoint>
            {
                new Checkpoint { afterShot = "2", time = 50f, question = "Which triad residue attacks the substrate?",
                    options = new[] { "Asp<sup>102</sup>", "His<sup>57</sup>", "Ser<sup>195</sup>" }, correct = 2,
                    feedback = "Ser<sup>195</sup>. His<sup>57</sup> is the general base, and Asp<sup>102</sup> is hydrogen-bonded to His<sup>57</sup>.",
                    tryIt = "Try it: click each triad residue to see its role." },
                new Checkpoint { afterShot = "3", time = 80f, question = "In Ala–Phe–Gly–Lys, which is the scissile bond for chymotrypsin?",
                    options = new[] { "Ala–Phe", "Phe–Gly", "Gly–Lys" }, correct = 1,
                    feedback = "Phe–Gly, on the carboxyl side of Phe.",
                    tryIt = "Try it: pick a side chain and watch whether it fits the hydrophobic pocket.", picker = true },
                new Checkpoint { afterShot = "4", time = 120f, question = "A serine OH is a poor acid. How can His<sup>57</sup> take its proton?",
                    options = new[] { "Substrate binding compresses the Asp<sup>102</sup>–His<sup>57</sup> hydrogen bond into a low-barrier hydrogen bond, raising the pK<sub>a</sub> of His<sup>57</sup>",
                                      "Water takes the proton from serine first", "Asp<sup>102</sup> takes the proton directly from serine" }, correct = 0,
                    feedback = "The low-barrier hydrogen bond raises the pK<sub>a</sub> of His<sup>57</sup> from about 7 to above 12.",
                    tryIt = "Try it: drag the step slider between the free enzyme and the ES complex and watch the Asp–His bond." },
                new Checkpoint { afterShot = "5", time = 140f, question = "What holds the negative charge of the tetrahedral intermediate?",
                    options = new[] { "Two backbone N–H groups (the oxyanion hole)", "The His<sup>57</sup> side chain", "Asp<sup>102</sup>" }, correct = 0,
                    feedback = "The backbone N–H groups of Gly<sup>193</sup> and Ser<sup>195</sup>, not a side chain.",
                    tryIt = "Try it: step to the intermediate and find the two dashed N–H bonds." },
                new Checkpoint { afterShot = "6", time = 170f, question = "What does His<sup>57</sup> do as the intermediate collapses?",
                    options = new[] { "Acts as a proton donor: protonates the amino group of the leaving group", "Acts as a nucleophile", "Takes a proton from the leaving group" }, correct = 0,
                    feedback = "His<sup>57</sup>, now N–H<sup>+</sup>, gives its proton to the leaving nitrogen.",
                    tryIt = "Try it: replay the collapse and follow the magenta proton." },
                new Checkpoint { afterShot = "7", time = 210f, question = "Which bond does water break in deacylation?",
                    options = new[] { "The ester bond between the acyl group and Ser<sup>195</sup>", "Another peptide bond in the substrate", "The Asp<sup>102</sup>–His<sup>57</sup> hydrogen bond" }, correct = 0,
                    feedback = "The ester bond between the acyl group and Ser<sup>195</sup> O<sub>γ</sub>.",
                    tryIt = "Try it: step between the acyl-enzyme and product 2." },
                new Checkpoint { afterShot = "8", time = 226f, question = "Why does product appear in a burst, then slowly?",
                    options = new[] { "Acylation is fast, and the slower deacylation sets the steady-state rate", "The enzyme stops working after one turnover", "The substrate runs out" }, correct = 0,
                    feedback = "With this ester substrate, acylation is fast and deacylation is slower, so deacylation sets the steady-state rate (for peptide substrates acylation is usually the slower step).",
                    tryIt = "Look at the burst panel: the burst height matches the amount of enzyme." },
            };
            shots = new List<Shot>
            {
                new Shot { label = "1 Establishing", time = 0f }, new Shot { label = "2 The triad", time = 32f },
                new Shot { label = "3 Substrate binds", time = 54f }, new Shot { label = "4 Acylation", time = 84f },
                new Shot { label = "5 Oxyanion hole", time = 124f }, new Shot { label = "6 Collapse", time = 144f },
                new Shot { label = "7 Deacylation", time = 172f }, new Shot { label = "8 Burst kinetics", time = 210.5f },
                new Shot { label = "9 Wide view", time = 226f }, new Shot { label = "10 Activation", time = 242f },
            };
        }

        void Start()
        {
            if (checkpoints.Count == 0) PopulateDefaults();
            if (!cam) cam = Camera.main;
            if (sequence != null && sequence.panels != null) sequence.panels.videoLayout = false;   // the twin's layout
            BuildUI();
            bool playing = sequence != null && sequence.playOnStart;
            if (sequence) sequence.active = playing;
            if (orbitCamera) orbitCamera.enabled = !playing;
        }

        void Update()
        {
            if (sequence != null && _active == null && sequence.playing && sequence.active)
            {
                foreach (var c in checkpoints)
                    if (!c.done && c.time > 0f && sequence.time >= c.time) { Show(c); break; }
            }
            PollClick();
            var m = builder != null ? builder.mechanism : null;
            if (m != null && _stepSlider != null)
            {
                if (!_sliderHeld && Mathf.Abs(_stepSlider.value - m.step) > 0.001f) _stepSlider.SetValueWithoutNotify(m.step);
                int si = Mathf.Clamp(Mathf.RoundToInt(m.step), 0, StateNames.Length - 1);
                if (_stateLabel) _stateLabel.text = StateNames[si];
            }
        }

        void LateUpdate()
        {
            // a checkpoint is waiting: keep the panel of that moment up (the sequence hides panels once free look starts)
            if (!_exploring && _active != null && sequence != null && sequence.panels != null && !sequence.active)
                sequence.panels.UpdateAt(_active.time);
            // exploring: keep the state's panel up (the sequence hides panels while it is inactive)
            if (_exploring && sequence != null && sequence.panels != null && builder != null && builder.mechanism != null)
            {
                int si = Mathf.Clamp(Mathf.RoundToInt(builder.mechanism.step), 0, StatePanelTime.Length - 1);
                bool picking = picker != null && !string.IsNullOrEmpty(picker.current);
                sequence.panels.UpdateAt(picking ? 68f : StatePanelTime[si]);           // the picker shows the pocket panel (ch_p02)
            }
        }

        // ---------------------------------------------------------------- flow
        public void ReplayShot(float time)
        {
            if (sequence == null) return;
            _active = null; HidePanel(); StopExploring();
            // checkpoints from here on are asked again; earlier ones are skipped (the student jumped past them)
            foreach (var c in checkpoints) { c.done = c.time < time; c.wrongCount = 0; }
            sequence.Seek(time); sequence.Play();
            if (orbitCamera) orbitCamera.enabled = false;
        }

        public void FreeLook()
        {
            if (sequence != null) { sequence.Pause(); sequence.active = false; }
            if (orbitCamera)
            {
                if (builder != null && builder.Target("site") != null && orbitCamera.target == null) orbitCamera.target = builder.Target("site");
                orbitCamera.enabled = true; orbitCamera.SyncFromCamera();
            }
        }

        void StartExploring()
        {
            if (_exploring) return;
            _exploring = true;
            FreeLook();
            var m = builder.mechanism;
            builder.SetChainsAlpha(_chainsToggle == null || _chainsToggle.isOn ? 0.15f : 0f);
            builder.SetPocketAlpha(_pocketToggle == null || _pocketToggle.isOn ? 0.6f : 0f);
            if (builder.labels != null) { foreach (var g in builder.labels.groupNames) builder.labels.SetGroup(g, 0f); builder.labels.SetGroup("triad", _labelsToggle == null || _labelsToggle.isOn ? 1f : 0f); }
            m.arrows = 1f;
            builder.SetZymogen(0f, 0f);                                    // leaving the activation coda: active enzyme only
            if (builder.activationRoot != null) builder.activationRoot.gameObject.SetActive(false);
            m.substrateAlpha = 1f;
        }

        void StopExploring()
        {
            _exploring = false;
            if (picker != null && !string.IsNullOrEmpty(picker.current) && _chainsToggle != null) _chainsToggle.SetIsOnWithoutNotify(true);   // the picker turned them off
            if (picker != null) { picker.Pick(""); if (_pickerRow) _pickerRow.SetActive(false); if (_schematic) _schematic.text = ""; }
        }

        void SetStep(float v)
        {
            if (builder == null || builder.mechanism == null) return;
            StartExploring();
            var m = builder.mechanism;
            m.step = v;
            int si = Mathf.RoundToInt(v);
            bool atState = Mathf.Abs(v - si) < 0.06f;
            m.arrows = atState && (_arrowsToggle == null || _arrowsToggle.isOn) ? 1f : 0f;
            m.lbhb = (v > 0.5f && v < 6.5f) ? 0.6f : 0f;
            m.hole = (si == 2 || si == 5) ? 1f : 0f;
            if (builder.labels != null && (_labelsToggle == null || _labelsToggle.isOn))
            {
                builder.labels.SetGroup("triad", si == 2 || si == 5 ? 0f : 1f);
                builder.labels.SetGroup("hole", si == 2 || si == 5 ? 1f : 0f);
                builder.labels.SetGroup("water", si == 4 ? 1f : 0f);
            }
            m.Apply();
        }

        void Show(Checkpoint c)
        {
            _active = c;
            if (sequence != null) sequence.Pause();
            if (_panel == null) return;
            _panel.SetActive(true);
            _question.text = Fmt(c.question);
            _feedback.text = ""; _tryIt.text = "";
            _continue.gameObject.SetActive(false);
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                bool on = c.options != null && i < c.options.Length;
                _optionButtons[i].gameObject.SetActive(on);
                if (on) _optionButtons[i].GetComponentInChildren<TextMeshProUGUI>().text = Fmt(c.options[i]);
            }
        }

        void Answer(int i)
        {
            if (_active == null) return;
            if (i == _active.correct) Finish("Correct. " + _active.feedback);
            else
            {
                _active.wrongCount++;
                if (_active.wrongCount >= 2) Finish(_active.feedback);
                else _feedback.text = "Not quite. Try once more.";
            }
        }

        void Finish(string text)
        {
            _feedback.text = Fmt(text);
            _tryIt.text = Fmt(_active.tryIt);
            _active.done = true;
            foreach (var b in _optionButtons) b.gameObject.SetActive(false);
            _continue.gameObject.SetActive(true);
            // let the student try it in the scene while the panel waits
            if (_active.picker && _pickerRow != null) { StartExploring(); SetStep(1f); _pickerRow.SetActive(true); }
            else FreeLook();
        }

        void Continue()
        {
            HidePanel();
            _active = null;
            StopExploring();
            if (sequence != null) { sequence.active = true; sequence.Play(); if (orbitCamera) orbitCamera.enabled = false; }
        }

        void HidePanel() { if (_panel) _panel.SetActive(false); }

        void Pick(string aa)
        {
            if (picker == null) return;
            StartExploring(); SetStep(1f);
            builder.SetPocketAlpha(1f);
            if (builder.labels != null) builder.labels.SetGroup("pocket", 1f);
            builder.mechanism.arrows = 0f;                                 // the picker is about binding, not the arrows
            if (_chainsToggle != null && _chainsToggle.isOn) _chainsToggle.isOn = false;   // uncluttered view of the pocket
            picker.Pick(aa);
            if (_schematic) _schematic.text = Fmt($"<b>{aa}</b>: {ChSideChainPicker.Verdict(aa)}\n<size=80%><i>Schematic animation, not computed docking.</i></size>");
        }

        void PollClick()
        {
            if (_active != null && !_active.done) return;          // answer the question first
            bool down = false; Vector2 pos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) { down = true; pos = Mouse.current.position.ReadValue(); }
            else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) { down = true; pos = Touchscreen.current.primaryTouch.position.ReadValue(); }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) { down = true; pos = Input.mousePosition; }
#endif
            if (!down || cam == null || builder == null || builder.mechanism == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, 500f))
            {
                string res = builder.mechanism.ResidueOf(hit.collider);
                if (res != null && Roles.TryGetValue(res, out var role) && _info != null) _info.text = Fmt(role);
            }
        }

        // ---------------------------------------------------------------- UI
        /// <summary>TMP's own &lt;sup&gt; follows the font's superscript size, which is too small at UI sizes; use a larger raised size.</summary>
        static string Fmt(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("<sup>", "<voffset=0.42em><size=72%>").Replace("</sup>", "</size></voffset>")
                    .Replace("<sub>", "<voffset=-0.12em><size=72%>").Replace("</sub>", "</size></voffset>");
        }

        void BuildUI()
        {
            _font = TMP_Settings.defaultFontAsset;
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
            var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = cgo.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900);
            cgo.AddComponent<GraphicRaycaster>();

            // --- top-right: the mechanism, step by step
            var strip = Panel(cgo.transform, "Controls", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10, -10), new Vector2(330, 10), new Color(0, 0, 0, 0.6f));
            Layout(strip);
            Label(strip.transform, "<b>Step through the mechanism</b>", fontSize);
            _stateLabel = Label(strip.transform, "", fontSize - 2);
            _stepSlider = SliderRow(strip.transform, 0f, 7f, v => SetStep(v));
            var hold = _stepSlider.gameObject.AddComponent<SliderHold>(); hold.onHeld = h => { _sliderHeld = h; if (!h && builder != null) SetStep(Mathf.Round(_stepSlider.value)); };
            _arrowsToggle = ToggleRow(strip.transform, "Curved arrows", true, v => { if (_exploring) SetStep(builder.mechanism.step); });
            _labelsToggle = ToggleRow(strip.transform, "Labels", true, v => { if (_exploring && builder.labels != null) { foreach (var g in builder.labels.groupNames) builder.labels.SetGroup(g, 0f); if (v) SetStep(builder.mechanism.step); } });
            _pocketToggle = ToggleRow(strip.transform, "Hydrophobic pocket", true, v => { if (_exploring) builder.SetPocketAlpha(v ? 0.6f : 0f); });
            _chainsToggle = ToggleRow(strip.transform, "Protein chains", true, v => { if (_exploring) builder.SetChainsAlpha(v ? 0.15f : 0f); });
            ButtonRow(strip.transform, "Side chains in the pocket…", () => { StartExploring(); SetStep(1f); if (_pickerRow) _pickerRow.SetActive(!_pickerRow.activeSelf); });
            _pickerRow = new GameObject("Picker"); _pickerRow.transform.SetParent(strip.transform, false);
            var grid = _pickerRow.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(98, fontSize + 14); grid.spacing = new Vector2(6, 6);
            _pickerRow.AddComponent<LayoutElement>().minHeight = 2 * (fontSize + 14) + 6;
            foreach (var aa in ChSideChainPicker.Choices) { string a = aa; ButtonRow(_pickerRow.transform, a, () => Pick(a), false); }
            _pickerRow.SetActive(false);
            _schematic = Label(strip.transform, "", fontSize - 3);
            ButtonRow(strip.transform, "Free look (pause)", () => { StartExploring(); });
            _info = Label(strip.transform, "<i>Click a triad residue to see its role.</i>", fontSize - 3);

            // --- bottom-left: replay a shot
            var bar = Panel(cgo.transform, "Shots", new Vector2(0, 0), new Vector2(0, 0), new Vector2(10, 10), new Vector2(330, 10), new Color(0, 0, 0, 0.6f));
            Layout(bar);
            Label(bar.transform, "<b>Replay a shot</b>", fontSize);
            var sg = new GameObject("Shot grid"); sg.transform.SetParent(bar.transform, false);
            var g2 = sg.AddComponent<GridLayoutGroup>(); g2.cellSize = new Vector2(152, fontSize + 10); g2.spacing = new Vector2(6, 5);
            g2.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g2.constraintCount = 2;
            int rows = (shots.Count + 1) / 2;
            sg.AddComponent<LayoutElement>().minHeight = rows * (fontSize + 10) + (rows - 1) * 5;
            foreach (var s in shots) { float t = s.time; var sb = ButtonRow(sg.transform, s.label, () => ReplayShot(t), false); sb.GetComponentInChildren<TextMeshProUGUI>().fontSize = fontSize - 3; }

            // --- bottom centre: checkpoint
            _panel = Panel(cgo.transform, "Checkpoint", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(60, 20), new Vector2(760, 10), new Color(0.08f, 0.08f, 0.1f, 0.94f));
            var pl = Layout(_panel); pl.padding = new RectOffset(16, 16, 12, 12); pl.spacing = 8;
            _question = Label(_panel.transform, "", fontSize + 2);
            _optionButtons = new Button[4];
            for (int i = 0; i < 4; i++) { int idx = i; _optionButtons[i] = ButtonRow(_panel.transform, "", () => Answer(idx)); _optionButtons[i].GetComponentInChildren<TextMeshProUGUI>().fontSize = fontSize; }
            _feedback = Label(_panel.transform, "", fontSize); _feedback.color = new Color(1f, 0.85f, 0.4f);
            _feedback.gameObject.AddComponent<LayoutElement>().minHeight = fontSize + 6;   // reserved, so the answers do not jump when "Not quite" appears
            _tryIt = Label(_panel.transform, "", fontSize - 2); _tryIt.color = new Color(0.7f, 0.85f, 1f);
            _continue = ButtonRow(_panel.transform, "Continue", Continue);
            _panel.SetActive(false);
        }

        VerticalLayoutGroup Layout(GameObject go)
        {
            var vl = go.AddComponent<VerticalLayoutGroup>(); vl.padding = new RectOffset(10, 10, 10, 10); vl.spacing = 6;
            vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true;
            return vl;
        }
        GameObject Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color col)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.AddComponent<Image>().color = col;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }
        TextMeshProUGUI Label(Transform parent, string text, int size)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>(); if (_font) t.font = _font;
            t.fontSize = size; t.text = Fmt(text); t.color = Color.white; t.textWrappingMode = TextWrappingModes.Normal; t.richText = true;
            return t;
        }
        Button ButtonRow(Transform parent, string text, UnityEngine.Events.UnityAction onClick, bool layout = true)
        {
            var go = new GameObject("Button"); go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0.25f, 0.3f, 0.4f, 0.95f);
            var b = go.AddComponent<Button>(); b.onClick.AddListener(onClick);
            // the label is a layout child, so long answers wrap and the button grows to fit them
            var hl = go.AddComponent<HorizontalLayoutGroup>(); hl.padding = new RectOffset(10, 10, 5, 5);
            hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = true; hl.childForceExpandHeight = true;
            if (layout) go.AddComponent<LayoutElement>().minHeight = fontSize + 12;
            var t = Label(go.transform, text, fontSize - 1); t.alignment = TextAlignmentOptions.Center;
            return b;
        }
        Toggle ToggleRow(Transform parent, string text, bool value, UnityEngine.Events.UnityAction<bool> onChange)
        {
            var go = new GameObject("Toggle " + text); go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = fontSize + 8;
            var tg = go.AddComponent<Toggle>();
            var bg = new GameObject("Background"); bg.transform.SetParent(go.transform, false);
            var bgi = bg.AddComponent<Image>(); bgi.color = new Color(0.9f, 0.9f, 0.9f);
            var brt = bg.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(18, 18); brt.anchoredPosition = new Vector2(12, 0);
            var ck = new GameObject("Checkmark"); ck.transform.SetParent(bg.transform, false);
            var cki = ck.AddComponent<Image>(); cki.color = new Color(0.2f, 0.5f, 0.9f);
            var crt = ck.GetComponent<RectTransform>(); crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = new Vector2(3, 3); crt.offsetMax = new Vector2(-3, -3);
            tg.targetGraphic = bgi; tg.graphic = cki; tg.isOn = value; tg.onValueChanged.AddListener(onChange);
            var t = Label(go.transform, text, fontSize - 2); var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(30, 0); trt.offsetMax = Vector2.zero; t.alignment = TextAlignmentOptions.MidlineLeft;
            return tg;
        }
        Slider SliderRow(Transform parent, float min, float max, UnityEngine.Events.UnityAction<float> onChange)
        {
            var go = new GameObject("Slider"); go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = 24;
            var s = go.AddComponent<Slider>(); s.minValue = min; s.maxValue = max;
            var bg = new GameObject("Background"); bg.transform.SetParent(go.transform, false);
            bg.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);
            var brt = bg.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(0, 0.35f); brt.anchorMax = new Vector2(1, 0.65f); brt.offsetMin = brt.offsetMax = Vector2.zero;
            var fa = new GameObject("Fill Area"); fa.transform.SetParent(go.transform, false);
            var fart = fa.AddComponent<RectTransform>(); fart.anchorMin = new Vector2(0, 0.35f); fart.anchorMax = new Vector2(1, 0.65f); fart.offsetMin = new Vector2(8, 0); fart.offsetMax = new Vector2(-8, 0);
            var fill = new GameObject("Fill"); fill.transform.SetParent(fa.transform, false);
            fill.AddComponent<Image>().color = new Color(0.3f, 0.6f, 1f);
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
}
