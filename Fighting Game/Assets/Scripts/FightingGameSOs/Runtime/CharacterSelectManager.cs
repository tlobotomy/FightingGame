using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Manages the character select screen for two local players.
    ///
    /// Flow:
    ///   1. Players join by pressing a button on their device
    ///   2. Each navigates the roster grid with their stick/dpad
    ///   3. A button press locks in the character
    ///   4. Super art selection appears for each locked player
    ///   5. Once both are locked with super arts chosen, transition to battle
    ///
    /// Setup:
    ///   - Attach to a GameObject alongside a PlayerInputManager
    ///   - Set PlayerInputManager's Player Prefab to a prefab with only
    ///     a PlayerInput component + CharacterSelectPlayer component
    ///   - Set PlayerInputManager's Max Player Count to 2
    ///   - Wire PlayerInputManager's Player Joined Event → this.OnPlayerJoined
    ///   - Fill the Roster array with every selectable CharacterData
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public class CharacterSelectManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Roster")]
        [Tooltip("All selectable characters, in grid order.")]
        public CharacterData[] Roster;

        [Tooltip("How many columns in the character grid (for cursor wrapping).")]
        public int GridColumns = 4;

        [Header("UI — Portraits & Names")]
        public Image P1Portrait;
        public Image P2Portrait;
        public TMP_Text P1NameLabel;
        public TMP_Text P2NameLabel;

        [Header("UI — Cursors")]
        [Tooltip("Parent transform whose children are the grid slots. " +
                 "Cursors will position themselves over the selected slot.")]
        public RectTransform GridContainer;

        [Tooltip("Visual cursor overlay for P1 (e.g. a colored border).")]
        public RectTransform P1Cursor;

        [Tooltip("Visual cursor overlay for P2.")]
        public RectTransform P2Cursor;

        [Header("UI — Super Art Selection")]
        [Tooltip("Panel that appears when a player picks a character. " +
                 "Should contain 3 child buttons/labels for the super arts.")]
        public GameObject P1SuperArtPanel;
        public GameObject P2SuperArtPanel;
        public TMP_Text[] P1SuperArtLabels;
        public TMP_Text[] P2SuperArtLabels;

        [Header("UI — Ready")]
        public GameObject ReadyBanner;

        [Header("UI — Join Prompt")]
        [Tooltip("Shown before both players have joined (e.g. 'Press any button to join').")]
        public GameObject JoinPrompt;

        [Header("Scene Transition")]
        [Tooltip("Name of the battle scene to load.")]
        public string BattleSceneName = "BattleScene";

        [Tooltip("Delay in seconds after both players lock in before loading.")]
        public float TransitionDelay = 1.5f;

        [Header("Audio")]
        public AudioClip JoinSound;
        public AudioClip CursorMoveSound;
        public AudioClip ConfirmSound;
        public AudioClip CancelSound;
        public AudioSource AudioSource;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private enum SelectPhase { NotJoined, Browsing, SuperArtSelect, Locked }

        private int[] _cursorIndex = new int[2];
        private SelectPhase[] _phase = new SelectPhase[2];
        private int[] _superArtCursor = new int[2];
        private CharacterData[] _selected = new CharacterData[2];

        private bool _transitioning;
        private int _playersJoined;

        // References to spawned player input handlers
        private CharacterSelectPlayer[] _inputHandlers = new CharacterSelectPlayer[2];

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            if (Roster == null || Roster.Length == 0) {
                Debug.LogError("[CharacterSelect] No characters in Roster!");
                return;
            }

            _phase[0] = SelectPhase.NotJoined;
            _phase[1] = SelectPhase.NotJoined;

            if (P1SuperArtPanel != null) P1SuperArtPanel.SetActive(false);
            if (P2SuperArtPanel != null) P2SuperArtPanel.SetActive(false);
            if (ReadyBanner != null) ReadyBanner.SetActive(false);
            if (JoinPrompt != null) JoinPrompt.SetActive(true);

            // Hide cursors until players join
            if (P1Cursor != null) P1Cursor.gameObject.SetActive(false);
            if (P2Cursor != null) P2Cursor.gameObject.SetActive(false);
        }

        // ──────────────────────────────────────
        //  PLAYER JOIN
        //  Wire PlayerInputManager's "Player Joined Event" to this.
        // ──────────────────────────────────────

        /// <summary>
        /// Called by PlayerInputManager when a player presses a button
        /// on their device to join. The spawned prefab must have a
        /// CharacterSelectPlayer component.
        /// </summary>
        public void OnPlayerJoined(PlayerInput playerInput) {
            int idx = playerInput.playerIndex;
            if (idx > 1) {
                Debug.LogWarning("[CharacterSelect] More than 2 players attempted to join.");
                Destroy(playerInput.gameObject);
                return;
            }

            // Get the input handler on the spawned prefab
            var handler = playerInput.GetComponent<CharacterSelectPlayer>();
            if (handler == null) {
                Debug.LogError("[CharacterSelect] Select player prefab missing CharacterSelectPlayer component.");
                return;
            }

            // Register this handler and link it back to us
            _inputHandlers[idx] = handler;
            handler.Initialize(this, idx);

            _phase[idx] = SelectPhase.Browsing;
            _playersJoined++;

            // Show this player's cursor
            RectTransform cursor = idx == 0 ? P1Cursor : P2Cursor;
            if (cursor != null) cursor.gameObject.SetActive(true);

            // Hide join prompt once both players are in
            if (_playersJoined >= 2 && JoinPrompt != null)
                JoinPrompt.SetActive(false);

            PlaySound(JoinSound);
            UpdateUI(idx);

            Debug.Log($"[CharacterSelect] Player {idx + 1} joined.");
        }

        // ──────────────────────────────────────
        //  INPUT RECEPTION
        //  Called by CharacterSelectPlayer when it receives input.
        // ──────────────────────────────────────

        /// <summary>
        /// Called by CharacterSelectPlayer each frame with the current stick value.
        /// </summary>
        public void OnPlayerNavigate(int playerIndex, Vector2 stick) {
            if (playerIndex < 0 || playerIndex > 1) return;
            if (_phase[playerIndex] == SelectPhase.NotJoined) return;

            HandleStickInput(playerIndex, stick);
        }

        /// <summary>
        /// Called by CharacterSelectPlayer on confirm button press.
        /// </summary>
        public void OnPlayerConfirm(int playerIndex) {
            if (playerIndex < 0 || playerIndex > 1) return;
            if (_transitioning) return;

            switch (_phase[playerIndex]) {
                case SelectPhase.Browsing:
                    ConfirmCharacter(playerIndex);
                    break;
                case SelectPhase.SuperArtSelect:
                    ConfirmSuperArt(playerIndex);
                    break;
            }
        }

        /// <summary>
        /// Called by CharacterSelectPlayer on cancel button press.
        /// </summary>
        public void OnPlayerCancel(int playerIndex) {
            if (playerIndex < 0 || playerIndex > 1) return;
            if (_transitioning) return;

            switch (_phase[playerIndex]) {
                case SelectPhase.SuperArtSelect:
                    CancelSuperArt(playerIndex);
                    break;
                case SelectPhase.Locked:
                    CancelLock(playerIndex);
                    break;
            }
        }

        // ──────────────────────────────────────
        //  STICK NAVIGATION
        // ──────────────────────────────────────

        // Prevents stick repeat: must return to neutral before next move
        private bool[] _stickConsumed = new bool[2];

        private void HandleStickInput(int player, Vector2 stick) {
            // Reset consumed flag when stick returns to neutral
            if (stick.magnitude < 0.3f) {
                _stickConsumed[player] = false;
                return;
            }

            if (_stickConsumed[player]) return;

            if (_phase[player] == SelectPhase.Browsing) {
                int dx = 0, dy = 0;
                if (stick.x > 0.5f) dx = 1;
                else if (stick.x < -0.5f) dx = -1;
                if (stick.y > 0.5f) dy = -1;   // up = previous row
                else if (stick.y < -0.5f) dy = 1; // down = next row

                int newIndex = _cursorIndex[player] + dx + (dy * GridColumns);
                newIndex = Mathf.Clamp(newIndex, 0, Roster.Length - 1);

                if (newIndex != _cursorIndex[player]) {
                    _cursorIndex[player] = newIndex;
                    PlaySound(CursorMoveSound);
                    UpdateUI(player);
                }

                _stickConsumed[player] = true;
            }
            else if (_phase[player] == SelectPhase.SuperArtSelect) {
                var superArts = _selected[player].Moveset.SuperArts;
                int dir = stick.x > 0.5f ? 1 : stick.x < -0.5f ? -1 : 0;

                if (dir != 0) {
                    _superArtCursor[player] = Mathf.Clamp(
                        _superArtCursor[player] + dir, 0, superArts.Length - 1);
                    PlaySound(CursorMoveSound);
                    UpdateSuperArtUI(player);
                    _stickConsumed[player] = true;
                }
            }
        }

        // ──────────────────────────────────────
        //  CONFIRM / CANCEL ACTIONS
        // ──────────────────────────────────────

        private void ConfirmCharacter(int player) {
            _selected[player] = Roster[_cursorIndex[player]];
            MatchSettings.SelectedCharacters[player] = _selected[player];
            PlaySound(ConfirmSound);

            // Check if this character has super arts to choose from
            if (_selected[player].Moveset != null
                && _selected[player].Moveset.SuperArts != null
                && _selected[player].Moveset.SuperArts.Length > 1) {
                _phase[player] = SelectPhase.SuperArtSelect;
                _superArtCursor[player] = 0;
                ShowSuperArtPanel(player);
            }
            else {
                // Only one or no super arts — skip selection
                _phase[player] = SelectPhase.Locked;
                MatchSettings.SelectedSuperArts[player] = 0;
                CheckBothReady();
            }

            UpdateUI(player);
        }

        private void ConfirmSuperArt(int player) {
            MatchSettings.SelectedSuperArts[player] = _superArtCursor[player];
            _phase[player] = SelectPhase.Locked;
            HideSuperArtPanel(player);
            PlaySound(ConfirmSound);
            UpdateUI(player);
            CheckBothReady();
        }

        private void CancelSuperArt(int player) {
            _phase[player] = SelectPhase.Browsing;
            _selected[player] = null;
            MatchSettings.SelectedCharacters[player] = null;
            HideSuperArtPanel(player);
            PlaySound(CancelSound);
            UpdateUI(player);
        }

        private void CancelLock(int player) {
            // If they had super art selection, go back there
            if (_selected[player].Moveset != null
                && _selected[player].Moveset.SuperArts != null
                && _selected[player].Moveset.SuperArts.Length > 1) {
                _phase[player] = SelectPhase.SuperArtSelect;
                ShowSuperArtPanel(player);
            }
            else {
                _phase[player] = SelectPhase.Browsing;
                _selected[player] = null;
                MatchSettings.SelectedCharacters[player] = null;
            }

            PlaySound(CancelSound);
            if (ReadyBanner != null) ReadyBanner.SetActive(false);
            UpdateUI(player);
        }

        // ──────────────────────────────────────
        //  READY CHECK & TRANSITION
        // ──────────────────────────────────────

        private void CheckBothReady() {
            if (_phase[0] != SelectPhase.Locked || _phase[1] != SelectPhase.Locked)
                return;

            // Handle mirror match palette conflict
            if (MatchSettings.SelectedCharacters[0] == MatchSettings.SelectedCharacters[1]) {
                MatchSettings.SelectedPalettes[0] = 0;
                MatchSettings.SelectedPalettes[1] = 1;
            }
            else {
                MatchSettings.SelectedPalettes[0] = 0;
                MatchSettings.SelectedPalettes[1] = 0;
            }

            if (ReadyBanner != null) ReadyBanner.SetActive(true);

            _transitioning = true;
            Invoke(nameof(LoadBattleScene), TransitionDelay);
        }

        private void LoadBattleScene() {
            // Destroy the spawned select-screen player objects
            // so they don't carry into the battle scene
            for (int i = 0; i < 2; i++) {
                if (_inputHandlers[i] != null)
                    Destroy(_inputHandlers[i].gameObject);
            }

            SceneManager.LoadScene(BattleSceneName);
        }

        // ──────────────────────────────────────
        //  UI UPDATES
        // ──────────────────────────────────────

        private void UpdateUI(int player) {
            if (_phase[player] == SelectPhase.NotJoined) return;

            CharacterData highlighted = Roster[_cursorIndex[player]];

            // Portrait
            Image portrait = player == 0 ? P1Portrait : P2Portrait;
            if (portrait != null && highlighted.Portrait != null)
                portrait.sprite = highlighted.Portrait;

            // Name
            TMP_Text nameLabel = player == 0 ? P1NameLabel : P2NameLabel;
            if (nameLabel != null)
                nameLabel.text = highlighted.CharacterName;

            // Cursor position — move it over the correct grid slot
            RectTransform cursor = player == 0 ? P1Cursor : P2Cursor;
            if (cursor != null && GridContainer != null
                && _cursorIndex[player] < GridContainer.childCount) {
                RectTransform slot = GridContainer.GetChild(_cursorIndex[player]) as RectTransform;
                if (slot != null)
                    cursor.position = slot.position;
            }

            // Lock-in visual feedback
            if (cursor != null) {
                var img = cursor.GetComponent<Image>();
                if (img != null) {
                    Color c = img.color;
                    c.a = _phase[player] == SelectPhase.Locked ? 1f : 0.6f;
                    img.color = c;
                }
            }
        }

        private void ShowSuperArtPanel(int player) {
            GameObject panel = player == 0 ? P1SuperArtPanel : P2SuperArtPanel;
            if (panel != null) panel.SetActive(true);
            UpdateSuperArtUI(player);
        }

        private void HideSuperArtPanel(int player) {
            GameObject panel = player == 0 ? P1SuperArtPanel : P2SuperArtPanel;
            if (panel != null) panel.SetActive(false);
        }

        private void UpdateSuperArtUI(int player) {
            if (_selected[player]?.Moveset?.SuperArts == null) return;

            TMP_Text[] labels = player == 0 ? P1SuperArtLabels : P2SuperArtLabels;
            var superArts = _selected[player].Moveset.SuperArts;

            if (labels == null) return;

            for (int i = 0; i < labels.Length; i++) {
                if (labels[i] == null) continue;

                if (i < superArts.Length) {
                    labels[i].text = superArts[i].Name;
                    labels[i].fontStyle = (i == _superArtCursor[player])
                        ? FontStyles.Bold | FontStyles.Underline
                        : FontStyles.Normal;
                    labels[i].color = (i == _superArtCursor[player])
                        ? Color.yellow
                        : Color.white;
                }
                else {
                    labels[i].text = "";
                }
            }
        }

        // ──────────────────────────────────────
        //  AUDIO
        // ──────────────────────────────────────

        private void PlaySound(AudioClip clip) {
            if (clip != null && AudioSource != null)
                AudioSource.PlayOneShot(clip);
        }
    }
}