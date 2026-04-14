using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Defines the input required to perform a move.
    /// Supports both classic motions (QCF, QCB, DP, charge) and
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
                    return Array.Empty<NumpadDirection>();

                case MotionType.QuarterCircleForward:
                    // 236: Down → DownForward → Forward
                    return new[] {
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward
                    };

                case MotionType.QuarterCircleBack:
                    // 214: Down → DownBack → Back
                    return new[] {
                        NumpadDirection.Down,
                        NumpadDirection.DownBack,
                        NumpadDirection.Back
                    };

                case MotionType.DragonPunch:
                    // 623: Forward → Down → DownForward
                    return new[] {
                        NumpadDirection.Forward,
                        NumpadDirection.Down,
                        NumpadDirection.DownForward
                    };

                case MotionType.HalfCircleForward:
                    // 41236: Back → DownBack → Down → DownForward → Forward
                    return new[] {
                        NumpadDirection.Back,
                        NumpadDirection.DownBack,
                        NumpadDirection.Down,
                        NumpadDirection.DownForward,
                        NumpadDirection.Forward
                    };

                case MotionType.HalfCircleBack:
                    // 63214: Forward → DownForward → Down → DownBack → Back
                    return new[] {
                        NumpadDirection.Forward,
                        NumpadDirection.DownForward,
                        NumpadDirection.Down,
                        NumpadDirection.DownBack,
                        NumpadDirection.Back
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
                    // 236236: super motion
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
                    return Array.Empty<NumpadDirection>();

                case MotionType.Custom:
                    return CustomSequence ?? Array.Empty<NumpadDirection>();

                default:
                    return Array.Empty<NumpadDirection>();
            }
        }
    }
}