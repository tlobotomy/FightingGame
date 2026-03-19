using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// A single slot in the character select grid. Place one per
    /// character as a child of the GridContainer.
    ///
    /// This is a pure UI display component — it doesn't handle input.
    /// CharacterSelectManager controls cursor movement and selection.
    ///
    /// Setup: add to each grid cell, assign the corresponding
    /// CharacterData, and it auto-populates the portrait + name.
    /// </summary>
    public class CharacterSelectSlot : MonoBehaviour {
        [Header("Data")]
        [Tooltip("The character this slot represents.")]
        public CharacterData Character;

        [Header("UI Elements")]
        [Tooltip("Portrait image inside this slot.")]
        public Image PortraitImage;

        [Tooltip("Name label under the portrait (optional).")]
        public TMP_Text NameLabel;

        [Header("Visual States")]
        [Tooltip("Background image to tint when highlighted/locked.")]
        public Image BackgroundImage;

        public Color DefaultColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public Color P1HighlightColor = new Color(0.2f, 0.4f, 1f, 1f);
        public Color P2HighlightColor = new Color(1f, 0.3f, 0.2f, 1f);
        public Color BothHighlightColor = new Color(0.8f, 0.2f, 0.8f, 1f);
        public Color LockedColor = new Color(1f, 0.85f, 0f, 1f);

        private bool _p1Highlighted;
        private bool _p2Highlighted;
        private bool _locked;

        private void Start() {
            PopulateFromCharacterData();
            ResetVisual();
        }

        /// <summary>
        /// Auto-fills portrait and name from the assigned CharacterData.
        /// </summary>
        public void PopulateFromCharacterData() {
            if (Character == null) return;

            if (PortraitImage != null && Character.Portrait != null)
                PortraitImage.sprite = Character.Portrait;

            if (NameLabel != null)
                NameLabel.text = Character.CharacterName;
        }

        /// <summary>
        /// Called by CharacterSelectManager to highlight/unhighlight
        /// this slot for a given player.
        /// </summary>
        public void SetHighlighted(int playerIndex, bool highlighted) {
            if (playerIndex == 0) _p1Highlighted = highlighted;
            else _p2Highlighted = highlighted;
            UpdateVisual();
        }

        /// <summary>
        /// Called when a player locks in this character.
        /// </summary>
        public void SetLocked(bool locked) {
            _locked = locked;
            UpdateVisual();
        }

        public void ResetVisual() {
            _p1Highlighted = false;
            _p2Highlighted = false;
            _locked = false;
            UpdateVisual();
        }

        private void UpdateVisual() {
            if (BackgroundImage == null) return;

            if (_locked)
                BackgroundImage.color = LockedColor;
            else if (_p1Highlighted && _p2Highlighted)
                BackgroundImage.color = BothHighlightColor;
            else if (_p1Highlighted)
                BackgroundImage.color = P1HighlightColor;
            else if (_p2Highlighted)
                BackgroundImage.color = P2HighlightColor;
            else
                BackgroundImage.color = DefaultColor;
        }
    }
}