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

        // ---- Attack Level (GGACR) ----

        [Header("Attack Level")]
        [Tooltip("GGACR attack level (1–5). Determines hitstop, blockstop, hitstun, blockstun, " +
                 "untechable time, and guard balance.\n" +
                 "Level 1 = light normals, Level 5 = Dust/supers. See AttackLevelData.")]
        [Range(1, 5)] public int AttackLevel;

        // ---- Optional per-move overrides ----
        // Specials and supers often deviate from the attack level table.
        // Set any value > 0 to override the level default.

        [Header("Overrides (leave 0 to use Attack Level defaults)")]

        [Tooltip("When true, HitstopOverride is used even if it's 0 (for moves with genuinely zero hitstop like Sol f.S).")]
        public bool UseHitstopOverride;

        [Tooltip("Override hitstop (same for attacker and defender on normal hit). Only used if UseHitstopOverride is true OR value > 0.")]
        [Min(0)] public int HitstopOverride;

        [Tooltip("When true, BlockstopOverride is used even if it's 0.")]
        public bool UseBlockstopOverride;

        [Tooltip("Override blockstop. Only used if UseBlockstopOverride is true OR value > 0.")]
        [Min(0)] public int BlockstopOverride;

        [Tooltip("Override standing hitstun. 0 = use level default.")]
        [Min(0)] public int HitstunOverride;

        [Tooltip("Override ground blockstun. 0 = use level default.")]
        [Min(0)] public int BlockstunOverride;

        [Tooltip("Override untechable time (air hit). 0 = use level default.")]
        [Min(0)] public int UntechableTimeOverride;

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

        /// <summary>
        /// Base hitstop frames (same for attacker and defender on normal hit).
        /// In GGACR, hitstop is symmetric. Counter hit adds extra to defender only.
        /// </summary>
        public int GetHitstop() {
            if (UseHitstopOverride || HitstopOverride > 0) return HitstopOverride;
            return AttackLevelData.Get(AttackLevel).Hitstop;
        }

        /// <summary>Blockstop frames (freeze on block, same for both players).</summary>
        public int GetBlockstop() {
            if (UseBlockstopOverride || BlockstopOverride > 0) return BlockstopOverride;
            return AttackLevelData.Get(AttackLevel).Blockstop;
        }

        /// <summary>Standing hitstun (after hitstop ends).</summary>
        public int GetStandingHitstun() {
            if (HitstunOverride > 0) return HitstunOverride;
            return AttackLevelData.Get(AttackLevel).StandingHitstun;
        }

        /// <summary>Crouching hitstun = standing + crouching bonus.</summary>
        public int GetCrouchingHitstun() {
            if (HitstunOverride > 0) return HitstunOverride; // override replaces everything
            var props = AttackLevelData.Get(AttackLevel);
            return props.StandingHitstun + props.CrouchingHitstunBonus;
        }

        /// <summary>Ground blockstun (after blockstop ends).</summary>
        public int GetBlockstun() {
            if (BlockstunOverride > 0) return BlockstunOverride;
            return AttackLevelData.Get(AttackLevel).Blockstun;
        }

        /// <summary>Ground blockstun during Faultless Defense.</summary>
        public int GetFDBlockstun() {
            int baseStun = GetBlockstun();
            return baseStun + AttackLevelData.Get(AttackLevel).FDBlockstunMod;
        }

        /// <summary>Ground blockstun during Instant Block.</summary>
        public int GetIBBlockstun() {
            int baseStun = GetBlockstun();
            return Mathf.Max(1, baseStun + AttackLevelData.Get(AttackLevel).IBBlockstunMod);
        }

        /// <summary>Air blockstun (normal air block).</summary>
        public int GetAirBlockstun() {
            return AttackLevelData.Get(AttackLevel).AirBlockstun;
        }

        /// <summary>Air blockstun during Faultless Defense.</summary>
        public int GetAirFDBlockstun() {
            var props = AttackLevelData.Get(AttackLevel);
            return props.AirBlockstun + props.AirFDBlockstunMod;
        }

        /// <summary>Air blockstun during Instant Block.</summary>
        public int GetAirIBBlockstun() {
            var props = AttackLevelData.Get(AttackLevel);
            return Mathf.Max(1, props.AirBlockstun + props.AirIBBlockstunMod);
        }

        /// <summary>
        /// Untechable time on air hit. Doubled on counter hit per GGACR rules.
        /// Pass counterHit = true for the doubled value.
        /// </summary>
        public int GetUntechableTime(bool counterHit = false) {
            int baseTime;
            if (UntechableTimeOverride > 0)
                baseTime = UntechableTimeOverride;
            else
                baseTime = AttackLevelData.Get(AttackLevel).UntechableTime;

            return counterHit ? baseTime * 2 : baseTime;
        }

        /// <summary>Guard balance damage on block (depends on attack height).</summary>
        public int GetGuardBalanceDamage(AttackHeight height) {
            var props = AttackLevelData.Get(AttackLevel);
            if (height == AttackHeight.Mid)
                return props.GuardBalanceMid;
            return props.GuardBalanceHighLow;
        }

        // ---- Legacy convenience wrappers ----
        // These map old call sites to the new GGACR-accurate methods.

        /// <summary>Attacker hitstop = base hitstop (symmetric in GGACR).</summary>
        public int GetAttackerHitstop() => GetHitstop();

        /// <summary>Defender hitstop on hit = base hitstop (CH bonus added separately by MatchManager).</summary>
        public int GetDefenderHitstop() => GetHitstop();

        /// <summary>Defender blockstop = blockstop.</summary>
        public int GetDefenderBlockstop() => GetBlockstop();

        /// <summary>Default hitstun (standing). Use GetStandingHitstun/GetCrouchingHitstun for precision.</summary>
        public int GetHitstun() => GetStandingHitstun();
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

        [Tooltip("Per-hit combo damage scaling multiplier (1.0 = no extra scaling per hit).")]
        [Range(0f, 1f)] public float DamageScaling;

        [Tooltip("INITIAL (forced) combo proration. When this move STARTS a combo, " +
                 "all subsequent hits are scaled to this percentage.\n\n" +
                 "1.0 = no forced proration (most normals).\n" +
                 "0.8 = 80% forced proration (e.g. 2P).\n" +
                 "0.6 = 60% forced proration (e.g. f.S launcher).\n\n" +
                 "In GGACR, forced proration only applies when the move is the " +
                 "FIRST hit of the combo. It does NOT stack with per-hit scaling — " +
                 "it SETS the starting scale floor.")]
        [Range(0f, 1f)] public float InitialProration;

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

        [Tooltip("Can this move be jump-cancelled on hit? (GGXX: common for normals.)")]
        public bool AllowJumpCancel;

        [Tooltip("Can this move be jump-cancelled on block? (Less common.)")]
        public bool AllowJumpCancelOnBlock;

        [Tooltip("Can cancel into super regardless of MaxCancelLevel.\n" +
                 "Common for normals that should be super-cancellable but not special-cancellable.")]
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