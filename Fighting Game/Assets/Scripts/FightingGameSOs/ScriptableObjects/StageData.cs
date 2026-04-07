using UnityEngine;

namespace FightingGame.ScriptableObjects {
    /// <summary>
    /// Data asset for a single stage. Carries everything the battle scene
    /// needs to set up the arena: boundaries, visuals, music, and optional
    /// gameplay modifiers.
    ///
    /// Create via:  Assets > Create > Fighting Game > Stage
    /// </summary>
    [CreateAssetMenu(fileName = "NewStage", menuName = "Fighting Game/Stage", order = 3)]
    public class StageData : ScriptableObject {
        // ──────────────────────────────────────
        //  IDENTITY
        // ──────────────────────────────────────

        [Header("Identity")]
        [Tooltip("Display name shown on the stage select screen.")]
        public string StageName;

        [Tooltip("Short description or subtitle (e.g. 'Downtown — Night').")]
        public string Description;

        [Tooltip("Preview image shown on the stage select screen.")]
        public Sprite PreviewImage;

        // ──────────────────────────────────────
        //  BACKGROUND VISUALS
        // ──────────────────────────────────────

        [Header("Background")]
        [Tooltip("Prefab instantiated as the stage background. Can include " +
                 "parallax layers, animated elements, etc.")]
        public GameObject BackgroundPrefab;

        // ──────────────────────────────────────
        //  STAGE BOUNDS
        // ──────────────────────────────────────

        [Header("Stage Bounds")]
        [Tooltip("Left boundary of the stage (world X).")]
        public float LeftBound = -6f;

        [Tooltip("Right boundary of the stage (world X).")]
        public float RightBound = 6f;

        [Tooltip("Ground Y position.")]
        public float GroundY = 0f;

        // ──────────────────────────────────────
        //  AUDIO
        // ──────────────────────────────────────

        [Header("Music")]
        [Tooltip("Background music track for this stage.")]
        public AudioClip BGM;

        [Tooltip("Music volume multiplier (1.0 = full volume).")]
        [Range(0f, 1f)] public float BGMVolume = 1f;

        [Tooltip("Should the BGM loop?")]
        public bool BGMLoop = true;

        // ──────────────────────────────────────
        //  GAMEPLAY MODIFIERS (optional)
        // ──────────────────────────────────────

        [Header("Gameplay Modifiers")]
        [Tooltip("Global damage multiplier on this stage (1.0 = normal). " +
                 "Set > 1 for a 'danger zone' feel, < 1 for a calmer stage.")]
        public float DamageMultiplier = 1f;

        [Tooltip("Round timer override. 0 = use MatchManager's default.")]
        [Min(0)] public int RoundTimeOverride = 0;

        [Tooltip("Gravity scale on this stage (1.0 = normal). Affects jump arcs.")]
        public float GravityScale = 1f;
    }
}