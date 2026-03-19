using UnityEngine;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Reads the InputBuffer and attempts to match its contents against
    /// the MotionInput defined on MoveData assets.
    ///
    /// Supports:
    ///  - Sequence motions (QCF, DP, HCF, 360, double QCF, custom)
    ///  - Charge motions (hold back/down → release direction + button)
    ///  - Direction + button (command normals)
    ///  - Plain button press (normals)
    ///  - Negative edge (button release triggers specials)
    ///
    /// Not a MonoBehaviour — instantiated and owned by PlayerController.
    /// </summary>
    public class InputParser {
        private readonly InputBuffer _buffer;

        /// <summary>
        /// How many frames of leniency when walking a directional sequence.
        /// A step can be "missing" for up to this many frames and the
        /// sequence still matches. Handles sloppy stick movement.
        /// </summary>
        public int DirectionLeniency = 2;

        /// <summary>
        /// How many frames after a button press it still counts as
        /// "just pressed" for move activation. Effectively a small
        /// input buffer window on top of the motion window.
        /// </summary>
        public int ButtonPressWindow = 4;

        public InputParser(InputBuffer buffer) {
            _buffer = buffer;
        }

        // ──────────────────────────────────────
        //  PUBLIC API
        // ──────────────────────────────────────

        /// <summary>
        /// Attempts to match a complete move, delegating to the
        /// appropriate algorithm based on the move's MotionType.
        /// Returns true if the input was completed within the
        /// move's window ending on (or very near) the current frame.
        /// </summary>
        public bool TryMatchMove(MoveData move) {
            if (move == null) return false;

            MotionInput motion = move.Motion;

            switch (motion.Type) {
                case MotionType.None:
                    // Pure button press — normals
                    return MatchButton(motion.Button, motion.AllowNegativeEdge);

                case MotionType.DirectionPlusButton:
                    // Command normal: direction held + button press
                    return MatchDirectionPlusButton(move.RequiredDirection, motion.Button);

                case MotionType.ChargeBack:
                    return MatchCharge(
                        DirectionInput.Back, DirectionInput.Forward,
                        motion.ChargeFrames, motion.InputWindow,
                        motion.Button, motion.AllowNegativeEdge);

                case MotionType.ChargeDown:
                    return MatchCharge(
                        DirectionInput.Down, DirectionInput.Up,
                        motion.ChargeFrames, motion.InputWindow,
                        motion.Button, motion.AllowNegativeEdge);

                // All sequence-based motions (QCF, DP, HCF, 360, 2xQCF, custom)
                default:
                    return MatchSequence(motion);
            }
        }

        /// <summary>
        /// Attempts to match any move from an array, returning the first match.
        /// The array should be pre-sorted by InputPriority descending so that
        /// the most complex / highest-priority move wins.
        /// Returns null if nothing matched.
        /// </summary>
        public MoveData TryMatchFirst(MoveData[] moves) {
            if (moves == null) return null;

            foreach (var move in moves)
                if (TryMatchMove(move)) return move;

            return null;
        }

        // ──────────────────────────────────────
        //  PLAIN BUTTON
        // ──────────────────────────────────────

        /// <summary>
        /// Matches a standalone button press (standing/crouching/air normal).
        /// </summary>
        public bool MatchButton(ButtonInput btn, bool allowNegativeEdge = false) {
            if (btn == ButtonInput.None) return false;

            if (_buffer.ButtonPressedInWindow(btn, ButtonPressWindow))
                return true;

            if (allowNegativeEdge && _buffer.ButtonReleasedInWindow(btn, ButtonPressWindow))
                return true;

            return false;
        }

        // ──────────────────────────────────────
        //  DIRECTION + BUTTON (COMMAND NORMALS)
        // ──────────────────────────────────────

        /// <summary>
        /// Matches a command normal: the required direction must be held
        /// on the current frame AND the button was pressed recently.
        /// </summary>
        public bool MatchDirectionPlusButton(DirectionInput requiredDir, ButtonInput btn) {
            if (btn == ButtonInput.None) return false;

            InputFrame current = _buffer.Current;
            bool directionHeld = requiredDir == DirectionInput.None
                || current.Direction.HasFlag(requiredDir);

            return directionHeld && _buffer.ButtonPressedInWindow(btn, ButtonPressWindow);
        }

        // ──────────────────────────────────────
        //  SEQUENCE MOTIONS (QCF, DP, HCF, etc.)
        // ──────────────────────────────────────

        /// <summary>
        /// Walks backward through the buffer trying to find each step of
        /// the motion sequence in reverse order, ending with a button press.
        ///
        /// The algorithm is lenient: each directional step just needs to
        /// appear somewhere within the window, in the correct order.
        /// Small gaps (where the stick passes through neutral) are tolerated.
        /// </summary>
        private bool MatchSequence(MotionInput motion) {
            // Button must have been pressed (or released for negative edge) recently
            if (!HasButtonActivation(motion.Button, motion.AllowNegativeEdge))
                return false;

            NumpadDirection[] sequence = motion.GetSequence();
            if (sequence == null || sequence.Length == 0) return false;

            int window = motion.InputWindow > 0 ? motion.InputWindow : 20;
            int limit = Mathf.Min(window, _buffer.Count);

            // Walk the sequence backward (last step first, since we're
            // reading the buffer from most-recent to oldest)
            int seqIndex = sequence.Length - 1;
            int gapFrames = 0; // frames since last matched step

            for (int i = 0; i < limit; i++) {
                InputFrame frame = _buffer.Get(i);
                NumpadDirection frameDir = InputDetector.ToNumpad(frame.Direction);
                NumpadDirection target = sequence[seqIndex];

                if (DirectionMatches(frameDir, target)) {
                    seqIndex--;
                    gapFrames = 0;

                    if (seqIndex < 0)
                        return true; // full sequence matched
                }
                else {
                    gapFrames++;

                    // If the gap is too large between two steps, bail.
                    // This prevents matching stale inputs from seconds ago.
                    if (gapFrames > DirectionLeniency + 4) {
                        // Don't immediately fail — there might be an older
                        // valid sequence. But for performance we bail here.
                        break;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Lenient direction comparison. A frame's direction matches
        /// the target if it contains at least the same directional components.
        ///
        /// e.g. DownForward (3) matches a target of Down (2) — this handles
        /// the common case of players rolling through diagonals.
        /// But Down (2) does NOT match a target of DownForward (3) —
        /// the target requires the forward component.
        /// </summary>
        private bool DirectionMatches(NumpadDirection actual, NumpadDirection target) {
            if (actual == target) return true;

            // Convert both to flags and check containment
            DirectionInput actualFlags = InputDetector.FromNumpad(actual);
            DirectionInput targetFlags = InputDetector.FromNumpad(target);

            // Actual must contain ALL the flags of target
            return targetFlags != DirectionInput.None
                && (actualFlags & targetFlags) == targetFlags;
        }

        // ──────────────────────────────────────
        //  CHARGE MOTIONS
        // ──────────────────────────────────────

        /// <summary>
        /// Matches a charge move: the charge direction must have been held
        /// for at least `chargeFrames`, then the release direction + button
        /// appeared within the input window.
        ///
        /// e.g. [4]6+P:  hold Back for 40 frames, then Forward + Punch
        /// </summary>
        private bool MatchCharge(
            DirectionInput chargeDir, DirectionInput releaseDir,
            int chargeFrames, int inputWindow,
            ButtonInput btn, bool allowNegativeEdge) {
            // Button activation required
            if (!HasButtonActivation(btn, allowNegativeEdge))
                return false;

            // Release direction must be present on the current frame
            InputFrame current = _buffer.Current;
            if (!current.Direction.HasFlag(releaseDir))
                return false;

            // Check that the charge direction was held for enough frames
            // somewhere in the lookback window (before the release)
            int lookback = inputWindow > 0 ? inputWindow : 60;
            return _buffer.DirectionHeldFor(chargeDir, chargeFrames, lookback);
        }

        // ──────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────

        /// <summary>
        /// Checks whether the button was pressed (or released, if negative
        /// edge is enabled) within the standard button window.
        /// </summary>
        private bool HasButtonActivation(ButtonInput btn, bool allowNegativeEdge) {
            if (btn == ButtonInput.None) return false;

            if (_buffer.ButtonPressedInWindow(btn, ButtonPressWindow))
                return true;

            if (allowNegativeEdge && _buffer.ButtonReleasedInWindow(btn, ButtonPressWindow))
                return true;

            return false;
        }
    }
}