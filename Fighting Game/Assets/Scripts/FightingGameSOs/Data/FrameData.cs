using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Core frame data for a move. Every value is in game frames (1/60s).
    /// This is a plain serializable struct so it shows inline in the
    /// MoveData inspector — no separate asset needed.
    /// </summary>
    [Serializable]
    public struct FrameData {
        [Header("Phase Durations")]
        [Tooltip("Frames before the hitbox becomes active.")]
        [Min(1)] public int Startup;

        [Tooltip("Frames the hitbox is live.")]
        [Min(1)] public int Active;

        [Tooltip("Frames after hitbox deactivates before the character can act.")]
        [Min(0)] public int Recovery;

        // ---- Derived / convenience ----

        /// <summary>Total move duration.</summary>
        public int TotalFrames => Startup + Active + Recovery;

        /// <summary>First active frame (1-indexed, the way FGC notation works).</summary>
        public int FirstActiveFrame => Startup; // 0-indexed internally

        /// <summary>Last active frame.</summary>
        public int LastActiveFrame => Startup + Active - 1;

        [Header("Advantage")]
        [Tooltip("Frame advantage on hit (positive = attacker recovers first).")]
        public int HitAdvantage;

        [Tooltip("Frame advantage on block.")]
        public int BlockAdvantage;

        [Header("Hitstun / Blockstun (auto-derived if 0)")]
        [Tooltip("Override hitstun frames. If 0, calculated from HitAdvantage + Recovery.")]
        public int HitstunOverride;

        [Tooltip("Override blockstun frames. If 0, calculated from BlockAdvantage + Recovery.")]
        public int BlockstunOverride;

        /// <summary>
        /// Actual hitstun the opponent suffers.
        /// If no override, we derive from advantage:
        ///   hitstun = Recovery + HitAdvantage
        /// </summary>
        public int Hitstun => HitstunOverride > 0
            ? HitstunOverride
            : Mathf.Max(0, Recovery + HitAdvantage);

        public int Blockstun => BlockstunOverride > 0
            ? BlockstunOverride
            : Mathf.Max(0, Recovery + BlockAdvantage);
    }

    /// <summary>
    /// Damage and combo-scaling values for a single move.
    /// </summary>
    [Serializable]
    public struct DamageData {
        [Tooltip("Raw damage before scaling.")]
        [Min(0)] public int BaseDamage;

        [Tooltip("Chip damage on block (typically specials/supers only).")]
        [Min(0)] public int ChipDamage;

        [Tooltip("Stun meter damage (dizzy).")]
        [Min(0)] public int StunDamage;

        [Tooltip("Combo damage scaling multiplier (1.0 = no extra scaling).")]
        [Range(0f, 1f)] public float DamageScaling;

        [Tooltip("Super meter gained on hit.")]
        [Min(0)] public int MeterGainOnHit;

        [Tooltip("Super meter gained on whiff.")]
        [Min(0)] public int MeterGainOnWhiff;
    }

    /// <summary>
    /// Describes which moves this move can cancel into, and during
    /// which frame window the cancel is valid.
    /// </summary>
    [Serializable]
    public struct CancelData {
        [Tooltip("Highest cancel level this move allows.")]
        public CancelLevel MaxCancelLevel;

        [Tooltip("First frame cancelling is allowed (usually first active frame).")]
        public int CancelWindowStart;

        [Tooltip("Last frame cancelling is allowed.")]
        public int CancelWindowEnd;

        [Tooltip("Can this move be kara-cancelled (cancel startup into special)?")]
        public bool AllowKaraCancel;

        [Tooltip("If kara-cancel is allowed, how many startup frames are cancellable (typically 1-2).")]
        [Range(0, 3)] public int KaraCancelFrames;

        [Tooltip("Can cancel into super regardless of MaxCancelLevel.")]
        public bool AlwaysSuperCancellable;

        /// <summary>
        /// Returns true if the given frame (relative to move start) is
        /// inside the cancel window.
        /// </summary>
        public bool IsInCancelWindow(int moveFrame) {
            return moveFrame >= CancelWindowStart && moveFrame <= CancelWindowEnd;
        }

        public bool IsInKaraWindow(int moveFrame) {
            return AllowKaraCancel && moveFrame >= 0 && moveFrame < KaraCancelFrames;
        }
    }
}