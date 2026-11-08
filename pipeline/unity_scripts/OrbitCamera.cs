// OrbitCamera.cs — mouse/touch orbit, zoom and pan around a target. Works with either input backend.
// Disable this component while a Timeline camera clip is driving the camera; re-enable for the twin.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HbTutorial
{
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 14f;
        public float minDistance = 1.5f, maxDistance = 40f;
        public float yaw = 30f, pitch = 20f;
        public float roll = 0f;            // kept from a scripted shot whose camera is tilted, so free look starts on the same view
        public float orbitSpeed = 0.25f;   // degrees per pixel
        public float zoomSpeed = 1.2f;
        public float panSpeed = 0.01f;
        public float smoothing = 12f;
        public bool blockWhenPointerOverUI = true;

        Vector3 _pivot;
        float _curYaw, _curPitch, _curDist;
        Vector3 _curPivot;
        Vector2 _lastPointer; bool _dragging; int _dragButton;
        float _lastPinch = -1f;

        bool _synced;

        void Start()
        {
            if (_synced) return;               // first enabled by free look: keep the pose SyncFromCamera adopted
            _pivot = target ? target.position : Vector3.zero;
            _curYaw = yaw; _curPitch = pitch; _curDist = distance; _curPivot = _pivot;
            Snap();
        }

        public void FocusOn(Transform t, float dist = -1f)
        {
            target = t; _pivot = t ? t.position : Vector3.zero;
            if (dist > 0f) distance = dist;
        }
        public void FocusOn(Vector3 p, float dist = -1f) { target = null; _pivot = p; if (dist > 0f) distance = dist; }

        /// <summary>Adopt the camera's current pose (after a scripted shot) so free-look starts where the shot left off.</summary>
        public void SyncFromCamera()
        {
            Vector3 fwd = transform.forward;
            float d = target ? Vector3.Distance(transform.position, target.position) : distance;
            _pivot = transform.position + fwd * d; target = null;
            var e = transform.rotation.eulerAngles;
            pitch = e.x > 180f ? e.x - 360f : e.x; yaw = e.y; roll = e.z > 180f ? e.z - 360f : e.z; distance = d;
            _curYaw = yaw; _curPitch = pitch; _curDist = distance; _curPivot = _pivot; _synced = true;
        }

        void Snap()
        {
            var rot = Quaternion.Euler(_curPitch, _curYaw, roll);
            transform.position = _curPivot + rot * new Vector3(0f, 0f, -_curDist);
            transform.rotation = rot;
        }

        void LateUpdate()
        {
            if (target) _pivot = target.position;
            ReadInput();
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            _curYaw = Mathf.Lerp(_curYaw, yaw, k);
            _curPitch = Mathf.Lerp(_curPitch, pitch, k);
            _curDist = Mathf.Lerp(_curDist, distance, k);
            _curPivot = Vector3.Lerp(_curPivot, _pivot, k);
            Snap();
        }

        bool OverUI()
        {
            return blockWhenPointerOverUI && UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        void ReadInput()
        {
            Vector2 pos = Vector2.zero; float scroll = 0f;
            bool left = false, right = false, middle = false, leftDown = false, anyDown = false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m != null)
            {
                pos = m.position.ReadValue(); scroll = m.scroll.ReadValue().y / 120f;
                left = m.leftButton.isPressed; right = m.rightButton.isPressed; middle = m.middleButton.isPressed;
                leftDown = m.leftButton.wasPressedThisFrame; anyDown = leftDown || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame;
            }
            var ts = Touchscreen.current;
            if (ts != null && ts.touches.Count > 0)
            {
                int n = 0; Vector2 p0 = Vector2.zero, p1 = Vector2.zero;
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (n == 0) p0 = t.position.ReadValue(); else if (n == 1) p1 = t.position.ReadValue();
                    n++;
                }
                if (n == 1) { pos = p0; left = true; leftDown = ts.touches[0].press.wasPressedThisFrame; anyDown = leftDown; }
                else if (n >= 2)
                {
                    float d = Vector2.Distance(p0, p1);
                    if (_lastPinch > 0f) distance = Mathf.Clamp(distance * (_lastPinch / Mathf.Max(d, 1f)), minDistance, maxDistance);
                    _lastPinch = d; _dragging = false; return;
                }
                if (n < 2) _lastPinch = -1f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            pos = Input.mousePosition; scroll = Input.mouseScrollDelta.y;
            left = Input.GetMouseButton(0); right = Input.GetMouseButton(1); middle = Input.GetMouseButton(2);
            leftDown = Input.GetMouseButtonDown(0); anyDown = leftDown || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            if (Input.touchCount == 2)
            {
                float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (_lastPinch > 0f) distance = Mathf.Clamp(distance * (_lastPinch / Mathf.Max(d, 1f)), minDistance, maxDistance);
                _lastPinch = d; _dragging = false; return;
            }
            _lastPinch = -1f;
#endif
            if (anyDown)
            {
                if (OverUI()) { _dragging = false; return; }
                _dragging = true; _lastPointer = pos; _dragButton = left ? 0 : (right ? 1 : 2);
            }
            if (!(left || right || middle)) _dragging = false;
            if (_dragging)
            {
                Vector2 delta = pos - _lastPointer; _lastPointer = pos;
                if (_dragButton == 0)
                {
                    yaw += delta.x * orbitSpeed; pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -89f, 89f);
                }
                else
                {
                    var rot = Quaternion.Euler(pitch, yaw, roll);
                    _pivot -= rot * new Vector3(delta.x, delta.y, 0f) * panSpeed * distance * 0.1f;
                    target = null;
                }
            }
            if (Mathf.Abs(scroll) > 0.001f && !OverUI())
                distance = Mathf.Clamp(distance * Mathf.Pow(1f + 0.1f * zoomSpeed, -scroll), minDistance, maxDistance);
        }
    }
}
