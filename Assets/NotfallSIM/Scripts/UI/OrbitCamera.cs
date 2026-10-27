using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Kamera um den Patienten: rechte Maustaste ziehen = drehen, Mausrad = zoomen, mittlere Taste = verschieben.
    /// Linksklick bleibt frei für die Untersuchung.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Vector3 target = new Vector3(0, 0.9f, 0.1f);
        [SerializeField] float distance = 2.6f, yaw = 180f, pitch = 38f;
        [SerializeField] float minDistance = 0.6f, maxDistance = 5f;

        public void Configure(Vector3 focus, float dist, float yawDeg, float pitchDeg) { target = focus; distance = dist; yaw = yawDeg; pitch = pitchDeg; }

        void LateUpdate()
        {
            Vector2 delta = Vector2.zero; float scroll = 0; bool rotate = false, pan = false;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                delta = mouse.delta.ReadValue() * 0.1f;
                scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) >= 10f) scroll /= 120f; // ältere Input-System-Versionen liefern 120 je Raste
                rotate = mouse.rightButton.isPressed;
                pan = mouse.middleButton.isPressed;
            }
#else
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
            scroll = Input.mouseScrollDelta.y;
            rotate = Input.GetMouseButton(1);
            pan = Input.GetMouseButton(2);
#endif
            if (rotate) { yaw += delta.x * 3f; pitch = Mathf.Clamp(pitch - delta.y * 3f, 5f, 85f); }
            if (pan) target -= (transform.right * delta.x + transform.up * delta.y) * 0.01f * distance;
            distance = Mathf.Clamp(distance * (1 - scroll * 0.1f), minDistance, maxDistance);

            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
