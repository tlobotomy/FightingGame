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
    ///   1. Both players navigate a grid of characters with their stick/dpad
    ///   2. A button press locks in the character
    ///   3. Super art selection appears for each locked player
    ///   4. Once both are locked with super arts chosen, transition to battle
    ///
    /// Supports both players on the same screen. P1 uses one input device,
    /// P2 uses another (or keyboard left/right halves).
    ///
    /// Setup:
    ///   - Attach to a GameObject in the character select scene
    ///   - Requires a PlayerInputManager if you want auto-join,
    ///     OR you can use two pre-placed PlayerInput components
    ///   - Fill the Roster array with every selectable CharacterData
    /// </summary>
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

        [Header("Scene Transition")]
        [Tooltip("Name of the battle scene to load.")]
        public string BattleSceneName = "BattleScene";

        [Tooltip("Delay in seconds after both players lock in before loading.")]
        public float TransitionDelay = 1.5f;

        [Header("Audio")]
        public AudioClip CursorMoveSound;
        public AudioClip ConfirmSound;
        public AudioClip CancelSound;
        public AudioSource AudioSource;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private enum SelectPhase { Browsing, SuperArtSelect, Locked }

        private int[] _cursorIndex = new int[2];
        private SelectPhase[] _phase = new SelectPhase[2];
        private int[] _superArtCursor = new int[2];
        private CharacterData[] _selected = new CharacterData[2];

        private bool _transitioning;

        // Input tracking — we read sticks manually each frame
        // because we're not using PlayerController here.
        private Vector2[] _rawStick = new Vector2[2];
        private bool[] _confirmPressed = new bool[2];
        private bool[] _cancelPressed = new bool[2];

        // Prevents stick repeat: must return to neutral before next move
        private bool[] _stickConsumed = new bool[2];

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            if (Roster == null || Roster.Length == 0) {
                Debug.LogError("[CharacterSelect] No characters in Roster!");
                return;
            }

            _phase[0] = SelectPhase.Browsing;
            _phase[1] = SelectPhase.Browsing;

            if (P1SuperArtPanel != null) P1SuperArtPanel.SetActive(false);
            if (P2SuperArtPanel != null) P2SuperArtPanel.SetActive(false);
            if (ReadyBanner != null) ReadyBanner.SetActive(false);

            UpdateUI(0);
            UpdateUI(1);
        }

        // ──────────────────────────────────────
        //  INPUT CALLBACKS
        //  Wire these on two PlayerInput components
        //  (one per player), or use a PlayerInputManager.
        // ──────────────────────────────────────

        /// <summary>
        /// Called by PlayerInput for each player. The playerIndex
        /// determines which player (0 or 1) the input belongs to.
        /// </summary>
        public void OnNavigate(InputAction.CallbackContext ctx) {
            int idx = GetPlayerIndex(ctx);
            if (idx < 0) return;

            _rawStick[idx] = ctx.ReadValue<Vector2>();

            // Reset consumed flag when stick returns to neutral
            if (_rawStick[idx].magnitude < 0.3f)
                _stickConsumed[idx] = false;
        }

        public void OnConfirm(InputAction.CallbackContext ctx) {
            int idx = GetPlayerIndex(ctx);
            if (idx < 0) return;
            if (ctx.started) _confirmPressed[idx] = true;
        }

        public void OnCancel(InputAction.CallbackContext ctx) {
            int idx = GetPlayerIndex(ctx);
            if (idx < 0) return;
            if (ctx.started) _cancelPressed[idx] = true;
        }

        private int GetPlayerIndex(InputAction.CallbackContext ctx) {
            var pi = ctx.action.actionMap?.FindAction("Navigate")?.actionMap;
            // Fallback: use PlayerInput component's playerIndex
            var playerInput = ctx.action.actionMap?.asset?.name;
            // Simplest approach: check which PlayerInput sent this
            var inputs = FindObjectsOfType<PlayerInput>();
            foreach (var input in inputs) {
                if (input.currentActionMap == ctx.action.actionMap)
                    return input.playerIndex;
            }
            return -1;
        }

        // ──────────────────────────────────────
        //  UPDATE LOOP
        // ──────────────────────────────────────

        private void Update() {
            if (_transitioning) return;

            for (int i = 0; i < 2; i++) {
                switch (_phase[i]) {
                    case SelectPhase.Browsing:
                        HandleBrowsing(i);
                        break;
                    case SelectPhase.SuperArtSelect:
                        HandleSuperArtSelect(i);
                        break;
                    case SelectPhase.Locked:
                        HandleLocked(i);
                        break;
                }
            }

            // Reset per-frame input flags
            _confirmPressed[0] = false;
            _confirmPressed[1] = false;
            _cancelPressed[0] = false;
            _cancelPressed[1] = false;
        }

        // ──────────────────────────────────────
        //  PHASE: BROWSING (moving cursor over roster)
        // ──────────────────────────────────────

        private void HandleBrowsing(int player) {
            // Stick navigation
            if (!_stickConsumed[player] && _rawStick[player].magnitude > 0.5f) {
                int dx = 0, dy = 0;
                if (_rawStick[player].x > 0.5f) dx = 1;
                else if (_rawStick[player].x < -0.5f) dx = -1;
                if (_rawStick[player].y > 0.5f) dy = -1;   // up = previous row
                else if (_rawStick[player].y < -0.5f) dy = 1; // down = next row

                int newIndex = _cursorIndex[player] + dx + (dy * GridColumns);
                newIndex = Mathf.Clamp(newIndex, 0, Roster.Length - 1);

                if (newIndex != _cursorIndex[player]) {
                    _cursorIndex[player] = newIndex;
                    PlaySound(CursorMoveSound);
                    UpdateUI(player);
                }

                _stickConsumed[player] = true;
            }

            // Confirm → lock in character, move to super art select
            if (_confirmPressed[player]) {
                _selected[player] = Roster[_cursorIndex[player]];
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
                }

                MatchSettings.SelectedCharacters[player] = _selected[player];
                UpdateUI(player);
                CheckBothReady();
            }
        }

        // ──────────────────────────────────────
        //  PHASE: SUPER ART SELECT
        // ──────────────────────────────────────

        private void HandleSuperArtSelect(int player) {
            var superArts = _selected[player].Moveset.SuperArts;

            // Navigate between super art options
            if (!_stickConsumed[player] && Mathf.Abs(_rawStick[player].x) > 0.5f) {
                int dir = _rawStick[player].x > 0 ? 1 : -1;
                _superArtCursor[player] = Mathf.Clamp(
                    _superArtCursor[player] + dir, 0, superArts.Length - 1);
                PlaySound(CursorMoveSound);
                UpdateSuperArtUI(player);
                _stickConsumed[player] = true;
            }

            // Confirm super art
            if (_confirmPressed[player]) {
                MatchSettings.SelectedSuperArts[player] = _superArtCursor[player];
                _phase[player] = SelectPhase.Locked;
                PlaySound(ConfirmSound);
                UpdateUI(player);
                CheckBothReady();
            }

            // Cancel → go back to browsing
            if (_cancelPressed[player]) {
                _phase[player] = SelectPhase.Browsing;
                _selected[player] = null;
                MatchSettings.SelectedCharacters[player] = null;
                HideSuperArtPanel(player);
                PlaySound(CancelSound);
                UpdateUI(player);
            }
        }

        // ──────────────────────────────────────
        //  PHASE: LOCKED (waiting for the other player)
        // ──────────────────────────────────────

        private void HandleLocked(int player) {
            // Cancel → unlock and go back
            if (_cancelPressed[player]) {
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
            SceneManager.LoadScene(BattleSceneName);
        }

        // ──────────────────────────────────────
        //  UI UPDATES
        // ──────────────────────────────────────

        private void UpdateUI(int player) {
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
                    // Brighter/thicker when locked
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
                    // Highlight the currently selected one
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