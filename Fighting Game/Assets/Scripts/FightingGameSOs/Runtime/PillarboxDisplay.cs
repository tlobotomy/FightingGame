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
    ///   - Attach to the PillarboxCanvas GameObject
    ///   - Assign the Image and optional Text references in the inspector
    ///   - Art is pulled from CharacterData.FullBodyArt
    ///   - Runs once on Start, reading from MatchSettings
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