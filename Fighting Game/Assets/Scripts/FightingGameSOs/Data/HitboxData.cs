using System;
using UnityEngine;

namespace FightingGame.Data {
    /// <summary>
    /// A single hitbox or hurtbox rect, defined relative to the
    /// character's pivot. These are NOT Unity colliders — the game
    /// checks them manually each frame for determinism.
    /// </summary>
    [Serializable]
    public struct BoxRect {
        [Tooltip("Offset from the character pivot (flipped automatically when facing left).")]
        public Vector2 Offset;

        [Tooltip("Half-extents of the box.")]
        public Vector2 Size;

        /// <summary>
        /// Returns the world-space rect given a position and facing direction.
        /// </summary>
        public Rect GetWorldRect(Vector2 position, int facingSign) {
            Vector2 worldOffset = new Vector2(Offset.x * facingSign, Offset.y);
            Vector2 center = position + worldOffset;
            return new Rect(center - Size, Size * 2f);
        }
    }

    /// <summary>
    /// Per-frame hitbox layout. A move can have different hitbox
    /// shapes on different active frames (e.g. a multi-hit move
    /// where the second hit reaches further).
    /// </summary>
    [Serializable]
    public struct HitboxFrame {
        [Tooltip("Which active frame(s) this hitbox applies to (0-indexed from first active frame).")]
        public int StartFrame;
        public int EndFrame;

        [Tooltip("The attack hitbox(es) — can be multiple rects per frame.")]
        public BoxRect[] Hitboxes;

        [Tooltip("Pushbox adjustment during these frames (optional).")]
        public BoxRect PushboxOverride;
        public bool OverridePushbox;
    }

    /// <summary>
    /// The character's hurtbox layout for a given state.
    /// Different from hitboxes — these receive hits.
    /// </summary>
    [Serializable]
    public struct HurtboxLayout {
        [Tooltip("Human-readable label (e.g. 'Standing', 'Crouching', 'Jump Arc').")]
        public string Label;

        public BoxRect[] Hurtboxes;

        [Tooltip("If true, this hurtbox set is fully invincible (e.g. during a reversal).")]
        public bool Invincible;

        [Tooltip("If true, only the lower body is invincible (low-crush moves).")]
        public bool LowInvincible;

        [Tooltip("If true, only the upper body is invincible (high-crush moves).")]
        public bool HighInvincible;

        [Tooltip("If true, throw-invincible (airborne frames, some startup frames).")]
        public bool ThrowInvincible;
    }

    /// <summary>
    /// Movement applied to the character during a move (e.g. a
    /// dragon punch rising, a slide moving forward).
    /// </summary>
    [Serializable]
    public struct MoveMovement {
        [Tooltip("Velocity curve applied each frame. X = forward, Y = up.")]
        public AnimationCurve HorizontalCurve;
        public AnimationCurve VerticalCurve;

        [Tooltip("Scale multiplier for the curves.")]
        public float HorizontalSpeed;
        public float VerticalSpeed;

        [Tooltip("If true, the character is airborne during this move (affects gravity, juggle state).")]
        public bool Airborne;

        /// <summary>
        /// Evaluate the movement vector at a normalized time (0..1 over total frames).
        /// </summary>
        public Vector2 Evaluate(float normalizedTime, int facingSign) {
            float h = HorizontalCurve != null
                ? HorizontalCurve.Evaluate(normalizedTime) * HorizontalSpeed * facingSign
                : 0f;
            float v = VerticalCurve != null
                ? VerticalCurve.Evaluate(normalizedTime) * VerticalSpeed
                : 0f;
            return new Vector2(h, v);
        }
    }
}