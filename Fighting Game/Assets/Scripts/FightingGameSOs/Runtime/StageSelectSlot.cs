using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// A single slot in the stage select grid. Place one per stage
    /// as a child of the GridContainer.
    ///
    /// This is a pure UI display component — it doesn't handle input.
    /// StageSelectManager controls cursor movement and selection.
    ///
    /// Setup: add to each grid cell, assign the corresponding
    /// StageData, and it auto-populates the thumbnail + name.
    /// </summary>
    public class StageSelectSlot : MonoBehaviour {
        [Header("Data")]
        [Tooltip("The stage this slot represents.")]
        public StageData Stage;

        [Header("UI Elements")]
        [Tooltip("Thumbnail image inside this slot.")]
        public Image ThumbnailImage;

        [Tooltip("Name label under the thumbnail (optional).")]
        public TMP_Text NameLabel;

        [Header("Visual States")]
        [Tooltip("Background image to tint when highlighted/confirmed.")]
        public Image BackgroundImage;

        public Color DefaultColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public Color HighlightColor = new Color(0.3f, 0.6f, 1f, 1f);
        public Color ConfirmedColor = new Color(1f, 0.85f, 0f, 1f);

        private bool _highlighted;
        private bool _confirmed;

        private void Start() {
            PopulateFromStageData();
            ResetVisual();
        }

        /// <summary>
        /// Auto-fills thumbnail and name from the assigned StageData.
        /// </summary>
        public void PopulateFromStageData() {
            if (Stage == null) return;

            if (ThumbnailImage != null && Stage.PreviewImage != null)
                ThumbnailImage.sprite = Stage.PreviewImage;

            if (NameLabel != null)
                NameLabel.text = Stage.StageName;
        }

        public void SetHighlighted(bool highlighted) {
            _highlighted = highlighted;
            UpdateVisual();
        }

        public void SetConfirmed(bool confirmed) {
            _confirmed = confirmed;
            UpdateVisual();
        }

        public void ResetVisual() {
            _highlighted = false;
            _confirmed = false;
            UpdateVisual();
        }

        private void UpdateVisual() {
            if (BackgroundImage == null) return;

            if (_confirmed)
                BackgroundImage.color = ConfirmedColor;
            else if (_highlighted)
                BackgroundImage.color = HighlightColor;
            else
                BackgroundImage.color = DefaultColor;
        }
    }
}