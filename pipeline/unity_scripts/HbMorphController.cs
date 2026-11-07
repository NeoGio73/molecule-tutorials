// HbMorphController.cs — one slider drives every morphable in the scene.
// Animate "t" from a Timeline Animation track, or scrub it in the Inspector / the twin's slider.
using System.Collections.Generic;
using UnityEngine;

namespace HbTutorial
{
    public class HbMorphController : MonoBehaviour
    {
        [Range(0f, 1f)] public float t = 0f;
        [Range(1f, 5f)] public float feExaggeration = 1f;   // scales the Fe and proximal-His displacement (shot 3)
        public bool showChains = true;
        [Range(0f, 1f)] public float chainsAlpha = 1f;    // fade for the chain tubes (1 = opaque)
        public bool showGhost = false;
        public bool showHemes = true;
        public bool showResidues = true;
        public bool showIonPairs = true;
        public bool showO2 = true;
        public bool showSickle = false;

        public Transform chainsRoot, ghostRoot, hemesRoot, o2Root, residuesRoot, ionPairsRoot, sickleRoot;

        readonly List<HbMorphable> _morphables = new List<HbMorphable>();
        readonly List<StickGroup> _exaggerated = new List<StickGroup>();
        float _lastT = -1f, _lastEx = -1f, _lastAlpha = -1f;
        bool _c, _g, _h, _r, _i, _o, _s, _init;

        public void Rescan()
        {
            _morphables.Clear(); _exaggerated.Clear();
            GetComponentsInChildren(true, _morphables);
            foreach (var m in _morphables)
            {
                var sg = m as StickGroup;
                if (sg != null && sg.tag == "heme") _exaggerated.Add(sg);   // the proximal His follows its heme's Fe
            }
            _lastT = -1f; _lastEx = -1f; _lastAlpha = -1f; _init = false;
        }

        void OnEnable() { Rescan(); }

        void Update() { Apply(); }

        public void ForceApply() { _lastT = -1f; _lastEx = -1f; _lastAlpha = -1f; Apply(); }

        public void Apply()
        {
            if (!Mathf.Approximately(feExaggeration, _lastEx))
            {
                _lastEx = feExaggeration; _lastT = -1f;
                foreach (var sg in _exaggerated) sg.exaggeration = feExaggeration;
            }
            if (!Mathf.Approximately(chainsAlpha, _lastAlpha) && chainsRoot != null)
            {
                _lastAlpha = chainsAlpha;
                foreach (var tube in chainsRoot.GetComponentsInChildren<ChainTube>(true)) tube.SetAlpha(chainsAlpha);
            }
            if (!Mathf.Approximately(t, _lastT))
            {
                _lastT = t;
                for (int i = 0; i < _morphables.Count; i++) if (_morphables[i] != null) _morphables[i].SetT(t);
            }
            if (!_init || _c != showChains || _g != showGhost || _h != showHemes || _r != showResidues || _i != showIonPairs || _o != showO2 || _s != showSickle)
            {
                _init = true; _c = showChains; _g = showGhost; _h = showHemes; _r = showResidues; _i = showIonPairs; _o = showO2; _s = showSickle;
                if (chainsRoot) chainsRoot.gameObject.SetActive(showChains);
                if (ghostRoot) ghostRoot.gameObject.SetActive(showGhost);
                if (hemesRoot) hemesRoot.gameObject.SetActive(showHemes);
                if (residuesRoot) residuesRoot.gameObject.SetActive(showResidues);
                if (ionPairsRoot) ionPairsRoot.gameObject.SetActive(showIonPairs);
                if (o2Root) o2Root.gameObject.SetActive(showO2);
                if (sickleRoot) sickleRoot.gameObject.SetActive(showSickle);
            }
        }

        // Convenience for UI sliders / buttons
        public void SetT(float v) { t = Mathf.Clamp01(v); }
        public void ToggleChains(bool v) { showChains = v; }
        public void ToggleGhost(bool v) { showGhost = v; }
        public void ToggleHemes(bool v) { showHemes = v; }
        public void ToggleResidues(bool v) { showResidues = v; }
        public void ToggleIonPairs(bool v) { showIonPairs = v; }
        public void ToggleO2(bool v) { showO2 = v; }
        public void ToggleSickle(bool v) { showSickle = v; }

#if UNITY_EDITOR
        void OnValidate() { if (!Application.isPlaying) { Rescan(); Apply(); } }
#endif
    }
}
