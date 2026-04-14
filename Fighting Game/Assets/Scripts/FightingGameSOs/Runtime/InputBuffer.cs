using System;
using UnityEngine;
using FightingGame.Data;

namespace FightingGame.Runtime {
    /// <summary>
    /// Raw snapshot of input state on a single game frame.
    /// Created by InputDetector, stored by InputBuffer, read by InputParser.
    ///
    /// MULTI-BUTTON SUPPORT:
    ///   HeldButtons is a flags enum — multiple buttons can be held simultaneously.
    ///   PressedButtons tracks which buttons transitioned from up→down THIS frame.
    ///   ReleasedButtons tracks which buttons transitioned from down→up THIS frame.
    ///   This fixes the original single-button limitation that broke throw (P+K)
    ///   and taunt (HS+D) detection.
    /// </summary>
    [Serializable]
    public struct InputFrame {
        public DirectionInput Direction;

        /// <summary>All buttons currently held down this frame (flags).</summary>
        public ButtonFlags HeldButtons;

        /// <summary>Buttons that were JUST pressed this frame (up→down transition).</summary>
        public ButtonFlags PressedButtons;

        /// <summary>Buttons that were JUST released this frame (down→up transition).</summary>
        public ButtonFlags ReleasedButtons;

        /// <summary>The game frame this was recorded on.</summary>
        public int Frame;

        // ── Legacy compatibility accessors ──
        // These allow existing code that checks a single Button/ButtonPressed
        // to still function. They return the highest-priority held/pressed button.

        /// <summary>Legacy: returns the first held button found (for move resolution).</summary>
        public ButtonInput Button {
            get {
                if (HeldButtons.HasFlag(ButtonFlags.Dust)) return ButtonInput.Dust;
                if (HeldButtons.HasFlag(ButtonFlags.HeavySlash)) return ButtonInput.HeavySlash;
                if (HeldButtons.HasFlag(ButtonFlags.Slash)) return ButtonInput.Slash;
                if (HeldButtons.HasFlag(ButtonFlags.Kick)) return ButtonInput.Kick;
                if (HeldButtons.HasFlag(ButtonFlags.Punch)) return ButtonInput.Punch;
                return ButtonInput.None;
            }
        }

        /// <summary>Legacy: true if ANY button was pressed this frame.</summary>
        public bool ButtonPressed => PressedButtons != ButtonFlags.None;

        /// <summary>Legacy: true if ANY button was released this frame.</summary>
        public bool ButtonReleased => ReleasedButtons != ButtonFlags.None;
    }

    /// <summary>
    /// Fixed-size ring buffer that stores the last N frames of input.
    /// The parser reads backward through this to match motions.
    ///
    /// Not a MonoBehaviour — owned and ticked by PlayerController.
    /// </summary>
    public class InputBuffer {
        private readonly InputFrame[] _buffer;
        private int _head;

        /// <summary>How many frames have been recorded (capped at capacity).</summary>
        public int Count { get; private set; }

        /// <summary>Ring buffer capacity (default 60 = one second at 60fps).</summary>
        public int Capacity => _buffer.Length;

        public InputBuffer(int capacity = 60) {
            _buffer = new InputFrame[capacity];
        }

        /// <summary>Push a new frame onto the buffer (called once per game tick).</summary>
        public void Push(InputFrame frame) {
            _buffer[_head] = frame;
            _head = (_head + 1) % _buffer.Length;
            Count = Mathf.Min(Count + 1, _buffer.Length);
        }

        /// <summary>
        /// Read a frame from the buffer.
        /// framesAgo = 0 is the most recent, 1 is one frame before that, etc.
        /// </summary>
        public InputFrame Get(int framesAgo) {
            if (framesAgo >= Count) return default;
            int index = ((_head - 1 - framesAgo) % _buffer.Length + _buffer.Length) % _buffer.Length;
            return _buffer[index];
        }

        /// <summary>Returns the most recent frame.</summary>
        public InputFrame Current => Count > 0 ? Get(0) : default;

        /// <summary>
        /// Was a specific direction held at any point within the last `window` frames?
        /// </summary>
        public bool DirectionInWindow(DirectionInput dir, int window) {
            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++)
                if (Get(i).Direction.HasFlag(dir)) return true;
            return false;
        }

        /// <summary>
        /// Was a specific button pressed (not held — the initial press)
        /// within the last `window` frames? Uses the new flags-based system.
        /// </summary>
        public bool ButtonPressedInWindow(ButtonInput btn, int window) {
            ButtonFlags flag = ButtonFlagsUtil.FromSingle(btn);
            if (flag == ButtonFlags.None) return false;

            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++) {
                if (Get(i).PressedButtons.HasFlag(flag)) return true;
            }
            return false;
        }

        /// <summary>
        /// Was a specific button released within the last `window` frames?
        /// Used for negative edge detection.
        /// </summary>
        public bool ButtonReleasedInWindow(ButtonInput btn, int window) {
            ButtonFlags flag = ButtonFlagsUtil.FromSingle(btn);
            if (flag == ButtonFlags.None) return false;

            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++) {
                if (Get(i).ReleasedButtons.HasFlag(flag)) return true;
            }
            return false;
        }

        /// <summary>
        /// Were two specific buttons BOTH pressed within the last `window` frames?
        /// Used for simultaneous button detection (throw = P+K, taunt = HS+D).
        /// Both buttons must have a press event within the window, but NOT necessarily
        /// on the same frame — this handles slight timing differences on hardware.
        /// </summary>
        public bool TwoButtonsPressedInWindow(ButtonInput btn1, ButtonInput btn2, int window) {
            ButtonFlags flag1 = ButtonFlagsUtil.FromSingle(btn1);
            ButtonFlags flag2 = ButtonFlagsUtil.FromSingle(btn2);
            if (flag1 == ButtonFlags.None || flag2 == ButtonFlags.None) return false;

            bool found1 = false;
            bool found2 = false;
            int limit = Mathf.Min(window, Count);

            for (int i = 0; i < limit; i++) {
                var f = Get(i);
                if (f.PressedButtons.HasFlag(flag1)) found1 = true;
                if (f.PressedButtons.HasFlag(flag2)) found2 = true;
                if (found1 && found2) return true;
            }
            return false;
        }

        /// <summary>
        /// Was a direction held continuously for at least `frames` frames,
        /// ending within the last `lookback` frames? Used for charge detection.
        /// </summary>
        public bool DirectionHeldFor(DirectionInput dir, int frames, int lookback) {
            int limit = Mathf.Min(lookback, Count);
            int consecutive = 0;

            for (int i = 0; i < limit; i++) {
                if (Get(i).Direction.HasFlag(dir)) {
                    consecutive++;
                    if (consecutive >= frames) return true;
                }
                else {
                    consecutive = 0;
                }
            }
            return false;
        }

        /// <summary>Reset the buffer (e.g. on round start).</summary>
        public void Clear() {
            Count = 0;
            _head = 0;
        }
    }
}