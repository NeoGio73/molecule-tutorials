// IonPairLine.cs — a glowing line between two atoms of a T-state ion pair.
// Yellow and solid in T, reddening and fading as the pair stretches toward R.
using UnityEngine;

namespace HbTutorial
{
    [RequireComponent(typeof(LineRenderer))]
    public class IonPairLine : HbMorphable
    {
        public string pairName, side;
        public Transform a, b;
        public float distT, distR;         // angstrom, from the JSON (for labels)
        public float breakStart = 0.35f;   // t at which the pair starts to fade
        public float breakEnd = 0.75f;
        public float widthT = 0.025f;
        public bool breaks = true;         // false = a permanent link (Fe–His coordination bond)
        public Color color = new Color(1.0f, 0.95f, 0.35f);

        LineRenderer _lr;
        Material _mat;

        public void Init(HbIonPair p, Transform from, Transform to)
        {
            pairName = p.name; side = p.side; a = from; b = to; distT = p.distT; distR = p.distR;
            _lr = GetComponent<LineRenderer>();
            _lr.positionCount = 2; _lr.useWorldSpace = true;
            _lr.startWidth = _lr.endWidth = widthT;
            _lr.numCapVertices = 4;
            _mat = HbStyle.LineMat(color);
            _lr.sharedMaterial = _mat;
            _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetT(0f);
        }

        public override void SetT(float t)
        {
            if (_lr == null)
            {
                _lr = GetComponent<LineRenderer>();
                // work on a private copy so the colour animation never writes into a saved material asset
                if (_lr != null && _lr.sharedMaterial != null) { _mat = new Material(_lr.sharedMaterial); _lr.sharedMaterial = _mat; }
            }
            if (_lr == null || _mat == null || a == null || b == null) return;
            _lr.SetPosition(0, a.position); _lr.SetPosition(1, b.position);
            float k = breaks ? Mathf.InverseLerp(breakStart, breakEnd, t) : 0f;   // 0 intact .. 1 broken
            Color c = Color.Lerp(color, HbStyle.IonPairBrokenColor, k);
            c.a = 1f - k;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c); else _mat.color = c;
            float w = widthT * (1f - 0.7f * k);
            _lr.startWidth = _lr.endWidth = w;
            _lr.enabled = k < 0.999f;
        }

        void LateUpdate()
        {
            // atoms move under the morph; keep endpoints attached
            if (_lr != null && _lr.enabled && a != null && b != null) { _lr.SetPosition(0, a.position); _lr.SetPosition(1, b.position); }
        }
    }
}
