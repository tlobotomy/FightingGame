using UnityEngine;

namespace FightingGame.Runtime {
    /// <summary>
    /// Configures the main camera's viewport rect to create a 4:3 pillarboxed
    /// gameplay area, and sets up a background camera to fill the remaining
    /// screen with black (or any solid color).
    ///
    /// PILLARBOX RENDERING ARCHITECTURE:
    ///   1. BackgroundCamera: Depth -2, renders a solid color across the full screen.
    ///      Created automatically at runtime. Clears to BackgroundColor.
    ///   2. GameplayCamera (Main Camera): Depth 0, renders gameplay in the center
    ///      4:3 viewport rect. Does NOT clear the full screen (only its rect).
    ///   3. PillarboxCanvas: Must be set to "Screen Space - Overlay" so it renders
    ///      over the FULL screen regardless of the gameplay camera's viewport rect.
    ///      The left and right panels sit in the pillarbox bands.
    ///
    /// WHY "Screen Space - Camera" BREAKS:
    ///   If the pillarbox Canvas uses "Screen Space - Camera" with the gameplay
    ///   camera, Unity renders the Canvas INSIDE the camera's viewport rect.
    ///   That means the pillarbox panels get clipped to the 4:3 area —
    ///   exactly where they're NOT supposed to be. Overlay mode bypasses this.
    ///
    /// Setup:
    ///   - Attach this to the Main Camera (same object as BattleCameraController).
    ///   - Drag the GameplayCamera reference (usually the same camera).
    ///   - Set TargetAspect to 4:3 (1.333) or your desired ratio.
    ///   - Make sure your PillarboxCanvas is set to "Screen Space - Overlay".
    ///   - The background camera is auto-created; you don't need to set it up.
    /// </summary>
    public class PillarboxSetup : MonoBehaviour {
        [Header("References")]
        [Tooltip("The gameplay camera whose viewport will be restricted to the target aspect ratio.")]
        public Camera GameplayCamera;

        [Header("Aspect Ratio")]
        [Tooltip("Target gameplay aspect ratio (width / height). 4:3 = 1.333, 16:9 = 1.778.")]
        public float TargetAspect = 4f / 3f;

        [Header("Background")]
        [Tooltip("Color shown in the pillarbox bands (behind the UI art panels).")]
        public Color BackgroundColor = Color.black;

        private Camera _bgCamera;

        private void Awake() {
            if (GameplayCamera == null)
                GameplayCamera = GetComponent<Camera>();

            SetupViewport();
            SetupBackgroundCamera();
        }

        private void SetupViewport() {
            if (GameplayCamera == null) return;

            float screenAspect = (float)Screen.width / Screen.height;

            if (screenAspect > TargetAspect) {
                // Screen is wider than target — add pillarbox (left/right bars)
                float viewportWidth = TargetAspect / screenAspect;
                float xOffset = (1f - viewportWidth) * 0.5f;

                GameplayCamera.rect = new Rect(xOffset, 0f, viewportWidth, 1f);
            }
            else {
                // Screen is narrower or equal — use full width
                // (could add letterbox here if needed, but fighting games rarely need it)
                GameplayCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private void SetupBackgroundCamera() {
            // Create a background camera that fills the entire screen with a solid color.
            // This sits behind the gameplay camera (lower depth) and renders nothing
            // except its clear color — providing the black bands.
            var bgObj = new GameObject("PillarboxBackgroundCamera");
            bgObj.transform.SetParent(transform, false);

            _bgCamera = bgObj.AddComponent<Camera>();
            _bgCamera.depth = GameplayCamera.depth - 2;
            _bgCamera.clearFlags = CameraClearFlags.SolidColor;
            _bgCamera.backgroundColor = BackgroundColor;
            _bgCamera.cullingMask = 0; // render nothing — just the background color
            _bgCamera.orthographic = true;
        }

        /// <summary>
        /// Call if the screen resolution changes at runtime (e.g. window resize).
        /// </summary>
        public void RecalculateViewport() {
            SetupViewport();
        }

#if UNITY_EDITOR
        // In the editor, screen size can change between play and scene view.
        // Recalculate each frame to keep the pillarbox correct.
        private void Update() {
            SetupViewport();
        }
#endif
    }
}