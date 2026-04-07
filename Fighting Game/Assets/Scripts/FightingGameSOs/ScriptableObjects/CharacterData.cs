using UnityEngine;
using FightingGame.Data;

namespace FightingGame.ScriptableObjects {
    /// <summary>
    /// Top-level character definition — the single asset you drag
    /// onto a PlayerController to fully define a fighter.
    ///
    /// Create via:  Assets > Create > Fighting Game > Character
    /// </summary>
    [CreateAssetMenu(fileName = "NewCharacter", menuName = "Fighting Game/Character", order = 0)]
    public class CharacterData : ScriptableObject {
        // ──────────────────────────────────────
        //  IDENTITY
        // ──────────────────────────────────────

        [Header("Identity")]
        public string CharacterName;

        [TextArea(2, 4)]
        public string Description;

        [Tooltip("Character portrait for select screen / HUD.")]
        public Sprite Portrait;

        [Tooltip("Full-body art for select screen.")]
        public Sprite FullBodyArt;

        // ──────────────────────────────────────
        //  PREFAB
        // ──────────────────────────────────────

        [Header("Prefab")]
        [Tooltip("The in-game character prefab (with Animator, hurtboxes, etc).")]
        public GameObject CharacterPrefab;

        // ──────────────────────────────────────
        //  MOVESET
        // ──────────────────────────────────────

        [Header("Moveset")]
        [Tooltip("The full moveset for this character.")]
        public MovesetData Moveset;

        // ──────────────────────────────────────
        //  STATS
        // ──────────────────────────────────────

        [Header("Health & Stun")]
        [Min(1)] public int MaxHealth = 1200;

        [Tooltip("Stun meter capacity (reaching 0 = dizzy).")]
        [Min(1)] public int MaxStun = 72;

        [Tooltip("Stun recovery rate (points per frame while not getting hit).")]
        [Min(0)] public int StunRecoveryRate = 1;

        [Tooltip("Frames of dizzy when stunned.")]
        [Min(1)] public int StunDuration = 180;

        [Header("Movement")]
        [Tooltip("Walk forward speed (units per frame).")]
        public float WalkForwardSpeed = 0.04f;

        [Tooltip("Walk backward speed (typically slower).")]
        public float WalkBackwardSpeed = 0.03f;

        [Header("Jump")]
        [Tooltip("Jump horizontal velocity.")]
        public float JumpForwardSpeed = 0.045f;

        [Tooltip("Jump vertical initial velocity.")]
        public float JumpHeight = 0.12f;

        [Tooltip("Gravity acceleration per frame.")]
        public float Gravity = 0.008f;

        [Tooltip("Pre-jump frames (grounded but committed to jumping — can't block).")]
        [Min(1)] public int PreJumpFrames = 4;

        [Tooltip("Landing recovery frames.")]
        [Min(0)] public int JumpLandingFrames = 2;

        [Header("Dash")]
        [Tooltip("Forward dash total duration in frames.")]
        [Min(1)] public int DashDuration = 18;

        [Tooltip("Forward dash distance (total units).")]
        public float DashDistance = 1.5f;

        [Tooltip("Back dash total duration (usually has invincibility).")]
        [Min(1)] public int BackDashDuration = 22;

        [Tooltip("Back dash distance.")]
        public float BackDashDistance = 1.2f;

        [Tooltip("Invincibility frames at the start of back dash.")]
        [Min(0)] public int BackDashInvincibleFrames = 5;

        // ──────────────────────────────────────
        //  DEFENSE
        // ──────────────────────────────────────

        [Header("Defense")]
        [Tooltip("Defense modifier (1.0 = normal, <1 = takes more damage like Akuma).")]
        [Range(0.5f, 1.5f)] public float DefenseModifier = 1.0f;

        [Tooltip("Pushback modifier when blocking (affects how far back they slide).")]
        [Range(0.5f, 1.5f)] public float BlockPushbackModifier = 1.0f;

        // ──────────────────────────────────────
        //  HURTBOXES (DEFAULT STANCES)
        // ──────────────────────────────────────

        [Header("Default Hurtboxes")]
        [Tooltip("Hurtbox layout when standing idle.")]
        public HurtboxLayout StandingHurtbox;

        [Tooltip("Hurtbox layout when crouching.")]
        public HurtboxLayout CrouchingHurtbox;

        [Tooltip("Hurtbox layout while airborne.")]
        public HurtboxLayout AirborneHurtbox;

        [Header("Pushbox")]
        [Tooltip("The character's collision pushbox (prevents overlapping).")]
        public BoxRect Pushbox;

        // ──────────────────────────────────────
        //  THIRD STRIKE MECHANICS
        // ──────────────────────────────────────

        [Header("Parry (3S)")]
        [Tooltip("Parry input window in frames (typically 10 for forward parry).")]
        [Min(1)] public int ParryWindowFrames = 10;

        [Tooltip("Recovery frames after a failed parry attempt (can't block).")]
        [Min(1)] public int ParryWhiffRecovery = 20;

        [Tooltip("Meter gained on successful parry.")]
        [Min(0)] public int ParryMeterGain = 10;

        [Tooltip("Frames of hit freeze on successful parry.")]
        [Min(0)] public int ParryHitStop = 12;

        [Header("Meter")]
        [Tooltip("Total tension/super meter capacity for this character.")]
        [Min(1)] public int MaxMeter = 100;

        /// <summary>
        /// Total meter capacity. All supers draw from the same meter pool.
        /// </summary>
        public int GetTotalMeterCapacity() {
            return MaxMeter;
        }

        // ──────────────────────────────────────
        //  AUDIO / VISUAL
        // ──────────────────────────────────────

        [Header("Audio")]
        public AudioClip[] HitVoiceClips;
        public AudioClip[] AttackVoiceClips;

        [Header("Colors / Palette")]
        [Tooltip("Alternate color palettes (3S has ~6 per character).")]
        public Material[] ColorPalettes;

        [Header("Pillarbox Art Settings")]
        [Tooltip("Scale multiplier for this character's pillarbox art (1 = default, 1.5 = 50% bigger). " +
                 "Use this to make smaller characters fill more of the pillarbox.")]
        public float PillarboxArtScale = 1f;

        [Tooltip("Vertical offset for the pillarbox art (positive = up). " +
                 "Use this to reposition characters who sit too high or low.")]
        public float PillarboxArtOffsetY = 0f;

        // ──────────────────────────────────────
        //  VALIDATION
        // ──────────────────────────────────────

        // No editor validation needed — all supers are always available.
    }
}