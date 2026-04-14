using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Defines the standardized properties for each Attack Level (1–5).
    /// Sourced directly from GGXX Accent Core +R system data.
    ///
    /// Every normal and most specials have an attack level that determines
    /// hitstop, hitstun, blockstun, untechable time, and guard balance.
    /// Specials/supers can override any value via FrameData overrides.
    ///
    /// KEY GGACR RULES:
    ///   - Hitstop is the same for attacker and defender on normal hit.
    ///   - Counter hit adds extra hitstop to the DEFENDER only (not attacker).
    ///     CH hitstop is halved if the move's normal hitstop is lower than CH bonus.
    ///     CH hitstop is absent (zero) if normal hitstop is zero.
    ///   - Untechable time is DOUBLED on counter hit.
    ///   - Crouching hitstun = standing hitstun + CrouchingHitstunBonus.
    ///   - FD blockstun = normal blockstun + FDBlockstunMod (always +2).
    ///   - IB blockstun = normal blockstun + IBBlockstunMod (negative = less).
    ///   - Air blockstun, Air FD, Air IB are separate values.
    ///   - Blockstop (Blockstop/Hitstop row) is the freeze on block.
    ///   - GB+ = guard balance damage on mid / on high-or-low.
    ///   - GB- = guard balance recovery per frame.
    ///
    /// This is a static lookup table — not a ScriptableObject — because
    /// these values are universal game constants, not per-character data.
    /// </summary>
    public static class AttackLevelData {
        /// <summary>
        /// Properties granted by a single attack level.
        /// All frame values sourced from GGACR system tables.
        /// </summary>
        [Serializable]
        public struct LevelProperties {
            // ── HITSTOP ──

            [Tooltip("Frames both players freeze on hit (same for attacker and defender).")]
            public int Hitstop;

            [Tooltip("Additional hitstop added to DEFENDER on counter hit.")]
            public int CounterHitHitstopBonus;

            // ── BLOCKSTOP ──

            [Tooltip("Frames both players freeze on block.")]
            public int Blockstop;

            // ── HITSTUN ──

            [Tooltip("Standing hitstun frames (after hitstop ends).")]
            public int StandingHitstun;

            [Tooltip("Additional hitstun when the defender is crouching (added to standing).")]
            public int CrouchingHitstunBonus;

            // ── UNTECHABLE TIME ──

            [Tooltip("Untechable time on air hit (frames before the defender can air tech). Doubled on CH.")]
            public int UntechableTime;

            // ── BLOCKSTUN (GROUND) ──

            [Tooltip("Normal ground blockstun (after blockstop ends).")]
            public int Blockstun;

            [Tooltip("Modifier to blockstun during Faultless Defense (typically +2).")]
            public int FDBlockstunMod;

            [Tooltip("Modifier to blockstun during Instant Block (typically negative = less stun).")]
            public int IBBlockstunMod;

            // ── BLOCKSTUN (AIR) ──

            [Tooltip("Normal air blockstun.")]
            public int AirBlockstun;

            [Tooltip("Air FD blockstun modifier.")]
            public int AirFDBlockstunMod;

            [Tooltip("Air IB blockstun modifier.")]
            public int AirIBBlockstunMod;

            // ── GUARD BALANCE ──

            [Tooltip("Guard balance damage on block (mid attacks).")]
            public int GuardBalanceMid;

            [Tooltip("Guard balance damage on block (high or low attacks).")]
            public int GuardBalanceHighLow;

            [Tooltip("Guard balance recovery per frame when not blocking.")]
            public int GuardBalanceRecovery;
        }

        /// <summary>
        /// The lookup table. Index 0 = Level 1, Index 4 = Level 5.
        /// GGACR uses levels 1–5 (no level 0).
        ///
        /// Level 1: Light normals (5P, 5K, 2P, 2K)
        /// Level 2: Medium normals (c.S, f.S, 2S)
        /// Level 3: Heavy normals (5H, 2H)
        /// Level 4: Command normals, heavy slashes (6P, 6H, 6K)
        /// Level 5: Dust, certain specials, Overdrives
        /// </summary>
        private static readonly LevelProperties[] _levels = new LevelProperties[]
        {
            // ── Level 1 ──
            new LevelProperties
            {
                Hitstop                 = 11,
                CounterHitHitstopBonus  = 0,
                Blockstop               = 11,

                StandingHitstun         = 10,
                CrouchingHitstunBonus   = 0,
                UntechableTime          = 10,

                Blockstun               = 9,
                FDBlockstunMod          = +2,
                IBBlockstunMod          = -2,

                AirBlockstun            = 9,
                AirFDBlockstunMod       = +2,
                AirIBBlockstunMod       = -6,

                GuardBalanceMid         = 3,
                GuardBalanceHighLow     = 3,
                GuardBalanceRecovery    = 8,
            },
            // ── Level 2 ──
            new LevelProperties
            {
                Hitstop                 = 12,
                CounterHitHitstopBonus  = +2,
                Blockstop               = 12,

                StandingHitstun         = 12,
                CrouchingHitstunBonus   = +1,
                UntechableTime          = 12,

                Blockstun               = 11,
                FDBlockstunMod          = +2,
                IBBlockstunMod          = -3,

                AirBlockstun            = 11,
                AirFDBlockstunMod       = +3,
                AirIBBlockstunMod       = -6,

                GuardBalanceMid         = 6,
                GuardBalanceHighLow     = 5,
                GuardBalanceRecovery    = 7,
            },
            // ── Level 3 ──
            new LevelProperties
            {
                Hitstop                 = 13,
                CounterHitHitstopBonus  = +4,
                Blockstop               = 13,

                StandingHitstun         = 14,
                CrouchingHitstunBonus   = +1,
                UntechableTime          = 14,

                Blockstun               = 13,
                FDBlockstunMod          = +2,
                IBBlockstunMod          = -3,

                AirBlockstun            = 13,
                AirFDBlockstunMod       = +4,
                AirIBBlockstunMod       = -6,

                GuardBalanceMid         = 10,
                GuardBalanceHighLow     = 8,
                GuardBalanceRecovery    = 7,
            },
            // ── Level 4 ──
            new LevelProperties
            {
                Hitstop                 = 14,
                CounterHitHitstopBonus  = +8,
                Blockstop               = 14,

                StandingHitstun         = 17,
                CrouchingHitstunBonus   = +1,
                UntechableTime          = 16,

                Blockstun               = 16,
                FDBlockstunMod          = +2,
                IBBlockstunMod          = -4,

                AirBlockstun            = 16,
                AirFDBlockstunMod       = +4,
                AirIBBlockstunMod       = -7,

                GuardBalanceMid         = 14,
                GuardBalanceHighLow     = 11,
                GuardBalanceRecovery    = 6,
            },
            // ── Level 5 ──
            new LevelProperties
            {
                Hitstop                 = 15,
                CounterHitHitstopBonus  = +12,
                Blockstop               = 15,

                StandingHitstun         = 19,
                CrouchingHitstunBonus   = +1,
                UntechableTime          = 18,

                Blockstun               = 18,
                FDBlockstunMod          = +2,
                IBBlockstunMod          = -4,

                AirBlockstun            = 19,
                AirFDBlockstunMod       = +4,
                AirIBBlockstunMod       = -8,

                GuardBalanceMid         = 20,
                GuardBalanceHighLow     = 15,
                GuardBalanceRecovery    = 6,
            },
        };

        /// <summary>
        /// Returns the properties for a given attack level (clamped to 1–5).
        /// GGACR uses levels 1–5. Passing 0 returns level 1.
        /// </summary>
        public static LevelProperties Get(int level) {
            int index = Mathf.Clamp(level - 1, 0, _levels.Length - 1);
            return _levels[index];
        }

        /// <summary>Total number of defined levels (5).</summary>
        public static int Count => _levels.Length;

        /// <summary>Minimum valid attack level.</summary>
        public static int MinLevel => 1;

        /// <summary>Maximum valid attack level.</summary>
        public static int MaxLevel => _levels.Length;

        /// <summary>
        /// Calculates frame advantage given the attack level and the
        /// attacker's remaining frames after the hit connects.
        ///
        /// GGACR advantage formula:
        ///   On hit:   advantage = hitstun - (remainingActive + recovery)
        ///   On block: advantage = blockstun - (remainingActive + recovery)
        ///
        /// Positive = attacker recovers first. Negative = defender recovers first.
        /// </summary>
        public static int CalculateAdvantage(int level, int remainingActiveFrames,
            int recoveryFrames, bool blocked) {
            var props = Get(level);
            int defenderStun = blocked ? props.Blockstun : props.StandingHitstun;
            int attackerRemaining = remainingActiveFrames + recoveryFrames;
            return defenderStun - attackerRemaining;
        }

        /// <summary>
        /// Returns the counter hit hitstop bonus for the defender.
        /// Follows GGACR rules: halved if move's normal hitstop is lower
        /// than the bonus, absent if hitstop is zero.
        /// </summary>
        public static int GetCounterHitHitstop(int level, int normalHitstop) {
            if (normalHitstop <= 0) return 0;

            var props = Get(level);
            int bonus = props.CounterHitHitstopBonus;

            // CH hitstop is halved if normal hitstop < bonus
            if (normalHitstop < bonus)
                bonus = bonus / 2;

            return bonus;
        }
    }
}