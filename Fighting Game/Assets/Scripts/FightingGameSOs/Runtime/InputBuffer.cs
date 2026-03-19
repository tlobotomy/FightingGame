using System;
using UnityEngine;
using FightingGame.Data;

namespace FightingGame.Runtime {
    /// <summary>
    /// Raw snapshot of input state on a single game frame.
    /// Created by InputDetector, stored by InputBuffer, read by InputParser.
    /// </summary>
    [Serializable]
    public struct InputFrame {
        public DirectionInput Direction;
        public ButtonInput Button;

        /// <summary>True only on the frame the button transitions from up to down.</summary>
        public bool ButtonPressed;

        /// <summary>True only on the frame the button transitions from down to up (negative edge).</summary>
        public bool ButtonReleased;

        /// <summary>The game frame this was recorded on.</summary>
        public int Frame;
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

        /// <summary>
        /// Returns the most recent frame.
        /// </summary>
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
        /// within the last `window` frames?
        /// </summary>
        public bool ButtonPressedInWindow(ButtonInput btn, int window) {
            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++) {
                var f = Get(i);
                if (f.Button == btn && f.ButtonPressed) return true;
            }
            return false;
        }

        /// <summary>
        /// Was a specific button released within the last `window` frames?
        /// Used for negative edge detection.
        /// </summary>
        public bool ButtonReleasedInWindow(ButtonInput btn, int window) {
            int limit = Mathf.Min(window, Count);
            for (int i = 0; i < limit; i++) {
                var f = Get(i);
                if (f.Button == btn && f.ButtonReleased) return true;
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