using UnityEngine;
using FightingGame.Data;

namespace FightingGame.ScriptableObjects {
    /// <summary>
    /// A complete moveset for one character. Groups all moves by category
    /// and provides fast lookup for the input parser.
    ///
    /// GGXX layout: 4 main buttons (P / K / S / HS) + Dust (universal).
    /// Each stance has 4 normals. Dust is shared across stances.
    ///
    /// Create via:  Assets > Create > Fighting Game > Moveset
    /// </summary>
    [CreateAssetMenu(fileName = "NewMoveset", menuName = "Fighting Game/Moveset", order = 2)]
    public class MovesetData : ScriptableObject {
        [Header("Identity")]
        public string MovesetName;

        // ──────────────────────────────────────
        //  NORMALS  (GGXX 4-button: P / K / S / HS)
        // ──────────────────────────────────────

        [Header("Standing Normals (5P, 5K, 5S, 5HS)")]
        public MoveData StandPunch;
        public MoveData StandKick;
        public MoveData StandSlash;
        public MoveData StandHeavySlash;

        [Header("Crouching Normals (2P, 2K, 2S, 2HS)")]
        public MoveData CrouchPunch;
        public MoveData CrouchKick;
        public MoveData CrouchSlash;
        public MoveData CrouchHeavySlash;

        [Header("Jumping Normals (j.P, j.K, j.S, j.HS)")]
        public MoveData JumpPunch;
        public MoveData JumpKick;
        public MoveData JumpSlash;
        public MoveData JumpHeavySlash;

        // ──────────────────────────────────────
        //  DUST (universal overhead / launcher)
        // ──────────────────────────────────────

        [Header("Dust (5D — universal overhead)")]
        [Tooltip("Standing Dust attack. In GGXX this is a universal overhead / launcher.")]
        public MoveData StandDust;

        [Tooltip("Crouching Dust (2D — sweep in some GG games).")]
        public MoveData CrouchDust;

        [Tooltip("Jumping Dust (j.D).")]
        public MoveData JumpDust;

        // ──────────────────────────────────────
        //  CLOSE NORMALS (optional — some GG chars
        //  have proximity normals like close 5S)
        // ──────────────────────────────────────

        [Header("Close Normals (optional proximity variants)")]
        [Tooltip("Close standing Slash (f.S or cl.S depending on the game version).")]
        public MoveData CloseSlash;

        [Tooltip("Close standing Heavy Slash (optional).")]
        public MoveData CloseHeavySlash;

        // ──────────────────────────────────────
        //  COMMAND NORMALS & UNIQUE ATTACKS
        // ──────────────────────────────────────

        [Header("Command Normals / Unique Attacks")]
        [Tooltip("e.g. 6P (anti-air punch), 6K, 6HS. Direction + button, no motion.")]
        public MoveData[] CommandNormals;

        // ──────────────────────────────────────
        //  SPECIALS
        // ──────────────────────────────────────

        [Header("Special Moves")]
        [Tooltip("All special moves. Sorted by InputPriority descending at runtime.")]
        public MoveData[] Specials;

        [Header("EX Specials")]
        [Tooltip("EX/Force-Break versions (cost meter). Listed separately for clarity, " +
                 "but they participate in the same priority sort.")]
        public MoveData[] EXSpecials;

        // ──────────────────────────────────────
        //  SUPERS / OVERDRIVES
        // ──────────────────────────────────────

        [Header("Super Arts / Overdrives")]
        [Tooltip("All supers/Overdrives for this character. " +
                 "All are available at all times — no selection step.")]
        public SuperArtSlot[] SuperArts;

        // ──────────────────────────────────────
        //  UNIVERSAL ACTIONS
        // ──────────────────────────────────────

        [Header("Throws")]
        [Tooltip("Forward throw (4/6 + HS in GGXX, or dedicated throw button).")]
        public MoveData ForwardThrow;
        public MoveData BackThrow;

        [Header("Universal Mechanics")]
        // NOTE: Dashes do NOT use MoveData. They are parameter-driven
        // from CharacterData (DashDuration, DashDistance, etc.) and
        // triggered by double-tap forward/back in PlayerController.

        [Tooltip("Taunt / Respect (triggered by HS+D).")]
        public MoveData Taunt;

        // ──────────────────────────────────────
        //  GATLING / TARGET COMBOS
        // ──────────────────────────────────────

        [Header("Gatling Routes / Target Combos")]
        [Tooltip("Pre-defined chain routes (e.g. P > K > S > HS Gatling table). " +
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
                case ButtonInput.Punch: return StandPunch;
                case ButtonInput.Kick: return StandKick;
                case ButtonInput.Slash: return StandSlash;
                case ButtonInput.HeavySlash: return StandHeavySlash;
                case ButtonInput.Dust: return StandDust;
                default: return null;
            }
        }

        private MoveData GetCrouchingNormal(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.Punch: return CrouchPunch;
                case ButtonInput.Kick: return CrouchKick;
                case ButtonInput.Slash: return CrouchSlash;
                case ButtonInput.HeavySlash: return CrouchHeavySlash;
                case ButtonInput.Dust: return CrouchDust;
                default: return null;
            }
        }

        private MoveData GetJumpingNormal(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.Punch: return JumpPunch;
                case ButtonInput.Kick: return JumpKick;
                case ButtonInput.Slash: return JumpSlash;
                case ButtonInput.HeavySlash: return JumpHeavySlash;
                case ButtonInput.Dust: return JumpDust;
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
    /// A super art / Overdrive slot.
    /// All supers are available simultaneously — no selection mechanic.
    /// </summary>
    [System.Serializable]
    public struct SuperArtSlot {
        public string Name;
        public MoveData Move;

        [Tooltip("How many meter stocks this super art provides.")]
        [Min(1)] public int MaxStocks;

        [Tooltip("Meter units per stock.")]
        [Min(1)] public int MeterPerStock;

        [Tooltip("Meter cost per use (in units, not stocks).")]
        [Min(1)] public int CostPerUse;
    }

    /// <summary>
    /// A fixed chain of moves that bypasses normal cancel rules
    /// (Gatling combos, target combos).
    /// </summary>
    [System.Serializable]
    public struct TargetCombo {
        public string Name;

        [Tooltip("Ordered sequence of moves. Each move can cancel into the next " +
                 "regardless of cancel level (Gatling route).")]
        public MoveData[] Sequence;
    }
}