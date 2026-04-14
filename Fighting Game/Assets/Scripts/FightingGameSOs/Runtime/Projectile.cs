using UnityEngine;
using FightingGame.Data;

namespace FightingGame.Runtime {
    /// <summary>
    /// A projectile spawned by a move. Travels in a direction, has its own
    /// hitbox, and is resolved by MatchManager alongside player hitboxes.
    ///
    /// Setup:
    ///   - Create a prefab with a SpriteRenderer (or animated sprite).
    ///   - Add this Projectile component.
    ///   - Configure speed, lifetime, and hitbox in the inspector.
    ///   - Reference the prefab from MoveData.ProjectilePrefab.
    ///   - MatchManager handles collision; this script handles movement/lifetime.
    /// </summary>
    public class Projectile : MonoBehaviour {
        [Header("Movement")]
        [Tooltip("Horizontal speed in units per frame.")]
        public float Speed = 0.08f;

        [Tooltip("Vertical speed (0 for straight projectiles, positive for arcing up).")]
        public float VerticalSpeed = 0f;

        [Tooltip("Gravity applied per frame (for arcing projectiles).")]
        public float Gravity = 0f;

        [Header("Lifetime")]
        [Tooltip("Maximum frames before the projectile self-destructs.")]
        [Min(1)] public int MaxLifetimeFrames = 300;

        [Tooltip("How many hits before the projectile is destroyed (1 = destroyed on first hit).")]
        [Min(1)] public int Durability = 1;

        [Header("Hitbox")]
        [Tooltip("The projectile's hitbox, relative to its position.")]
        public BoxRect Hitbox;

        [Header("On-Hit Properties")]
        [Tooltip("Damage dealt by the projectile.")]
        [Min(0)] public int Damage = 30;

        [Tooltip("Chip damage on block.")]
        [Min(0)] public int ChipDamage = 5;

        [Tooltip("Hitstun frames on hit.")]
        [Min(0)] public int Hitstun = 14;

        [Tooltip("Blockstun frames on block.")]
        [Min(0)] public int Blockstun = 12;

        [Tooltip("Attack height for blocking purposes.")]
        public AttackHeight Height = AttackHeight.Mid;

        [Header("Effects")]
        public GameObject HitEffectPrefab;
        public AudioClip HitSound;
        public AudioClip BlockSound;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private int _facingSign;
        private int _framesAlive;
        private float _velocityY;
        private int _hitsRemaining;
        private GameObject _owner; // who spawned this (so we don't hit ourselves)
        private bool _alive = true;

        /// <summary>The owner player object (used by MatchManager to skip self-hit).</summary>
        public GameObject Owner => _owner;
        public bool IsAlive => _alive;
        public int FacingSign => _facingSign;

        // ──────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────

        /// <summary>
        /// Called by PlayerController after instantiation.
        /// </summary>
        public void Initialize(int facingSign, GameObject owner) {
            _facingSign = facingSign;
            _owner = owner;
            _framesAlive = 0;
            _velocityY = VerticalSpeed;
            _hitsRemaining = Durability;
            _alive = true;

            // Flip sprite if facing left
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Abs(scale.x) * facingSign;
            transform.localScale = scale;
        }

        // ──────────────────────────────────────
        //  TICK (called from MatchManager)
        // ──────────────────────────────────────

        /// <summary>
        /// Advance the projectile by one game frame. Called by MatchManager
        /// in FixedUpdate alongside player ticks.
        /// </summary>
        public void GameTick() {
            if (!_alive) return;

            _framesAlive++;

            // Movement
            Vector3 pos = transform.position;
            pos.x += Speed * _facingSign;
            _velocityY -= Gravity;
            pos.y += _velocityY;
            transform.position = pos;

            // Lifetime
            if (_framesAlive >= MaxLifetimeFrames) {
                DestroyProjectile();
            }
        }

        /// <summary>
        /// Returns the world-space hitbox rect for collision checking.
        /// </summary>
        public Rect GetHitboxRect() {
            return Hitbox.GetWorldRect(transform.position, _facingSign);
        }

        /// <summary>
        /// Called by MatchManager when the projectile hits something.
        /// </summary>
        public void OnHitConfirmed() {
            _hitsRemaining--;
            if (_hitsRemaining <= 0)
                DestroyProjectile();
        }

        /// <summary>
        /// Called when two projectiles collide and cancel each other out.
        /// </summary>
        public void OnProjectileClash() {
            DestroyProjectile();
        }

        private void DestroyProjectile() {
            _alive = false;

            if (HitEffectPrefab != null)
                Instantiate(HitEffectPrefab, transform.position, Quaternion.identity);

            Destroy(gameObject);
        }
    }
}