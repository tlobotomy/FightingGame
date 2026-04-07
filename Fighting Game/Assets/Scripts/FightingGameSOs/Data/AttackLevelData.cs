using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// Defines the standardized properties for each Attack Level (0–5).
    /// In Guilty Gear XX, every normal and most specials have an attack level
    /// that determines hitstop, blockstop, hitstun, and blockstun.
    ///
    /// This is a static lookup table — not a ScriptableObject — because
    /// these values are universal game constants, not per-character data.
    ///
    /// The values below approximate GGXX's system. Tune them to taste.
    /// </summary>
    public static class AttackLevelData {
        /// <summary>
        /// Properties granted by a single attack level.
        /// </summary>
        [Serializable]
        public struct LevelProperties {
            [Tooltip("Frames the ATTACKER freezes on hit or block.")]
            public int AttackerHitstop;

            [Tooltip("Frames the DEFENDER freezes on hit.")]
            public int DefenderHitstop;

            [Tooltip("Frames the DEFENDER freezes on block.")]
            public int DefenderBlockstop;

            [Tooltip("Frames of hitstun the defender suffers AFTER hitstop ends.")]
            public int Hitstun;

            [Tooltip("Frames of blockstun the defender suffers AFTER blockstop ends.")]
            public int Blockstun;

            [Tooltip("Base damage multiplier for this level (optional, for scaling).")]
            public float DamageMultiplier;
        }

        /// <summary>
        /// The lookup table. Index = attack level (0–5).
        ///
        /// Level 0: Jabs, light pokes
        /// Level 1: Standing/crouching light normals
        /// Level 2: Medium normals
        /// Level 3: Heavy normals, close slashes
        /// Level 4: Command normals, heavy slashes
        /// Level 5: Dust, certain specials, overdrives
        /// </summary>
        private static readonly LevelProperties[] _levels = new LevelProperties[]
        {
            // Level 0
            new LevelProperties
            {
                AttackerHitstop  = 8,
                DefenderHitstop  = 8,
                DefenderBlockstop = 8,
                Hitstun          = 10,
                Blockstun        = 9,
                DamageMultiplier = 0.5f
            },
            // Level 1
            new LevelProperties
            {
                AttackerHitstop  = 10,
                DefenderHitstop  = 10,
                DefenderBlockstop = 10,
                Hitstun          = 12,
                Blockstun        = 11,
                DamageMultiplier = 0.7f
            },
            // Level 2
            new LevelProperties
            {
                AttackerHitstop  = 11,
                DefenderHitstop  = 11,
                DefenderBlockstop = 11,
                Hitstun          = 14,
                Blockstun        = 13,
                DamageMultiplier = 0.85f
            },
            // Level 3
            new LevelProperties
            {
                AttackerHitstop  = 12,
                DefenderHitstop  = 12,
                DefenderBlockstop = 12,
                Hitstun          = 16,
                Blockstun        = 15,
                DamageMultiplier = 1.0f
            },
            // Level 4
            new LevelProperties
            {
                AttackerHitstop  = 13,
                DefenderHitstop  = 13,
                DefenderBlockstop = 13,
                Hitstun          = 18,
                Blockstun        = 18,
                DamageMultiplier = 1.1f
            },
            // Level 5
            new LevelProperties
            {
                AttackerHitstop  = 14,
                DefenderHitstop  = 14,
                DefenderBlockstop = 14,
                Hitstun          = 20,
                Blockstun        = 20,
                DamageMultiplier = 1.25f
            },
        };

        /// <summary>
        /// Returns the properties for a given attack level (clamped to 0–5).
        /// </summary>
        public static LevelProperties Get(int level) {
            level = Mathf.Clamp(level, 0, _levels.Length - 1);
            return _levels[level];
        }

        /// <summary>
        /// Total number of defined levels.
        /// </summary>
        public static int Count => _levels.Length;

        /// <summary>
        /// Calculates frame advantage given the attack level and the
        /// attacker's remaining frames after the hit connects.
        ///
        /// remainingActiveFrames: how many active frames are left AFTER
        ///   the frame that connected (0 if it hit on the last active frame).
        /// recoveryFrames: the move's total recovery frames.
        ///
        /// GGXX advantage formula:
        ///   On hit:   advantage = hitstun - (remainingActive + recovery)
        ///   On block: advantage = blockstun - (remainingActive + recovery)
        ///
        /// Positive = attacker recovers first. Negative = defender recovers first.
        /// </summary>
        public static int CalculateAdvantage(int level, int remainingActiveFrames,
            int recoveryFrames, bool blocked) {
            var props = Get(level);
            int defenderStun = blocked ? props.Blockstun : props.Hitstun;
            int attackerRemaining = remainingActiveFrames + recoveryFrames;
            return defenderStun - attackerRemaining;
        }
    }
}