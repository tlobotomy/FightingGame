using UnityEngine;
using UnityEngine.InputSystem;
using FightingGame.Data;

namespace FightingGame.Runtime {
    /// <summary>
    /// Reads raw input from Unity's new Input System and converts it
    /// into fighting-game DirectionInput/ButtonInput values.
    ///
    /// Requires a PlayerInput component on the same GameObject.
    /// For local versus, Unity's PlayerInputManager assigns each
    /// instance a different device automatically.
    ///
    /// IMPORTANT: All directions are pre-flipped based on FacingSign,
    /// so everything downstream (buffer, parser, controller) thinks
    /// in terms of "forward" and "back", never "left" and "right".
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class InputDetector : MonoBehaviour {
        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private Vector2 _rawStick;
        private ButtonInput _heldButton = ButtonInput.None;
        private bool _buttonDownThisFrame;
        private bool _buttonUpThisFrame;
        private ButtonInput _releasedButton = ButtonInput.None;

        /// <summary>
        /// +1 when the character faces right, -1 when facing left.
        /// Set by PlayerController or MatchManager every frame
        /// before Poll() is called.
        /// </summary>
        [HideInInspector] public int FacingSign = 1;

        /// <summary>
        /// Deadzone threshold for converting analog stick to digital 8-way.
        /// </summary>
        [Range(0.1f, 0.9f)]
        public float StickDeadzone = 0.4f;

        // ──────────────────────────────────────
        //  INPUT SYSTEM CALLBACKS
        //  Wire these in the PlayerInput component
        //  (Behavior = "Send Messages" or "Invoke C# Events")
        // ──────────────────────────────────────

        public void OnMove(InputAction.CallbackContext ctx) {
            _rawStick = ctx.ReadValue<Vector2>();
        }

        // GGXX 4+1 button layout: P / K / S / HS / D
        public void OnPunch(InputAction.CallbackContext ctx) => HandleButton(ctx, ButtonInput.Punch);
        public void OnKick(InputAction.CallbackContext ctx) => HandleButton(ctx, ButtonInput.Kick);
        public void OnSlash(InputAction.CallbackContext ctx) => HandleButton(ctx, ButtonInput.Slash);
        public void OnHeavySlash(InputAction.CallbackContext ctx) => HandleButton(ctx, ButtonInput.HeavySlash);
        public void OnDust(InputAction.CallbackContext ctx) => HandleButton(ctx, ButtonInput.Dust);

        private void HandleButton(InputAction.CallbackContext ctx, ButtonInput btn) {
            if (ctx.started) {
                _heldButton = btn;
                _buttonDownThisFrame = true;
            }
            else if (ctx.canceled) {
                _releasedButton = btn;
                _buttonUpThisFrame = true;

                if (_heldButton == btn)
                    _heldButton = ButtonInput.None;
            }
        }

        // ──────────────────────────────────────
        //  POLLING
        // ──────────────────────────────────────

        /// <summary>
        /// Called once per game tick by PlayerController.
        /// Reads the current raw state, converts to an InputFrame,
        /// and resets per-frame flags.
        /// </summary>
        public InputFrame Poll(int gameFrame) {
            DirectionInput dir = ConvertStickToDirection(_rawStick, FacingSign);

            var frame = new InputFrame {
                Direction = dir,
                Button = _heldButton,
                ButtonPressed = _buttonDownThisFrame,
                ButtonReleased = _buttonUpThisFrame,
                Frame = gameFrame
            };

            // If a button was released this frame, record which one
            // so the parser can use it for negative edge.
            // The "Button" field stays as the held button (or None),
            // while ButtonReleased flag + the released button identity
            // travel through the frame.
            if (_buttonUpThisFrame) {
                frame.Button = _releasedButton;
            }

            // Reset per-frame flags
            _buttonDownThisFrame = false;
            _buttonUpThisFrame = false;
            _releasedButton = ButtonInput.None;

            return frame;
        }

        // ──────────────────────────────────────
        //  STICK → DIRECTION CONVERSION
        // ──────────────────────────────────────

        /// <summary>
        /// Converts analog stick + facing direction into a DirectionInput.
        /// Horizontal axis is flipped by facingSign so that "positive = forward".
        /// </summary>
        private DirectionInput ConvertStickToDirection(Vector2 stick, int facingSign) {
            DirectionInput dir = DirectionInput.None;

            // Flip horizontal so "right on the stick" means "forward"
            // when facing right, and "back" when facing left.
            float h = stick.x * facingSign;
            float v = stick.y;

            if (h > StickDeadzone) dir |= DirectionInput.Forward;
            if (h < -StickDeadzone) dir |= DirectionInput.Back;
            if (v > StickDeadzone) dir |= DirectionInput.Up;
            if (v < -StickDeadzone) dir |= DirectionInput.Down;

            return dir;
        }

        // ──────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────

        /// <summary>
        /// Converts a DirectionInput to numpad notation.
        /// Useful for debug display and motion matching.
        /// </summary>
        public static NumpadDirection ToNumpad(DirectionInput dir) {
            bool u = dir.HasFlag(DirectionInput.Up);
            bool d = dir.HasFlag(DirectionInput.Down);
            bool f = dir.HasFlag(DirectionInput.Forward);
            bool b = dir.HasFlag(DirectionInput.Back);

            if (d && b) return NumpadDirection.DownBack;
            if (d && f) return NumpadDirection.DownForward;
            if (u && b) return NumpadDirection.UpBack;
            if (u && f) return NumpadDirection.UpForward;
            if (d) return NumpadDirection.Down;
            if (u) return NumpadDirection.Up;
            if (b) return NumpadDirection.Back;
            if (f) return NumpadDirection.Forward;

            return NumpadDirection.Neutral;
        }

        /// <summary>
        /// Converts a NumpadDirection back to DirectionInput flags.
        /// Used by the parser when comparing buffer contents to motion sequences.
        /// </summary>
        public static DirectionInput FromNumpad(NumpadDirection np) {
            switch (np) {
                case NumpadDirection.UpBack: return DirectionInput.Up | DirectionInput.Back;
                case NumpadDirection.Up: return DirectionInput.Up;
                case NumpadDirection.UpForward: return DirectionInput.Up | DirectionInput.Forward;
                case NumpadDirection.Back: return DirectionInput.Back;
                case NumpadDirection.Neutral: return DirectionInput.None;
                case NumpadDirection.Forward: return DirectionInput.Forward;
                case NumpadDirection.DownBack: return DirectionInput.Down | DirectionInput.Back;
                case NumpadDirection.Down: return DirectionInput.Down;
                case NumpadDirection.DownForward: return DirectionInput.Down | DirectionInput.Forward;
                default: return DirectionInput.None;
            }
        }
    }
}