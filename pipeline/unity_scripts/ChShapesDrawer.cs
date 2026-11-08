// ChShapesDrawer.cs — draws the curved arrows, dashed hydrogen bonds and circled charges of ChMechanism with the
// Shapes package (Freya Holmér, Unity Asset Store), so they look like the 2D panels: smooth, constant-width lines.
// Compiled only when the scripting define CH_SHAPES is set (Project Settings > Player). Shapes is a paid asset and is
// not in the public repository; without it, ChMechanism draws its own tube arrows, cylinder dashes and quad charges.
// Needs the Shapes render feature on the camera's URP renderer (Shapes > Settings adds it).
#if CH_SHAPES
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Shapes;

namespace ChTutorial
{
    [ExecuteAlways]
    public class ChShapesDrawer : ImmediateModeShapeDrawer
    {
        public ChMechanism mechanism;
        [Header("Widths in noots (1/100 of the screen's shorter side)")]
        public float arrowThickness = 0.45f;
        public float hbondThickness = 0.30f;
        public float chargeThickness = 0.17f;
        [Header("Look")]
        public float arrowHeadScale = 1.15f;           // times the mechanism's head length
        public float dashSize = 1.5f, dashSpacing = 1.7f;   // relative to the line width
        public Color chargeColor = new Color(1.6f, 1.6f, 1.6f, 1f);   // above 1 so tone mapping keeps it white
        public Color arrowColor = new Color(0.40f, 0.66f, 1.35f, 1f);   // the panels' blue, a little over 1 to stay bright after tone mapping

        readonly List<PolylinePath> _paths = new List<PolylinePath>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (mechanism != null) { mechanism.externalDraw = true; mechanism.Apply(); }
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (mechanism != null) { mechanism.externalDraw = false; mechanism.Apply(); }
            foreach (var p in _paths) p.Dispose();
            _paths.Clear();
        }

        void Update() { if (mechanism != null && !mechanism.externalDraw) mechanism.externalDraw = true; }

        public override void DrawShapes(Camera cam)
        {
            var m = mechanism;
            if (m == null || (m.hbondDraws.Count == 0 && m.arrowDraws.Count == 0 && m.chargeDraws.Count == 0)) return;
            Transform t = m.transform;
            Vector3 viewL = t.InverseTransformDirection(cam.transform.forward);
            Vector3 rightL = t.InverseTransformDirection(cam.transform.right);
            Vector3 upL = t.InverseTransformDirection(cam.transform.up);

            using (Draw.Command(cam))
            {
                Draw.ResetAllDrawStates();
                Draw.Matrix = t.localToWorldMatrix;
                Draw.BlendMode = ShapesBlendMode.Transparent;
                Draw.ThicknessSpace = ThicknessSpace.Noots;
                Draw.LineGeometry = LineGeometry.Billboard;
                Draw.PolylineGeometry = PolylineGeometry.Billboard;
                Draw.DiscGeometry = DiscGeometry.Billboard;
                Draw.LineEndCaps = LineEndCap.Round;

                // hydrogen bonds: dashed, hidden behind atoms like the bonds they stand for
                Draw.ZTest = CompareFunction.LessEqual;
                Draw.UseDashes = true;
                Draw.DashStyle = DashStyle.RelativeDashes(DashType.Rounded, dashSize, dashSpacing);
                foreach (var h in m.hbondDraws)
                {
                    Color c = Color.Lerp(m.hbondColor, m.glowColor * 1.6f, h.glow); c.a = h.weight;
                    Draw.Line(h.a, h.b, hbondThickness * (1f + 0.9f * h.glow), c);
                }
                Draw.UseDashes = false;

                // arrows and charges: on top of everything, as in the panels
                Draw.ZTest = CompareFunction.Always;
                while (_paths.Count < m.arrowDraws.Count) _paths.Add(new PolylinePath());
                Color ac = arrowColor;
                for (int k = 0; k < m.arrowDraws.Count; k++)
                {
                    var a = m.arrowDraws[k];
                    var path = _paths[k];                // one path per arrow: a command keeps each path's mesh until it renders
                    path.ClearAllPoints();
                    const int N = 28;
                    for (int i = 0; i <= N; i++) path.AddPoint(ChMeshes.Bezier(a.p0, a.p1, a.p2, a.p3, a.f * i / N));
                    Draw.Polyline(path, false, arrowThickness, PolylineJoins.Round, ac);
                    // head: a flat triangle in the screen plane, pointing along the curve
                    Vector3 tip = ChMeshes.Bezier(a.p0, a.p1, a.p2, a.p3, a.f);
                    Vector3 tan = ChMeshes.BezierTangent(a.p0, a.p1, a.p2, a.p3, a.f);
                    Vector3 dir = tan - Vector3.Dot(tan, viewL) * viewL;
                    if (dir.sqrMagnitude < 1e-12f) continue;
                    dir.Normalize();
                    Vector3 side = Vector3.Cross(dir, viewL).normalized;
                    float hs = Mathf.Clamp01(a.f / 0.2f);
                    float L = m.arrowHeadLength * arrowHeadScale * hs, W = m.arrowHeadRadius * 0.95f * hs;
                    Vector3 back = tip - dir * (L * 0.15f);  // tuck the base over the line's end
                    Draw.Triangle(back + dir * L, back + side * W, back - side * W, ac);
                }

                foreach (var c in m.chargeDraws)
                {
                    float r = c.size;
                    Draw.Ring(c.pos, r, chargeThickness, chargeColor);
                    Draw.Line(c.pos - rightL * r * 0.55f, c.pos + rightL * r * 0.55f, chargeThickness, LineEndCap.None, chargeColor);
                    if (c.plus) Draw.Line(c.pos - upL * r * 0.55f, c.pos + upL * r * 0.55f, chargeThickness, LineEndCap.None, chargeColor);
                }
            }
        }
    }
}
#endif
