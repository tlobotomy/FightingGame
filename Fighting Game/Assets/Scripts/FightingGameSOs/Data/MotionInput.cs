using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Defines the input required to perform a move.
    /// Supports both classic motions (QCF, DP, charge) and
    /// arbitrary custom sequences.
    /// </summary>
    [Serializable]
    public struct MotionInput {
        [Tooltip("The type of motion — determines which parser algorithm is used.")]
        public MotionType Type;

        [Tooltip("Which button completes the input.")]
        public ButtonInput Button;

        [Header("Motion Sequence (for Custom type)")]
        [Tooltip("Ordered directional steps if Type == Custom. " +
                 "For standard types (QCF, DP, etc.) this is auto-generated at runtime.")]
        public NumpadDirection[] CustomSequence;

        [Header("Charge Settings")]
        [Tooltip("Minimum frames the charge direction must be held (typically 40-48 in 3S).")]
        [Min(0)] public int ChargeFrames;

        [Header("Timing")]
        [Tooltip("Max frames allowed to complete the full motion (generous = easier execution).")]
        [Min(1)] public int InputWindow;

        [Tooltip("Allow negative edge (button release triggers the move).")]
        public bool AllowNegativeEdge;

        /// <summary>
        /// Returns the canonical directional sequence for this motion type.
        /// Called once at load time and cached by the parser.
        /// </summary>
        public NumpadDirection[] GetSequence() {
            switch (Type) {
                case MotionType.None:
                    return Array.Empty<NumpadDirection>();

                case MotionType.DirectionPlusButton:
                    // Caller should check the required direction on MoveData
                    return Array.Empty<NumpadDirection>();

                case MotionType.QuarterCircle:
                    // Covers both QCF (236) and QCB (214) —
                    // we always store as "forward" and the detector
                    // already flips based on facing.
                    return new[] {
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward
                    };

                case MotionType.DragonPunch:
                    return new[] {
                        NumpadDirection.Forward,
                        NumpadDirection.Down,
                        NumpadDirection.DownForward
                    };

                case MotionType.HalfCircle:
                    return new[] {
                        NumpadDirection.Back,
                        NumpadDirection.DownBack,
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward
                    };

                case MotionType.FullCircle:
                    return new[] {
                        NumpadDirection.Forward,
                        NumpadDirection.DownForward,
                        NumpadDirection.Down,
                        NumpadDirection.DownBack,
                        NumpadDirection.Back,
                        NumpadDirection.UpBack,
                        NumpadDirection.Up,
                        NumpadDirection.UpForward
                    };

                case MotionType.DoubleQuarterCircle:
                    return new[] {
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward,
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward
                    };

                case MotionType.ChargeBack:
                case MotionType.ChargeDown:
                    // Charge moves are matched differently (hold check + release),
                    // sequence isn't walked the same way.
                    return Array.Empty<NumpadDirection>();

                case MotionType.Custom:
                    return CustomSequence ?? Array.Empty<NumpadDirection>();

                default:
                    return Array.Empty<NumpadDirection>();
            }
        }
    }
}