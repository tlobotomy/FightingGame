using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
    ///   - Create a full-screen UI panel (child of the Battle scene Canvas).
    ///   - Start it DISABLED in the inspector.
    ///   - Wire MatchManager.PostMatchPanel to this GameObject.
    ///   - Wire button references in the inspector.
    ///   - This script handles input from both players' gamepads
    ///     and keyboard. Cursor navigation uses up/down + confirm.
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

        [Header("Cursor Highlight")]
        [Tooltip("Color applied to the currently selected button's text.")]
        public Color SelectedColor = Color.yellow;
        [Tooltip("Color for unselected buttons.")]
        public Color UnselectedColor = Color.white;

        [Header("Timing")]
        [Tooltip("Delay in seconds before the panel appears (lets the KO/win banner play first).")]
        public float ShowDelay = 3f;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private Button[] _buttons;
        private int _selectedIndex;
        private bool _active;
        private bool _inputLocked; // brief lock after activation to prevent accidental confirm
        private float _inputLockTimer;
        private const float INPUT_LOCK_DURATION = 0.5f;

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Awake() {
            if (PanelRoot != null)
                PanelRoot.SetActive(false);

            _buttons = new[] { RematchButton, CharacterSelectButton, MainMenuButton };
        }

        /// <summary>
        /// Called by MatchManager when the match ends.
        /// Shows the result and activates the menu after a delay.
        /// </summary>
        public void Show(string resultText) {
            if (ResultLabel != null)
                ResultLabel.text = resultText;

            Invoke(nameof(ActivatePanel), ShowDelay);
        }

        private void ActivatePanel() {
            if (PanelRoot != null)
                PanelRoot.SetActive(true);

            _active = true;
            _selectedIndex = 0;
            _inputLocked = true;
            _inputLockTimer = INPUT_LOCK_DURATION;
            UpdateHighlight();

            // Wire button click callbacks (for mouse/touch)
            // RemoveAllListeners first to prevent duplicates on rematch
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

        private void Update() {
            if (!_active) return;

            // Input lock cooldown
            if (_inputLocked) {
                _inputLockTimer -= Time.unscaledDeltaTime;
                if (_inputLockTimer <= 0f)
                    _inputLocked = false;
                return;
            }

            // Navigate with keyboard/gamepad (any player can navigate)
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                MoveCursor(-1);
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                MoveCursor(1);

            // Gamepad D-pad (raw axis)
            float vertical = Input.GetAxisRaw("Vertical");
            if (vertical > 0.5f && !_wasHoldingUp)
                MoveCursor(-1);
            else if (vertical < -0.5f && !_wasHoldingDown)
                MoveCursor(1);
            _wasHoldingUp = vertical > 0.5f;
            _wasHoldingDown = vertical < -0.5f;

            // Confirm
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)
                || Input.GetButtonDown("Submit")) {
                ConfirmSelection();
            }
        }

        private bool _wasHoldingUp;
        private bool _wasHoldingDown;

        // ──────────────────────────────────────
        //  CURSOR
        // ──────────────────────────────────────

        private void MoveCursor(int direction) {
            _selectedIndex += direction;
            if (_selectedIndex < 0) _selectedIndex = _buttons.Length - 1;
            if (_selectedIndex >= _buttons.Length) _selectedIndex = 0;
            UpdateHighlight();
        }

        private void UpdateHighlight() {
            for (int i = 0; i < _buttons.Length; i++) {
                if (_buttons[i] == null) continue;

                var label = _buttons[i].GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.color = (i == _selectedIndex) ? SelectedColor : UnselectedColor;

                // Scale bump for selected button
                _buttons[i].transform.localScale = (i == _selectedIndex)
                    ? Vector3.one * 1.1f
                    : Vector3.one;
            }
        }

        private void ConfirmSelection() {
            switch (_selectedIndex) {
                case 0: OnRematch(); break;
                case 1: OnCharacterSelect(); break;
                case 2: OnMainMenu(); break;
            }
        }

        // ──────────────────────────────────────
        //  ACTIONS
        // ──────────────────────────────────────

        private void OnRematch() {
            // Reload battle scene — MatchSettings still has the same characters/stage
            Time.timeScale = 1f;
            SceneManager.LoadScene(BattleSceneName);
        }

        private void OnCharacterSelect() {
            // Return to character select — clear stage but keep device mappings
            MatchSettings.SelectedStage = null;
            Time.timeScale = 1f;
            SceneManager.LoadScene(CharacterSelectSceneName);
        }

        private void OnMainMenu() {
            // Full reset
            MatchSettings.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(MainMenuSceneName);
        }
    }
}