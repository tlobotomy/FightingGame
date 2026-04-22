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
        /// <summary>
        /// Numpad direction for this frame (5=neutral, 6=forward, 2=down, etc.).
        /// Already flipped for facing — 6 always means "toward opponent".
        /// </summary>
        public NumpadDirection Numpad;

        /// <summary>All buttons currently held down this frame (flags).</summary>
        public ButtonFlags HeldButtons;

        /// <summary>Buttons that were JUST pressed this frame (up→down transition).</summary>
        public ButtonFlags PressedButtons;

        /// <summary>Buttons that were JUST released this frame (down→up transition).</summary>
        public ButtonFlags ReleasedButtons;

        /// <summary>The game frame this was recorded on.</summary>
        public int Frame;

        // ── Convenience accessors ──

        /// <summary>DirectionInput flags derived from the numpad value.</summary>
        public DirectionInput Direction => InputDetector.FromNumpad(Numpad);

        /// <summary>Returns the first held button found (highest priority).</summary>
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

        /// <summary>True if ANY button was pressed this frame.</summary>
        public bool ButtonPressed => PressedButtons != ButtonFlags.None;

        /// <summary>True if ANY button was released this frame.</summary>
        public bool ButtonReleased => ReleasedButtons != ButtonFlags.None;
    }

    /// <summary>
    /// Fixed-size circular buffer storing the last N frames of input.
    ///
    /// Based on the Fearless Night pattern: a flat ring buffer indexed
    /// by a head pointer. Walk backward from head to match sequences.
    ///
    /// Buffer size of 40 frames (~0.67s at 60fps) is sufficient for
    /// even the longest motion inputs (double QCF supers, 360s) while
    /// being tight enough to prevent ancient inputs from matching.
    ///
    /// Not a MonoBehaviour — owned and ticked by PlayerController.
    /// </summary>
    public class InputBuffer {
        private readonly InputFrame[] _frames;
        private int _head;

        /// <summary>How many frames have been recorded (capped at capacity).</summary>
        public int Count { get; private set; }

        /// <summary>Ring buffer capacity.</summary>
        public int Capacity => _frames.Length;

        /// <summary>
        /// Creates a new input buffer.
        /// 40 frames covers: double-QCF super (needs ~20f), 360 motion (~16f),
        /// charge moves (checked separately via ChargeFrames), and generous
        /// input leniency — without being so large that stale inputs ghost-match.
        /// </summary>
        public InputBuffer(int capacity = 40) {
            _frames = new InputFrame[capacity];
        }

        /// <summary>Push a new frame onto the buffer (called once per game tick).</summary>
        public void Push(InputFrame frame) {
            _frames[_head] = frame;
            _head = (_head + 1) % _frames.Length;
            Count = Mathf.Min(Count + 1, _frames.Length);
        }

        /// <summary>
        /// Read a frame from the buffer.
        /// framesAgo = 0 is the most recent, 1 is one frame before that, etc.
        /// </summary>
        public InputFrame Get(int framesAgo) {
            if (framesAgo >= Count) return default;
            int index = ((_head - 1 - framesAgo) % _frames.Length + _frames.Length) % _frames.Length;
            return _frames[index];
        }

        /// <summary>Returns the most recent frame.</summary>
        public InputFrame Current => Count > 0 ? Get(0) : default;

        // ──────────────────────────────────────
        //  DIRECTION QUERIES
        // ──────────────────────────────────────

        /// <summary>
        /// Walks backward from the most recent frame and checks if
        /// the given directional sequence was completed within maxDuration frames.
        ///
        /// This is the core matching algorithm (Fearless Night pattern):
        /// start at the sequence's last element, scan backward, decrement
        /// the sequence index each time a match is found. If we reach
        /// index -1, the full sequence was present.
        ///
        /// Numpad matching is lenient: a diagonal (3) satisfies both
        /// Down (2) and Forward (6) targets, because real stick movement
        /// passes through diagonals.
        /// </summary>
        public bool CheckSequence(NumpadDirection[] sequence, int maxDuration) {
            if (sequence == null || sequence.Length == 0) return true;

            int w = sequence.Length - 1;
            int limit = Mathf.Min(maxDuration, Count);

            for (int i = 0; i < limit; i++) {
                NumpadDirection dir = Get(i).Numpad;

                if (NumpadMatches(dir, sequence[w])) {
                    w--;
                    if (w < 0) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Lenient numpad comparison. A frame's direction matches the
        /// target if it contains at least the same directional components.
        ///
        /// e.g. DownForward (3) matches a target of Down (2) — handles
        /// the common case of rolling through diagonals during QCF.
        /// But Down (2) does NOT match DownForward (3) — the target
        /// requires the forward component.
        /// </summary>
        public static bool NumpadMatches(NumpadDirection actual, NumpadDirection target) {
            if (actual == target) return true;

            DirectionInput actualFlags = InputDetector.FromNumpad(actual);
            DirectionInput targetFlags = InputDetector.FromNumpad(target);

            // Actual must contain ALL the directional flags of target
            return targetFlags != DirectionInput.None
                && (actualFlags & targetFlags) == targetFlags;
        }

        /// <summary>
        /// Was a specific direction held at any point within the last `window` frames?
        /// </summary>
        public bool DirectionInWindow(DirectionInput dir, int window) {
            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++) {
                DirectionInput frameDir = InputDetector.FromNumpad(Get(i).Numpad);
                if ((frameDir & dir) == dir) return true;
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
                DirectionInput frameDir = InputDetector.FromNumpad(Get(i).Numpad);
                if ((frameDir & dir) == dir) {
                    consecutive++;
                    if (consecutive >= frames) return true;
                }
                else {
                    consecutive = 0;
                }
            }
            return false;
        }

        // ──────────────────────────────────────
        //  BUTTON QUERIES
        // ──────────────────────────────────────

        /// <summary>
        /// Was a specific button pressed (initial up→down transition)
        /// within the last `window` frames?
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

        /// <summary>Reset the buffer (e.g. on round start).</summary>
        public void Clear() {
            Count = 0;
            _head = 0;
        }
    }
}