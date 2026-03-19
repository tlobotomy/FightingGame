using UnityEngine;
using FightingGame.Data;

namespace FightingGame.ScriptableObjects {
    /// <summary>
    /// A complete moveset for one character. Groups all moves by category
    /// and provides fast lookup for the input parser.
    ///
    /// Create via:  Assets > Create > Fighting Game > Moveset
    /// </summary>
    [CreateAssetMenu(fileName = "NewMoveset", menuName = "Fighting Game/Moveset", order = 2)]
    public class MovesetData : ScriptableObject {
        [Header("Identity")]
        public string MovesetName;

        // ──────────────────────────────────────
        //  NORMALS
        // ──────────────────────────────────────

        [Header("Standing Normals")]
        public MoveData StandLightPunch;
        public MoveData StandMediumPunch;
        public MoveData StandHeavyPunch;
        public MoveData StandLightKick;
        public MoveData StandMediumKick;
        public MoveData StandHeavyKick;

        [Header("Crouching Normals")]
        public MoveData CrouchLightPunch;
        public MoveData CrouchMediumPunch;
        public MoveData CrouchHeavyPunch;
        public MoveData CrouchLightKick;
        public MoveData CrouchMediumKick;
        public MoveData CrouchHeavyKick;

        [Header("Jumping Normals")]
        public MoveData JumpLightPunch;
        public MoveData JumpMediumPunch;
        public MoveData JumpHeavyPunch;
        public MoveData JumpLightKick;
        public MoveData JumpMediumKick;
        public MoveData JumpHeavyKick;

        // ──────────────────────────────────────
        //  COMMAND NORMALS & UNIQUE ATTACKS
        // ──────────────────────────────────────

        [Header("Command Normals / Unique Attacks")]
        [Tooltip("e.g. Ryu's f+HP (Collarbone Breaker). Direction + button, no motion.")]
        public MoveData[] CommandNormals;

        // ──────────────────────────────────────
        //  SPECIALS
        // ──────────────────────────────────────

        [Header("Special Moves")]
        [Tooltip("All special moves. Sorted by InputPriority descending at runtime.")]
        public MoveData[] Specials;

        [Header("EX Specials")]
        [Tooltip("EX versions (two buttons). Listed separately for clarity, " +
                 "but they participate in the same priority sort.")]
        public MoveData[] EXSpecials;

        // ──────────────────────────────────────
        //  SUPERS
        // ──────────────────────────────────────

        [Header("Super Arts")]
        [Tooltip("In 3S, each character picks one of 3 super arts. " +
                 "List all options here; the selected index is on CharacterData.")]
        public SuperArtSlot[] SuperArts;

        // ──────────────────────────────────────
        //  UNIVERSAL ACTIONS
        // ──────────────────────────────────────

        [Header("Throws")]
        public MoveData ForwardThrow;
        public MoveData BackThrow;

        [Header("Universal Mechanics")]
        [Tooltip("Forward dash.")]
        public MoveData Dash;

        [Tooltip("Back dash.")]
        public MoveData BackDash;

        [Tooltip("Taunt (3S: Ken/Q taunts grant buffs).")]
        public MoveData Taunt;

        // ──────────────────────────────────────
        //  TARGET COMBOS
        // ──────────────────────────────────────

        [Header("Target Combos")]
        [Tooltip("Pre-defined chain routes (e.g. Dudley's f+MK > MK > HK). " +
                 "Each entry is a full sequence from starter to ender.")]
        public TargetCombo[] TargetCombos;

        // ──────────────────────────────────────
        //  LOOKUP HELPERS
        // ──────────────────────────────────────

        /// <summary>
        /// Returns the standing/crouching/jumping normal for a given button
        /// and stance. Used by the parser when no motion is detected.
        /// </summary>
        public MoveData GetNormal(ButtonInput button, MoveUsableState stance) {
            switch (stance) {
                case MoveUsableState.Standing:
                    return GetStandingNormal(button);
                case MoveUsableState.Crouching:
                    return GetCrouchingNormal(button);
                case MoveUsableState.Airborne:
                    return GetJumpingNormal(button);
                default:
                    return null;
            }
        }

        private MoveData GetStandingNormal(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.LightPunch: return StandLightPunch;
                case ButtonInput.MediumPunch: return StandMediumPunch;
                case ButtonInput.HeavyPunch: return StandHeavyPunch;
                case ButtonInput.LightKick: return StandLightKick;
                case ButtonInput.MediumKick: return StandMediumKick;
                case ButtonInput.HeavyKick: return StandHeavyKick;
                default: return null;
            }
        }

        private MoveData GetCrouchingNormal(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.LightPunch: return CrouchLightPunch;
                case ButtonInput.MediumPunch: return CrouchMediumPunch;
                case ButtonInput.HeavyPunch: return CrouchHeavyPunch;
                case ButtonInput.LightKick: return CrouchLightKick;
                case ButtonInput.MediumKick: return CrouchMediumKick;
                case ButtonInput.HeavyKick: return CrouchHeavyKick;
                default: return null;
            }
        }

        private MoveData GetJumpingNormal(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.LightPunch: return JumpLightPunch;
                case ButtonInput.MediumPunch: return JumpMediumPunch;
                case ButtonInput.HeavyPunch: return JumpHeavyPunch;
                case ButtonInput.LightKick: return JumpLightKick;
                case ButtonInput.MediumKick: return JumpMediumKick;
                case ButtonInput.HeavyKick: return JumpHeavyKick;
                default: return null;
            }
        }

        /// <summary>
        /// Returns all special + EX moves merged and sorted by
        /// InputPriority descending. Cache this at runtime.
        /// </summary>
        public MoveData[] GetAllSpecialsSorted() {
            int totalLen = (Specials?.Length ?? 0) + (EXSpecials?.Length ?? 0);
            var all = new MoveData[totalLen];
            int idx = 0;

            if (Specials != null)
                foreach (var m in Specials) all[idx++] = m;
            if (EXSpecials != null)
                foreach (var m in EXSpecials) all[idx++] = m;

            System.Array.Sort(all, (a, b) => b.InputPriority.CompareTo(a.InputPriority));
            return all;
        }
    }

    /// <summary>
    /// A super art slot — 3S lets you pick one of three,
    /// each with its own meter length and stock count.
    /// </summary>
    [System.Serializable]
    public struct SuperArtSlot {
        public string Name;
        public MoveData Move;

        [Tooltip("How many meter stocks this super art provides.")]
        [Min(1)] public int MaxStocks;

        [Tooltip("Meter units per stock (e.g. SA1 might be 1 long bar, SA3 might be 3 short bars).")]
        [Min(1)] public int MeterPerStock;

        [Tooltip("Meter cost per use (in units, not stocks).")]
        [Min(1)] public int CostPerUse;
    }

    /// <summary>
    /// A fixed chain of moves that bypasses normal cancel rules
    /// (e.g. Dudley's target combos).
    /// </summary>
    [System.Serializable]
    public struct TargetCombo {
        public string Name;

        [Tooltip("Ordered sequence of moves. Each move can cancel into the next " +
                 "regardless of cancel level.")]
        public MoveData[] Sequence;
    }
}