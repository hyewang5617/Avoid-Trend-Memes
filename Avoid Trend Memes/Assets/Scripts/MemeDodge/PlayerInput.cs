using UnityEngine;
using UnityEngine.InputSystem;

namespace MemeDodge
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInput : MonoBehaviour
    {
        bool jump;
        public float Direction { get; private set; }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused)
            {
                Clear();
                return;
            }
            Direction = (keyboard.rightArrowKey.isPressed ? 1 : 0)
                - (keyboard.leftArrowKey.isPressed ? 1 : 0);
            if (keyboard.spaceKey.wasPressedThisFrame) jump = true;
        }

        public bool ConsumeJump()
        {
            bool requested = jump;
            jump = false;
            return requested;
        }

        public void Clear() { Direction = 0; jump = false; }
        void OnDisable() => Clear();
        void OnApplicationFocus(bool focused) { if (!focused) Clear(); }
    }
}
