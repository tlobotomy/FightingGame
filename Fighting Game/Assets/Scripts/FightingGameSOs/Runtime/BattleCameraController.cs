using UnityEngine;

namespace FightingGame.Runtime {
    /// <summary>
    /// Dynamic 2D fighting game camera inspired by Guilty Gear Accent Core.
    /// Tracks both players horizontally and vertically, zooms in when close
    /// and out when far apart. Smoothly interpolates all values.
    ///
    /// HOW IT WORKS (GGAC-style):
    ///   - Horizontal position = midpoint between both players.
    ///   - Vertical position = midpoint Y with upward bias for jumps.
    ///   - Orthographic size = lerped based on player distance.
    ///   - All values are clamped to stage bounds so the camera never
    ///     shows outside the play area.
    ///
    /// CINEMACHINE ALTERNATIVE:
    ///   If you have Cinemachine installed, you can replace this with a
    ///   CinemachineVirtualCamera + CinemachineTargetGroup + FramingTransposer.
    ///   This script provides the same behavior without requiring the package,
    ///   and gives you fighting-game-specific control over the zoom curve.
    ///
    ///   To use Cinemachine instead:
    ///     1. Install Cinemachine via Package Manager.
    ///     2. Create a CinemachineTargetGroup with both BattlePlayer transforms.
    ///     3. Create a CinemachineVirtualCamera, set Follow/LookAt to the group.
    ///     4. Add a CinemachineFramingTransposer body with appropriate damping.
    ///     5. Use a CinemachineConfiner2D to clamp to stage bounds.
    ///     6. Write a small script to adjust ortho size based on group radius.
    ///
    /// Setup:
    ///   - Attach to the Main Camera (or a dedicated camera object).
    ///   - Drag the MatchManager reference (it reads player transforms from there).
    ///   - Camera must be Orthographic.
    ///   - Set the Z position to -10 (or whatever your sprite layer needs).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class BattleCameraController : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("References")]
        [Tooltip("The scene's MatchManager — camera reads player positions from it.")]
        public MatchManager Match;

        [Header("Zoom")]
        [Tooltip("Minimum orthographic size (players very close together).")]
        public float MinOrthoSize = 3.5f;

        [Tooltip("Maximum orthographic size (players at max distance apart).")]
        public float MaxOrthoSize = 5.4f;

        [Tooltip("Player distance at which the camera reaches MinOrthoSize.")]
        public float MinZoomDistance = 1.5f;

        [Tooltip("Player distance at which the camera reaches MaxOrthoSize.")]
        public float MaxZoomDistance = 8f;

        [Header("Vertical Tracking")]
        [Tooltip("Base Y position when both players are grounded.")]
        public float BaseY = 2.5f;

        [Tooltip("How much the camera rises to follow airborne players (0 = no follow, 1 = full follow).")]
        [Range(0f, 1f)]
        public float VerticalFollowStrength = 0.5f;

        [Tooltip("Maximum upward offset from BaseY.")]
        public float MaxVerticalOffset = 2f;

        [Header("Smoothing")]
        [Tooltip("Horizontal follow smoothing (lower = snappier).")]
        public float HorizontalDamping = 0.1f;

        [Tooltip("Vertical follow smoothing.")]
        public float VerticalDamping = 0.15f;

        [Tooltip("Zoom smoothing.")]
        public float ZoomDamping = 0.12f;

        [Header("Stage Bounds")]
        [Tooltip("If true, reads bounds from MatchManager. If false, uses the manual overrides below.")]
        public bool UseMatchManagerBounds = true;

        public float ManualLeftBound = -6f;
        public float ManualRightBound = 6f;
        public float ManualGroundY = 0f;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private Camera _cam;
        private Transform _p1;
        private Transform _p2;
        private bool _initialized;

        // Smooth damp velocities
        private float _velX;
        private float _velY;
        private float _velZoom;

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Awake() {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
        }

        private void LateUpdate() {
            // Wait for players to be spawned
            if (!_initialized) {
                if (Match == null) return;
                var p1 = Match.GetPlayer(0);
                var p2 = Match.GetPlayer(1);
                if (p1 == null || p2 == null) return;

                _p1 = p1.transform;
                _p2 = p2.transform;
                _initialized = true;

                // Snap to initial position (no smoothing on first frame)
                SnapToTarget();
                return;
            }

            UpdateCamera();
        }

        // ──────────────────────────────────────
        //  CAMERA LOGIC
        // ──────────────────────────────────────

        private void UpdateCamera() {
            float p1x = _p1.position.x;
            float p2x = _p2.position.x;
            float p1y = _p1.position.y;
            float p2y = _p2.position.y;

            // --- TARGET POSITION ---
            float targetX = (p1x + p2x) * 0.5f;

            // Vertical: base Y + upward offset based on highest player
            float highestY = Mathf.Max(p1y, p2y);
            float groundY = UseMatchManagerBounds ? Match.GroundY : ManualGroundY;
            float verticalOffset = (highestY - groundY) * VerticalFollowStrength;
            verticalOffset = Mathf.Clamp(verticalOffset, 0f, MaxVerticalOffset);
            float targetY = BaseY + verticalOffset;

            // --- TARGET ZOOM ---
            float playerDistance = Mathf.Abs(p1x - p2x);
            float zoomT = Mathf.InverseLerp(MinZoomDistance, MaxZoomDistance, playerDistance);
            float targetOrtho = Mathf.Lerp(MinOrthoSize, MaxOrthoSize, zoomT);

            // --- SMOOTH ---
            float smoothX = Mathf.SmoothDamp(transform.position.x, targetX, ref _velX, HorizontalDamping);
            float smoothY = Mathf.SmoothDamp(transform.position.y, targetY, ref _velY, VerticalDamping);
            float smoothOrtho = Mathf.SmoothDamp(_cam.orthographicSize, targetOrtho, ref _velZoom, ZoomDamping);

            // --- CLAMP TO STAGE BOUNDS ---
            float leftBound = UseMatchManagerBounds ? Match.StageLeftBound : ManualLeftBound;
            float rightBound = UseMatchManagerBounds ? Match.StageRightBound : ManualRightBound;

            // The camera's visible half-width depends on ortho size and aspect ratio
            float aspect = _cam.aspect;

            // If using viewport rect for pillarbox, account for that
            float viewportWidth = _cam.rect.width;
            float effectiveAspect = aspect * viewportWidth;

            float halfWidth = smoothOrtho * effectiveAspect;
            float halfHeight = smoothOrtho;

            // Clamp horizontal so camera edges don't exceed stage bounds
            float minCamX = leftBound + halfWidth;
            float maxCamX = rightBound - halfWidth;
            if (minCamX > maxCamX) {
                // Stage is narrower than camera view — center the camera
                smoothX = (leftBound + rightBound) * 0.5f;
            }
            else {
                smoothX = Mathf.Clamp(smoothX, minCamX, maxCamX);
            }

            // Clamp vertical so camera doesn't go below ground
            float minCamY = groundY + halfHeight;
            smoothY = Mathf.Max(smoothY, minCamY);

            // --- APPLY ---
            transform.position = new Vector3(smoothX, smoothY, transform.position.z);
            _cam.orthographicSize = smoothOrtho;
        }

        /// <summary>
        /// Instantly snaps camera to target (no smoothing). Called on init
        /// and at round start to prevent the camera from "flying in."
        /// </summary>
        public void SnapToTarget() {
            if (_p1 == null || _p2 == null) return;

            float targetX = (_p1.position.x + _p2.position.x) * 0.5f;
            float targetY = BaseY;

            float playerDistance = Mathf.Abs(_p1.position.x - _p2.position.x);
            float zoomT = Mathf.InverseLerp(MinZoomDistance, MaxZoomDistance, playerDistance);
            float targetOrtho = Mathf.Lerp(MinOrthoSize, MaxOrthoSize, zoomT);

            transform.position = new Vector3(targetX, targetY, transform.position.z);
            _cam.orthographicSize = targetOrtho;

            _velX = 0f;
            _velY = 0f;
            _velZoom = 0f;
        }
    }
}