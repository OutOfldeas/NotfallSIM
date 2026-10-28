using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Kamera im Schockraum. Sie kreist um einen Blickpunkt, der sich durch den Raum bewegen lässt:
    ///   rechte Maustaste ziehen = umsehen (drehen), Mausrad oder +/− = zoomen, mittlere Taste ziehen = verschieben,
    ///   WASD/Pfeiltasten = gehen (Umschalt = schneller), Q/E bzw. Bild↓/Bild↑ = tiefer/höher,
    ///   F oder Pos1 = zurück zur Ausgangsansicht am Patienten.
    /// Mit <see cref="Follow"/> hängt der Blickpunkt an einem bewegten Objekt (Unfall-Sandbox: Becken des Ragdolls);
    /// alle Eingaben verschieben dann den Blickpunkt relativ zu diesem Objekt.
    /// Linksklick bleibt frei für die Untersuchung. Blickpunkt und Kamera bleiben innerhalb der Raumwände;
    /// steht eine Wand im Weg, rückt die Kamera näher heran, statt durch die Wand zu schauen.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Vector3 target = new Vector3(0, 0.9f, 0.1f);
        [SerializeField] float distance = 2.6f, yaw = 180f, pitch = 38f;
        [SerializeField] float minDistance = 0.25f, maxDistance = 6f;
        public float MaxDistance { get => maxDistance; set => maxDistance = value; }
        [SerializeField] float walkSpeed = 1.4f, heightSpeed = 0.9f;
        [Tooltip("Innenraum, in dem Kamera und Blickpunkt bleiben (Weltkoordinaten).")]
        [SerializeField] Bounds room = new Bounds(new Vector3(0, 1.5f, 0), new Vector3(6.4f, 3.0f, 7.6f));
        [SerializeField] float wallMargin = 0.15f;

        Vector3 _homeTarget; float _homeDistance, _homeYaw, _homePitch;
        Vector3 _smoothTarget; float _smoothDistance;   // beim Folgen: relativ zum Ankerpunkt
        bool _init;
        Transform _follow;
        Vector3 _anchor; bool _anchorInit;
        static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        public void Configure(Vector3 focus, float dist, float yawDeg, float pitchDeg)
        {
            target = focus; distance = dist; yaw = yawDeg; pitch = pitchDeg;
            _init = false;
        }

        /// <summary>Raumgrenzen setzen (Innenmaße der Wände).</summary>
        public void SetRoom(Bounds inner) => room = inner;

        /// <summary>
        /// Blickpunkt an ein bewegtes Objekt hängen (null = fester Blickpunkt). Der mit <see cref="Configure"/> gesetzte
        /// Blickpunkt gilt dann als Versatz zu diesem Objekt.
        /// </summary>
        public void Follow(Transform t)
        {
            if (t == _follow) return;
            _follow = t;
            _anchorInit = false;
        }

        /// <summary>Blickrichtung aus einem Kameraversatz (Kamera = Blickpunkt + Versatz) und Blickpunkt setzen.</summary>
        public void ConfigureFromOffset(Vector3 focus, Vector3 cameraOffset)
        {
            var fwd = -cameraOffset.normalized;
            Configure(focus, cameraOffset.magnitude, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, Mathf.Asin(-fwd.y) * Mathf.Rad2Deg);
        }

        /// <summary>Zurück zur Ausgangsansicht am Patienten.</summary>
        public void ResetView()
        {
            target = _homeTarget; distance = _homeDistance; yaw = _homeYaw; pitch = _homePitch;
        }

        void Init()
        {
            _homeTarget = target; _homeDistance = distance; _homeYaw = yaw; _homePitch = pitch;
            _smoothTarget = target; _smoothDistance = distance;
            _init = true;
        }

        void LateUpdate()
        {
            if (!_init) Init();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f); // unabhängig von Zeitraffer und Pause

            Vector2 delta = Vector2.zero, move = Vector2.zero; float scroll = 0, rise = 0, zoomKeys = 0;
            bool rotate = false, pan = false, fast = false, reset = false;
            bool typing = IsTyping();
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
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && !typing)
            {
                move.y = Mathf.Clamp01(Key(kb.wKey) + Key(kb.upArrowKey)) - Mathf.Clamp01(Key(kb.sKey) + Key(kb.downArrowKey));
                move.x = Mathf.Clamp01(Key(kb.dKey) + Key(kb.rightArrowKey)) - Mathf.Clamp01(Key(kb.aKey) + Key(kb.leftArrowKey));
                rise = Mathf.Clamp01(Key(kb.eKey) + Key(kb.pageUpKey)) - Mathf.Clamp01(Key(kb.qKey) + Key(kb.pageDownKey));
                // Plus/Minus auf US- und deutscher Tastatur (Input System meldet die physische US-Taste: „+“ = ], „-“ = /)
                zoomKeys = Mathf.Clamp01(Key(kb.numpadPlusKey) + Key(kb.equalsKey) + Key(kb.rightBracketKey))
                         - Mathf.Clamp01(Key(kb.numpadMinusKey) + Key(kb.minusKey) + Key(kb.slashKey));
                fast = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                reset = kb.fKey.wasPressedThisFrame || kb.homeKey.wasPressedThisFrame;
            }
#else
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
            scroll = Input.mouseScrollDelta.y;
            rotate = Input.GetMouseButton(1);
            pan = Input.GetMouseButton(2);
            if (!typing)
            {
                move.y = K(KeyCode.W) + K(KeyCode.UpArrow) - K(KeyCode.S) - K(KeyCode.DownArrow);
                move.x = K(KeyCode.D) + K(KeyCode.RightArrow) - K(KeyCode.A) - K(KeyCode.LeftArrow);
                rise = K(KeyCode.E) + K(KeyCode.PageUp) - K(KeyCode.Q) - K(KeyCode.PageDown);
                zoomKeys = Mathf.Clamp01(K(KeyCode.KeypadPlus) + K(KeyCode.Equals) + K(KeyCode.Plus))
                         - Mathf.Clamp01(K(KeyCode.KeypadMinus) + K(KeyCode.Minus));
                fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                reset = Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Home);
            }
#endif
            if (scroll != 0 && PointerOverUi()) scroll = 0; // Mausrad in Listen/Panels scrollt dort, nicht die Kamera
            if (reset) ResetView();

            if (rotate) { yaw += delta.x * 3f; pitch = Mathf.Clamp(pitch - delta.y * 3f, -30f, 89f); }
            if (pan) target -= (transform.right * delta.x + transform.up * delta.y) * 0.01f * Mathf.Max(distance, 0.5f);

            // Gehen: in Blickrichtung, aber waagerecht (wie ein Mensch, der durch den Raum läuft)
            var yawRot = Quaternion.Euler(0, yaw, 0);
            var walk = yawRot * new Vector3(move.x, 0, move.y);
            if (walk.sqrMagnitude > 1) walk.Normalize();
            float speed = walkSpeed * (fast ? 2.5f : 1f);
            target += walk * speed * dt + Vector3.up * rise * heightSpeed * (fast ? 2f : 1f) * dt;

            distance *= 1 - scroll * 0.12f;
            distance *= Mathf.Pow(2f, -zoomKeys * dt * 1.5f);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            // Ankerpunkt beim Folgen (leicht geglättet gegen das Zittern der Physik)
            Vector3 anchor = Vector3.zero;
            if (_follow != null)
            {
                _anchor = _anchorInit ? Vector3.Lerp(_anchor, _follow.position, 1 - Mathf.Exp(-20f * dt)) : _follow.position;
                _anchorInit = true;
                anchor = _anchor;
            }

            // Blickpunkt im Raum halten
            var inner = Inner();
            var world = anchor + target;
            world = new Vector3(Mathf.Clamp(world.x, inner.min.x, inner.max.x),
                                Mathf.Clamp(world.y, inner.min.y, inner.max.y),
                                Mathf.Clamp(world.z, inner.min.z, inner.max.z));
            target = world - anchor;

            // leicht geglättet, damit Gehen und Zoomen nicht ruckeln
            float k = 1 - Mathf.Exp(-14f * dt);
            _smoothTarget = Vector3.Lerp(_smoothTarget, target, k);
            _smoothDistance = Mathf.Lerp(_smoothDistance, distance, k);

            var rot = Quaternion.Euler(pitch, yaw, 0);
            var back = -(rot * Vector3.forward);
            var focus = anchor + _smoothTarget;
            float d = Mathf.Min(_smoothDistance, DistanceToWall(focus, back, inner));
            transform.position = focus + back * d;
            transform.rotation = rot;
        }

        Bounds Inner()
        {
            var b = room;
            b.Expand(-2 * wallMargin);
            return b;
        }

        /// <summary>Wie weit man vom Blickpunkt rückwärts gehen kann, bis eine Wand (bzw. Boden/Decke) kommt.</summary>
        static float DistanceToWall(Vector3 from, Vector3 dir, Bounds box)
        {
            float t = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(dir[i]) < 1e-5f) continue;
                float wall = dir[i] > 0 ? box.max[i] : box.min[i];
                t = Mathf.Min(t, Mathf.Max(0, (wall - from[i]) / dir[i]));
            }
            return t;
        }

        static bool PointerOverUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es);
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return false;
            data.position = m.position.ReadValue();
#else
            data.position = Input.mousePosition;
#endif
            Hits.Clear();
            es.RaycastAll(data, Hits);
            foreach (var h in Hits)
                if (h.module is UnityEngine.UI.GraphicRaycaster) return true; // nur UI zählt, nicht der 3D-Patient
            return false;
        }

        static bool IsTyping()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            return sel != null && (sel.GetComponent<TMPro.TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
        }

#if ENABLE_INPUT_SYSTEM
        static float Key(UnityEngine.InputSystem.Controls.ButtonControl b) => b != null && b.isPressed ? 1f : 0f;
#else
        static float K(KeyCode c) => Input.GetKey(c) ? 1f : 0f;
#endif
    }
}
