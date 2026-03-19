using UnityEngine;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Debug gizmo renderer that draws hitboxes, hurtboxes, and pushboxes
    /// in the Scene view. Attach to each player's GameObject.
    ///
    /// Color key:
    ///   RED     = attack hitboxes (active frames only)
    ///   GREEN   = hurtboxes (always visible)
    ///   YELLOW  = pushbox (always visible)
    ///   CYAN    = invincible hurtbox override
    ///   MAGENTA = projectile spawn point
    ///
    /// Toggle visibility with the public bools in the inspector.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class HitboxVisualizer : MonoBehaviour {
        [Header("Visibility Toggles")]
        public bool ShowHitboxes = true;
        public bool ShowHurtboxes = true;
        public bool ShowPushbox = true;
        public bool ShowProjectileSpawn = true;

        [Header("Transparency")]
        [Range(0f, 1f)] public float FillAlpha = 0.25f;
        [Range(0f, 1f)] public float OutlineAlpha = 0.8f;

        private PlayerController _controller;

        private void Awake() {
            _controller = GetComponent<PlayerController>();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (_controller == null || _controller.Character == null) return;

            Vector2 pos = transform.position;
            int facing = _controller.FacingSign;

            // --- PUSHBOX ---
            if (ShowPushbox) {
                Rect pushRect = _controller.Character.Pushbox.GetWorldRect(pos, facing);
                DrawBoxGizmo(pushRect, Color.yellow);
            }

            // --- HURTBOXES ---
            if (ShowHurtboxes) {
                HurtboxLayout layout = GetCurrentHurtboxLayout();
                if (layout.Hurtboxes != null) {
                    Color hurtColor = layout.Invincible ? Color.cyan : Color.green;
                    foreach (var box in layout.Hurtboxes) {
                        Rect rect = box.GetWorldRect(pos, facing);
                        DrawBoxGizmo(rect, hurtColor);
                    }
                }
            }

            // --- HITBOXES ---
            if (ShowHitboxes && _controller.CurrentMove != null
                && _controller.State == PlayerController.PlayerState.Active) {
                MoveData move = _controller.CurrentMove;
                if (move.HitboxFrames != null) {
                    int activeFrame = _controller.MoveFrame - move.Frames.Startup;
                    foreach (var hbf in move.HitboxFrames) {
                        if (activeFrame >= hbf.StartFrame && activeFrame <= hbf.EndFrame
                            && hbf.Hitboxes != null) {
                            foreach (var box in hbf.Hitboxes) {
                                Rect rect = box.GetWorldRect(pos, facing);
                                DrawBoxGizmo(rect, Color.red);
                            }
                        }
                    }
                }
            }

            // --- PROJECTILE SPAWN POINT ---
            if (ShowProjectileSpawn && _controller.CurrentMove != null
                && _controller.CurrentMove.ProjectilePrefab != null) {
                Vector2 spawnOffset = _controller.CurrentMove.ProjectileSpawnOffset;
                Vector3 spawnPos = new Vector3(
                    pos.x + spawnOffset.x * facing,
                    pos.y + spawnOffset.y,
                    0);
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(spawnPos, 0.08f);
            }
        }

        private void DrawBoxGizmo(Rect rect, Color color) {
            Vector3 center = new Vector3(rect.center.x, rect.center.y, 0);
            Vector3 size = new Vector3(rect.width, rect.height, 0);

            // Filled
            Color fill = color;
            fill.a = FillAlpha;
            Gizmos.color = fill;
            Gizmos.DrawCube(center, size);

            // Outline
            Color outline = color;
            outline.a = OutlineAlpha;
            Gizmos.color = outline;
            Gizmos.DrawWireCube(center, size);
        }

        private HurtboxLayout GetCurrentHurtboxLayout() {
            // Check move overrides first
            if (_controller.CurrentMove != null
                && _controller.CurrentMove.HurtboxOverrides != null
                && _controller.CurrentMove.HurtboxOverrideFrameRanges != null) {
                int moveFrame = _controller.MoveFrame;
                for (int i = 0; i < _controller.CurrentMove.HurtboxOverrides.Length; i++) {
                    if (i >= _controller.CurrentMove.HurtboxOverrideFrameRanges.Length) break;
                    var range = _controller.CurrentMove.HurtboxOverrideFrameRanges[i];
                    if (moveFrame >= range.x && moveFrame <= range.y)
                        return _controller.CurrentMove.HurtboxOverrides[i];
                }
            }

            // Stance defaults
            switch (_controller.State) {
                case PlayerController.PlayerState.Crouching:
                    return _controller.Character.CrouchingHurtbox;
                case PlayerController.PlayerState.Airborne:
                case PlayerController.PlayerState.PreJump:
                    return _controller.Character.AirborneHurtbox;
                default:
                    return _controller.Character.StandingHurtbox;
            }
        }
#endif
    }
}