using UnityEngine;
using FightingGame.Data;

namespace FightingGame.ScriptableObjects {
    /// <summary>
    /// A single move — everything the game needs to know about a
    /// Hadouken, cr.MK, throw, super art, etc.
    ///
    /// Create via:  Assets > Create > Fighting Game > Move
    /// </summary>
    [CreateAssetMenu(fileName = "NewMove", menuName = "Fighting Game/Move", order = 1)]
    public class MoveData : ScriptableObject {
        [Header("Identity")]
        [Tooltip("Display name shown in command list / training mode (e.g. 'Shoryuken').")]
        public string MoveName;

        [Tooltip("Short notation label (e.g. 'cr.MK', 'DP', '236P').")]
        public string Notation;

        [Tooltip("Category — affects priority resolution and cancel rules.")]
        public MoveType Type;

        [Tooltip("Which stance(s) this move can be performed from.")]
        public MoveUsableState UsableFrom = MoveUsableState.Standing;

        // ──────────────────────────────────────
        //  INPUT
        // ──────────────────────────────────────

        [Header("Input")]
        public MotionInput Motion;

        [Tooltip("For command normals: the stick direction that must be held. " +
                 "Ignored for specials/supers (they use Motion.Type).")]
        public DirectionInput RequiredDirection;

        [Tooltip("Priority weight — higher number wins when multiple moves match " +
                 "on the same frame. Typically: Normal=0, Command=10, Special=20, Super=30.")]
        public int InputPriority;

        // ──────────────────────────────────────
        //  FRAME DATA
        // ──────────────────────────────────────

        [Header("Frame Data")]
        public FrameData Frames;

        // ──────────────────────────────────────
        //  DAMAGE
        // ──────────────────────────────────────

        [Header("Damage")]
        public DamageData Damage;

        // ──────────────────────────────────────
        //  PROPERTIES
        // ──────────────────────────────────────

        [Header("Hit Properties")]
        public AttackHeight Height;
        public HitEffect OnHitEffect;

        [Tooltip("Knockback velocity applied to the opponent on hit.")]
        public Vector2 HitKnockback;

        [Tooltip("Knockback on block (pushback).")]
        public Vector2 BlockKnockback;

        [Tooltip("Number of hits (for multi-hit moves like a super).")]
        [Min(1)] public int HitCount = 1;

        [Tooltip("Frames between each hit of a multi-hit move.")]
        [Min(0)] public int MultiHitInterval;

        // ──────────────────────────────────────
        //  CANCELS
        // ──────────────────────────────────────

        [Header("Cancel Rules")]
        public CancelData Cancel;

        [Tooltip("Specific moves this move can chain into (e.g. target combos). " +
                 "If empty, standard cancel-level rules apply.")]
        public MoveData[] TargetComboRoutes;

        // ──────────────────────────────────────
        //  HITBOXES
        // ──────────────────────────────────────

        [Header("Hitboxes")]
        [Tooltip("Hitbox frames — each entry describes which frames a set of hitboxes is active.")]
        public HitboxFrame[] HitboxFrames;

        [Header("Hurtbox Overrides")]
        [Tooltip("If set, replaces the character's default hurtbox layout during this move " +
                 "(e.g. crouching hurtbox during a sweep, invincible frames on a reversal).")]
        public HurtboxLayout[] HurtboxOverrides;

        [Tooltip("Frame ranges for each hurtbox override (parallel array with HurtboxOverrides).")]
        public Vector2Int[] HurtboxOverrideFrameRanges;

        // ──────────────────────────────────────
        //  MOVEMENT
        // ──────────────────────────────────────

        [Header("Movement During Move")]
        [Tooltip("Optional movement applied during the move (e.g. lunge forward, rise into the air).")]
        public MoveMovement Movement;

        // ──────────────────────────────────────
        //  VISUALS / AUDIO
        // ──────────────────────────────────────

        [Header("Animation")]
        [Tooltip("Animator state name or animation clip to play.")]
        public string AnimationStateName;
        public AnimationClip AnimationClip;

        [Header("Effects")]
        [Tooltip("Prefab spawned on hit (hit sparks, etc.).")]
        public GameObject HitEffectPrefab;

        [Tooltip("Prefab spawned on whiff (swing trail, etc.).")]
        public GameObject WhiffEffectPrefab;

        [Header("Audio")]
        public AudioClip SwingSound;
        public AudioClip HitSound;
        public AudioClip BlockSound;

        [Header("Projectile (if applicable)")]
        [Tooltip("If this move spawns a projectile, reference it here.")]
        public GameObject ProjectilePrefab;

        [Tooltip("Frame on which the projectile spawns.")]
        public int ProjectileSpawnFrame;

        [Tooltip("Spawn offset from character pivot.")]
        public Vector2 ProjectileSpawnOffset;

        // ──────────────────────────────────────
        //  METER
        // ──────────────────────────────────────

        [Header("Meter Requirements")]
        [Tooltip("Super meter cost to perform this move (0 for non-supers).")]
        [Min(0)] public int MeterCost;

        [Tooltip("Which super art stock this uses (for 3S-style multi-stock supers). " +
                 "-1 = doesn't use super stocks.")]
        public int SuperStockIndex = -1;

        // ──────────────────────────────────────
        //  3S-SPECIFIC PROPERTIES
        // ──────────────────────────────────────

        [Header("Third Strike Specifics")]
        [Tooltip("Can this move be parried?")]
        public bool Parryable = true;

        [Tooltip("Is this move an EX version (costs meter, enhanced properties)?")]
        public bool IsEX;

        [Tooltip("Juggle points this move costs (for juggle limit system).")]
        [Min(0)] public int JuggleCost;

        // ──────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────

        /// <summary>
        /// Quick check: can this move cancel into `target` given the current frame?
        /// </summary>
        public bool CanCancelInto(MoveData target, int currentMoveFrame) {
            // Target combo routes override normal cancel rules
            if (TargetComboRoutes != null && TargetComboRoutes.Length > 0) {
                foreach (var route in TargetComboRoutes)
                    if (route == target) return true;
            }

            // Super cancel always allowed if flagged
            if (Cancel.AlwaysSuperCancellable && target.Type == MoveType.Super)
                return Cancel.IsInCancelWindow(currentMoveFrame);

            // Standard cancel level check
            if ((int)target.GetCancelLevel() <= (int)Cancel.MaxCancelLevel)
                return false; // can't cancel into same or lower level

            return Cancel.IsInCancelWindow(currentMoveFrame);
        }

        /// <summary>
        /// Maps MoveType to CancelLevel for the hierarchy check.
        /// </summary>
        public CancelLevel GetCancelLevel() {
            switch (Type) {
                case MoveType.Normal: return CancelLevel.Normal;
                case MoveType.CommandNormal: return CancelLevel.Command;
                case MoveType.Special: return CancelLevel.Special;
                case MoveType.Super: return CancelLevel.Super;
                default: return CancelLevel.None;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Auto-name the asset based on the move name for easier browsing.
        /// </summary>
        private void OnValidate() {
            if (!string.IsNullOrEmpty(MoveName) && name != MoveName) {
                // Optional: auto-rename the asset file in the editor
                // UnityEditor.AssetDatabase.RenameAsset(
                //     UnityEditor.AssetDatabase.GetAssetPath(this), MoveName);
            }
        }
#endif
    }
}