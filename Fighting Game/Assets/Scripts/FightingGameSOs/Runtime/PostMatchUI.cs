using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

namespace FightingGame.Runtime {
    /// <summary>
    /// Post-match overlay displayed after the final round.
    /// Shows the match result and three options:
    ///   - Rematch (reload battle scene with same characters/stage)
    ///   - Character Select (return to character select)
    ///   - Main Menu (return to title screen)
    ///
    /// Setup:
    ///   1. Create a UI panel as a child of the battle scene Canvas.
    ///   2. Start the panel DISABLED in the inspector.
    ///   3. Put this script on any always-active GameObject in the scene.
    ///   4. Wire PanelRoot to the disabled panel, and wire the button/label slots.
    ///   5. On MatchManager, drag this into the PostMatchPanel slot.
    ///   6. Make sure the panel, buttons, and text all have non-zero scale
    ///      and properly anchored RectTransforms.
    ///
    /// MatchManager calls Show() after the winner banner finishes.
    /// Show() enables the panel and wires the buttons.
    /// Navigation is handled by the EventSystem's InputSystemUIInputModule.
    /// </summary>
    public class PostMatchUI : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Panel")]
        [Tooltip("Root panel — enabled when the match ends, disabled during gameplay.")]
        public GameObject PanelRoot;

        [Header("Result Text")]
        [Tooltip("Large text showing 'P1 WINS', 'P2 WINS', or 'DRAW GAME'.")]
        public TMP_Text ResultLabel;

        [Header("Buttons")]
        [Tooltip("Rematch button — reloads the battle scene with same settings.")]
        public Button RematchButton;

        [Tooltip("Character Select button — returns to character select.")]
        public Button CharacterSelectButton;

        [Tooltip("Main Menu button — returns to the title screen.")]
        public Button MainMenuButton;

        [Header("Scene Names")]
        [Tooltip("Name of the battle scene (for rematch reload).")]
        public string BattleSceneName = "BattleScene";

        [Tooltip("Name of the character select scene.")]
        public string CharacterSelectSceneName = "CharacterSelectScene";

        [Tooltip("Name of the main menu / title screen scene.")]
        public string MainMenuSceneName = "MainMenuScene";

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Awake() {
            if (PanelRoot != null)
                PanelRoot.SetActive(false);
        }

        /// <summary>
        /// Called by MatchManager after the winner banner finishes.
        /// Enables the panel, wires button callbacks, and sets the
        /// initial selection for stick/gamepad navigation.
        /// </summary>
        public void Show(string resultText) {
            if (PanelRoot == null) {
                Debug.LogError("[PostMatchUI] PanelRoot is NULL!");
                return;
            }

            // Enable the panel
            PanelRoot.SetActive(true);

            // Set result text
            if (ResultLabel != null)
                ResultLabel.text = resultText;

            // Set Rematch as the initial selection for stick navigation
            if (RematchButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(RematchButton.gameObject);

            // Wire button callbacks
            if (RematchButton != null) {
                RematchButton.onClick.RemoveAllListeners();
                RematchButton.onClick.AddListener(OnRematch);
            }
            if (CharacterSelectButton != null) {
                CharacterSelectButton.onClick.RemoveAllListeners();
                CharacterSelectButton.onClick.AddListener(OnCharacterSelect);
            }
            if (MainMenuButton != null) {
                MainMenuButton.onClick.RemoveAllListeners();
                MainMenuButton.onClick.AddListener(OnMainMenu);
            }
        }

        // ──────────────────────────────────────
        //  ACTIONS
        // ──────────────────────────────────────

        private void OnRematch() {
            Time.timeScale = 1f;
            SceneManager.LoadScene(BattleSceneName);
        }

        private void OnCharacterSelect() {
            MatchSettings.SelectedStage = null;
            Time.timeScale = 1f;
            SceneManager.LoadScene(CharacterSelectSceneName);
        }

        private void OnMainMenu() {
            MatchSettings.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuSceneName);
        }
    }
}