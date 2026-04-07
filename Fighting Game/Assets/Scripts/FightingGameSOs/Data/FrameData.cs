using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Core frame data for a move. Every value is in game frames (1/60s).
    ///
    /// STARTUP CONVENTION:
    ///   Startup does NOT include the first active frame.
    ///   A move with Startup 7 has 7 frames of wind-up, then the hitbox
    ///   first appears on frame 8 (0-indexed: frame 7).
    ///
    ///   Hitstun, blockstun, and hitstop are determined by the move's
    ///   AttackLevel (see AttackLevelData). Per-move overrides are still
    ///   available for specials/supers that deviate from the standard table.
    ///
    ///   Advantage is DERIVED, not authored:
    ///     advantage = defender's stun − (remainingActive + recovery)
    ///   where remainingActive = how many active frames are left AFTER the
    ///   frame that connected (0 if the hit landed on the last active frame).
    /// </summary>
    [Serializable]
    public struct FrameData {
        [Header("Phase Durations")]

        [Tooltip("Frames of wind-up before the hitbox becomes active.\n" +
                 "A move with Startup 7 has 7 frames of wind-up.\n" +
                 "The hitbox first appears on frame 8 (Startup + 1).")]
        [Min(1)] public int Startup;

        [Tooltip("Frames the hitbox is live.")]
        [Min(1)] public int Active;

        [Tooltip("Frames after hitbox deactivates before the character can act.")]
        [Min(0)] public int Recovery;

        // ---- Attack Level (GGXX) ----

        [Header("Attack Level")]
        [Tooltip("GGXX attack level (0–5). Determines hitstop, blockstop, hitstun, and blockstun.\n" +
                 "Level 0 = jabs, Level 5 = Dust/supers. See AttackLevelData for the lookup table.")]
        [Range(0, 5)] public int AttackLevel;

        // ---- Optional per-move overrides ----

        [Header("Overrides (leave 0 to use Attack Level defaults)")]

        [Tooltip("Override attacker hitstop. 0 = use AttackLevel default.")]
        [Min(0)] public int AttackerHitstopOverride;

        [Tooltip("Override defender hitstop on hit. 0 = use AttackLevel default.")]
        [Min(0)] public int DefenderHitstopOverride;

        [Tooltip("Override defender blockstop. 0 = use AttackLevel default.")]
        [Min(0)] public int DefenderBlockstopOverride;

        [Tooltip("Override hitstun frames. 0 = use AttackLevel default.")]
        [Min(0)] public int HitstunOverride;

        [Tooltip("Override blockstun frames. 0 = use AttackLevel default.")]
        [Min(0)] public int BlockstunOverride;

        // ---- Derived / convenience ----

        /// <summary>Total move duration in frames.</summary>
        public int TotalFrames => Startup + Active + Recovery;

        /// <summary>
        /// The 0-indexed frame on which the hitbox first appears.
        /// Startup 7 → hitbox first active on internal frame 7 (0-indexed).
        /// </summary>
        public int FirstActiveFrame => Startup;

        /// <summary>The 0-indexed last frame the hitbox is active.</summary>
        public int LastActiveFrame => Startup + Active - 1;

        // ---- Resolved properties (use these at runtime) ----

        /// <summary>Frames the ATTACKER freezes on hit or block.</summary>
        public int GetAttackerHitstop() {
            if (AttackerHitstopOverride > 0) return AttackerHitstopOverride;
            return AttackLevelData.Get(AttackLevel).AttackerHitstop;
        }

        /// <summary>Frames the DEFENDER freezes on hit.</summary>
        public int GetDefenderHitstop() {
            if (DefenderHitstopOverride > 0) return DefenderHitstopOverride;
            return AttackLevelData.Get(AttackLevel).DefenderHitstop;
        }

        /// <summary>Frames the DEFENDER freezes on block.</summary>
        public int GetDefenderBlockstop() {
            if (DefenderBlockstopOverride > 0) return DefenderBlockstopOverride;
            return AttackLevelData.Get(AttackLevel).DefenderBlockstop;
        }

        /// <summary>Hitstun the defender suffers AFTER hitstop ends.</summary>
        public int GetHitstun() {
            if (HitstunOverride > 0) return HitstunOverride;
            return AttackLevelData.Get(AttackLevel).Hitstun;
        }

        /// <summary>Blockstun the defender suffers AFTER blockstop ends.</summary>
        public int GetBlockstun() {
            if (BlockstunOverride > 0) return BlockstunOverride;
            return AttackLevelData.Get(AttackLevel).Blockstun;
        }
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