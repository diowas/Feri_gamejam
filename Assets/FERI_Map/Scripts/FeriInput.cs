using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FeriMap
{

    public static class FeriInput
    {
#if ENABLE_INPUT_SYSTEM
        public static Vector2 Move()
        {
            var k = Keyboard.current; if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
            float y = (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }
        public static Vector2 Look()
        {
            var m = Mouse.current; if (m == null) return Vector2.zero;
            return m.delta.ReadValue() * 0.05f;
        }
        public static bool Run() { var k = Keyboard.current; return k != null && k.leftShiftKey.isPressed; }
        public static bool Crouch() { var k = Keyboard.current; return k != null && k.leftCtrlKey.isPressed; }
        public static bool Interact()
        {
            var k = Keyboard.current; var m = Mouse.current;
            return (k != null && k.eKey.wasPressedThisFrame) || (m != null && m.leftButton.wasPressedThisFrame);
        }
        public static bool Flashlight() { var k = Keyboard.current; return k != null && k.fKey.wasPressedThisFrame; }
        public static bool Escape() { var k = Keyboard.current; return k != null && k.escapeKey.wasPressedThisFrame; }
        public static bool Click() { var m = Mouse.current; return m != null && m.leftButton.wasPressedThisFrame; }
#else
        public static Vector2 Move()
        {
            float x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }
        public static Vector2 Look() { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); }
        public static bool Run() { return Input.GetKey(KeyCode.LeftShift); }
        public static bool Crouch() { return Input.GetKey(KeyCode.LeftControl); }
        public static bool Interact() { return Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0); }
        public static bool Flashlight() { return Input.GetKeyDown(KeyCode.F); }
        public static bool Escape() { return Input.GetKeyDown(KeyCode.Escape); }
        public static bool Click() { return Input.GetMouseButtonDown(0); }
#endif
    }
}
