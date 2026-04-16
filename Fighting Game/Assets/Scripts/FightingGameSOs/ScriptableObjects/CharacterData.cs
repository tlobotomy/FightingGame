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

        [Tooltip("Number of air jumps allowed (1 = double jump, 2 = triple jump like Chipp). 0 = no air jump.")]
        [Min(0)] public int AirJumps = 1;

        [Tooltip("Air jump vertical velocity (can differ from ground jump).")]
        public float AirJumpHeight = 0.10f;

        [Tooltip("Air jump horizontal speed multiplier.")]
        public float AirJumpForwardSpeed = 0.04f;

        [Header("Air Dash")]
        [Tooltip("Number of air dashes allowed per airtime (0 = no air dash). " +
                 "Most GGACR characters get 1; Millia/Chipp get 2.")]
        [Min(0)] public int AirDashes = 1;

        [Tooltip("Air dash horizontal speed (units per frame).")]
        public float AirDashSpeed = 0.08f;

        [Tooltip("Air dash duration in frames.")]
        [Min(1)] public int AirDashDuration = 16;

        [Tooltip("Air back dash horizontal speed.")]
        public float AirBackDashSpeed = 0.06f;

        [Tooltip("Air back dash duration in frames.")]
        [Min(1)] public int AirBackDashDuration = 14;

        [Tooltip("Minimum airborne frames before air dash is allowed (prevents instant air dash from ground).")]
        [Min(0)] public int AirDashMinHeight = 4;

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

        [Tooltip("FD pushback multiplier on top of BlockPushbackModifier (GGXX FD pushes further).")]
        [Range(1f, 3f)] public float FDPushbackMultiplier = 1.5f;

        [Tooltip("Instant Block window in frames (how early before the hit you must block).")]
        [Min(1)] public int InstantBlockWindow = 8;

        [Header("Guard Crush")]
        [Tooltip("Frames of stagger when guard crush occurs (from unblockable or guard break moves).")]
        [Min(1)] public int GuardCrushStaggerFrames = 40;

        [Header("Throw")]
        [Tooltip("Throw startup frames (opponent can tech during this window).")]
        [Min(1)] public int ThrowStartupFrames = 3;

        [Tooltip("Throw tech window — frames the defender has to input throw to escape.")]
        [Min(1)] public int ThrowTechWindow = 10;

        [Header("Wakeup")]
        [Tooltip("Frames of wakeup during which a reversal special/super can be input.")]
        [Min(0)] public int WakeupReversalWindow = 4;

        [Tooltip("Back roll distance on ground tech.")]
        public float BackRollDistance = 1.5f;

        [Tooltip("Back roll duration in frames.")]
        [Min(1)] public int BackRollFrames = 20;

        [Tooltip("Invincibility frames during back roll.")]
        [Min(0)] public int BackRollInvincibleFrames = 14;

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

        [Header("Tension Gauge (GGXX)")]
        [Tooltip("Total tension gauge capacity (10000 = full bar in GGXX internal units).")]
        [Min(1)] public int MaxMeter = 10000;

        [Tooltip("Tension gained per frame while walking forward.")]
        [Min(0)] public int TensionGainForwardWalk = 8;

        [Tooltip("Tension gained per frame during forward dash.")]
        [Min(0)] public int TensionGainForwardDash = 16;

        [Tooltip("Tension gained when an attack connects (hit).")]
        [Min(0)] public int TensionGainOnHit = 72;

        [Tooltip("Tension gained when an attack is blocked by opponent.")]
        [Min(0)] public int TensionGainOnBlock = 36;

        [Tooltip("Tension gained on whiffed attack (per swing).")]
        [Min(0)] public int TensionGainOnWhiff = 18;

        [Tooltip("Tension gained per frame spent in startup/active of a move (rewarding aggression).")]
        [Min(0)] public int TensionGainPerAttackFrame = 2;

        [Tooltip("Frames of idle/backward walk before the Tension Pulse (negative penalty) kicks in.")]
        [Min(1)] public int TensionPulseThreshold = 120;

        [Tooltip("Tension drained per frame during negative penalty state.")]
        [Min(0)] public int TensionDrainRate = 4;

        [Tooltip("How many frames the negative penalty lasts once triggered.")]
        [Min(1)] public int NegativePenaltyDuration = 600;

        [Tooltip("Tension cost for Faultless Defense per frame (quarter-circle back + button while blocking).")]
        [Min(0)] public int FaultlessDefenseCostPerFrame = 50;

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