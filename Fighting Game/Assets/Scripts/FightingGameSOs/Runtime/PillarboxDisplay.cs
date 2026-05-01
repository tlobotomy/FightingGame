using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Populates the pillarbox side panels with character art
    /// based on the selections made at the character select screen.
    ///
    /// Setup:
    ///   1. Create a Canvas named "PillarboxCanvas".
    ///   2. Set Canvas Render Mode to "Screen Space - Overlay" (CRITICAL —
    ///      if you use "Screen Space - Camera", the canvas will be clipped
    ///      to the gameplay camera's viewport rect, which is exactly the area
    ///      the pillarbox is NOT supposed to cover).
    ///   3. Set Canvas Sort Order to -1 (renders behind the Battle UI canvas).
    ///   4. Add this script to the PillarboxCanvas.
    ///   5. Create two child panels (left and right) anchored to the edges.
    ///   6. Wire Image and Text references in the inspector.
    ///   7. PillarboxSetup (on the Main Camera) handles the viewport rect
    ///      and creates the background camera automatically.
    /// </summary>
    public class PillarboxDisplay : MonoBehaviour {
        [Header("P1 (Left Pillarbox)")]
        [Tooltip("Image component that displays P1's character art.")]
        public Image P1Art;

        [Tooltip("Optional name label under P1's art.")]
        public TMP_Text P1Name;

        [Tooltip("Background panel image (for tinting or keeping black if no art).")]
        public Image P1Background;

        [Header("P2 (Right Pillarbox)")]
        public Image P2Art;
        public TMP_Text P2Name;
        public Image P2Background;

        [Header("Settings")]
        [Tooltip("If true, P2's art is flipped horizontally so both characters face inward.")]
        public bool FlipP2Art = true;

        [Tooltip("Optional tint applied behind the character art.")]
        public Color BackgroundTint = Color.black;

        private void Start() {
            SetupPanel(0, P1Art, P1Name, P1Background);
            SetupPanel(1, P2Art, P2Name, P2Background);
        }

        private void SetupPanel(int playerIndex, Image artImage, TMP_Text nameLabel, Image background) {
            CharacterData character = MatchSettings.SelectedCharacters[playerIndex];

            // Apply background tint
            if (background != null)
                background.color = BackgroundTint;

            if (character == null) {
                // No character selected (shouldn't happen in normal flow)
                if (artImage != null) artImage.enabled = false;
                if (nameLabel != null) nameLabel.text = "";
                return;
            }

            // Set character art
            if (artImage != null) {
                if (character.FullBodyArt != null) {
                    artImage.sprite = character.FullBodyArt;
                    artImage.enabled = true;
                    artImage.preserveAspect = true;

                    // Apply per-character scale and offset
                    float scale = character.PillarboxArtScale;
                    float flipX = (playerIndex == 1 && FlipP2Art) ? -1f : 1f;

                    artImage.rectTransform.localScale = new Vector3(
                        scale * flipX, scale, 1f);

                    artImage.rectTransform.anchoredPosition = new Vector2(
                        artImage.rectTransform.anchoredPosition.x,
                        character.PillarboxArtOffsetY);
                }
                else {
                    artImage.enabled = false;
                }
            }

            // Set name label
            if (nameLabel != null)
                nameLabel.text = character.CharacterName;
        }

        /// <summary>
        /// Call this if characters change mid-session
        /// (e.g. rematch with different characters).
        /// </summary>
        public void Refresh() {
            SetupPanel(0, P1Art, P1Name, P1Background);
            SetupPanel(1, P2Art, P2Name, P2Background);
        }
    }
}