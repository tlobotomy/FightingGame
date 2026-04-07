using UnityEngine;

namespace FightingGame.Runtime {
    /// <summary>
    /// Smooths visual rendering between FixedUpdate ticks.
    ///
    /// Problem: PlayerController moves the parent transform in FixedUpdate (60fps),
    /// but Unity renders at the display refresh rate. Between ticks the position
    /// is static, causing visible stutter or perceived lag.
    ///
    /// Solution: This script sits on the PARENT (BattlePlayer) and offsets its
    /// child visual each render frame by interpolating between the previous and
    /// current FixedUpdate positions. The actual transform.position stays
    /// deterministic — only the visual gets smoothed.
    ///
    /// Setup: Add this component to the BattlePlayerPrefab. It will automatically
    /// find the first child (the visual) and interpolate it.
    /// </summary>
    public class FixedUpdateInterpolator : MonoBehaviour {
        private Vector3 _previousPosition;
        private Vector3 _currentPosition;
        private Transform _visual;

        private void Start() {
            // Cache the visual child (spawned by MatchManager)
            // Will be null until the visual is instantiated, so we also check in Update
            FindVisual();
            _previousPosition = transform.position;
            _currentPosition = transform.position;
        }

        private void FindVisual() {
            if (transform.childCount > 0)
                _visual = transform.GetChild(0);
        }

        /// <summary>
        /// Called by Unity right before FixedUpdate. Snapshot the position
        /// so we know where we WERE before physics moves us.
        /// </summary>
        private void FixedUpdate() {
            _previousPosition = _currentPosition;
            _currentPosition = transform.position;
        }

        private void Update() {
            if (_visual == null) {
                FindVisual();
                if (_visual == null) return;
            }

            // Unity's interpolation factor: how far between the last
            // FixedUpdate and the next one this render frame falls.
            // 0 = exactly at last tick, 1 = exactly at next tick.
            float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
            t = Mathf.Clamp01(t);

            // Interpolate the visual's world position between the two snapshots
            Vector3 interpolated = Vector3.Lerp(_previousPosition, _currentPosition, t);

            // Apply as a local offset so we don't fight the parent's actual position
            _visual.position = interpolated;
        }

        /// <summary>
        /// After rendering, snap the visual back to the true position
        /// so FixedUpdate calculations aren't affected by the visual offset.
        /// </summary>
        private void LateUpdate() {
            if (_visual != null)
                _visual.localPosition = Vector3.zero;
        }
    }
}