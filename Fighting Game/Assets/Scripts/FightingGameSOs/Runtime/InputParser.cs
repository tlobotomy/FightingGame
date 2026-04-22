using UnityEngine;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Reads the InputBuffer and matches its contents against the
    /// MotionInput defined on MoveData assets.
    ///
    /// Sequence matching uses the Fearless Night walk-backward pattern:
    /// starting from the most recent buffer entry, walk backward and
    /// match each directional step in reverse order. If all steps are
    /// found within the input window, the motion is complete.
    ///
    /// All directions in the buffer are pre-flipped by InputDetector
    /// based on FacingSign, so the parser always thinks in terms of
    /// "forward" (6) and "back" (4), never raw left/right. Side
    /// switches mid-match are handled automatically.
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
        /// How many frames after a button press it still counts as
        /// "just pressed" for move activation. Effectively a small
        /// input buffer window on top of the motion window.
        /// </summary>
        public int ButtonPressWindow = 5;

        /// <summary>
        /// Default input window for sequence motions when the move
        /// doesn't specify one. 16 frames is ~267ms at 60fps —
        /// tight enough to prevent accidental inputs but lenient
        /// enough for comfortable execution.
        /// </summary>
        public int DefaultMotionWindow = 16;

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
        /// Public so PlayerController can call it for normal resolution.
        /// </summary>
        public bool MatchButton(ButtonInput btn, bool allowNegativeEdge = false) {
            if (btn == ButtonInput.None) return false;

            if (_buffer.ButtonPressedInWindow(btn, ButtonPressWindow))
                return true;

            if (allowNegativeEdge && _buffer.ButtonReleasedInWindow(btn, ButtonPressWindow))
                return true;

            return false;
        }

        /// <summary>
        /// Checks if two buttons were both pressed within the given window.
        /// Used for simultaneous button inputs (throw = P+K, taunt = HS+D).
        /// </summary>
        public bool MatchTwoButtons(ButtonInput btn1, ButtonInput btn2, int window) {
            return _buffer.TwoButtonsPressedInWindow(btn1, btn2, window);
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
            DirectionInput currentDir = current.Direction;
            bool directionHeld = requiredDir == DirectionInput.None
                || (currentDir & requiredDir) == requiredDir;

            return directionHeld && _buffer.ButtonPressedInWindow(btn, ButtonPressWindow);
        }

        // ──────────────────────────────────────
        //  SEQUENCE MOTIONS (QCF, DP, HCF, etc.)
        // ──────────────────────────────────────

        /// <summary>
        /// Matches a directional sequence using the walk-backward algorithm.
        ///
        /// Gets the canonical numpad sequence from the motion type (e.g.
        /// QCF = [2, 3, 6]), then delegates to InputBuffer.CheckSequence
        /// which walks backward from the current frame matching each step.
        ///
        /// The button must have been pressed (or released for negative edge)
        /// within ButtonPressWindow frames.
        /// </summary>
        private bool MatchSequence(MotionInput motion) {
            // Button must have been pressed (or released) recently
            if (!HasButtonActivation(motion.Button, motion.AllowNegativeEdge))
                return false;

            NumpadDirection[] sequence = motion.GetSequence();
            if (sequence == null || sequence.Length == 0) return false;

            int window = motion.InputWindow > 0 ? motion.InputWindow : DefaultMotionWindow;

            return _buffer.CheckSequence(sequence, window);
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
            DirectionInput currentDir = current.Direction;
            if ((currentDir & releaseDir) != releaseDir)
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