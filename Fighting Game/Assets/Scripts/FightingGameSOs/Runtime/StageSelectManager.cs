using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Manages the stage select screen. P1 navigates and confirms the stage;
    /// P2 can see the preview but cannot steer.
    ///
    /// PLAYER SPAWNING:
    ///   Like MatchManager, this scene does NOT rely on PlayerInputManager's
    ///   join behavior. Instead, it manually spawns StageSelectPlayer prefabs
    ///   on Start and pairs each with the correct device using
    ///   MatchSettings.PlayerDeviceIds (saved during character select).
    ///   This guarantees consistent P1/P2 assignment across scenes.
    ///
    /// Flow:
    ///   1. Scene loads → both players are spawned with their devices.
    ///   2. P1 navigates the stage grid with stick/dpad.
    ///   3. Confirm locks in the stage, starts the transition to battle.
    ///   4. Cancel returns to the character select screen.
    ///
    /// Setup:
    ///   - Attach to a GameObject (no PlayerInputManager needed).
    ///   - Assign the StageSelectPlayerPrefab in the inspector.
    ///   - Fill the Stages array with every selectable StageData.
    /// </summary>
    public class StageSelectManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Player Prefab")]
        [Tooltip("Prefab with PlayerInput + StageSelectPlayer. Spawned for each player on Start.")]
        public GameObject StageSelectPlayerPrefab;

        [Header("Stage Roster")]
        [Tooltip("All selectable stages, in grid order.")]
        public StageData[] Stages;

        [Tooltip("How many columns in the stage grid (for cursor wrapping).")]
        public int GridColumns = 2;

        [Header("UI — Preview")]
        [Tooltip("Large preview image that updates as P1 navigates.")]
        public Image PreviewImage;

        [Tooltip("Stage name label.")]
        public TMP_Text StageNameLabel;

        [Header("UI — Grid")]
        [Tooltip("Parent transform whose children are the StageSelectSlot objects.")]
        public RectTransform GridContainer;

        [Tooltip("Visual cursor overlay for the currently highlighted slot.")]
        public RectTransform StageCursor;

        [Header("UI — Status")]
        [Tooltip("Shown briefly after confirming, before scene loads.")]
        public GameObject ReadyBanner;

        [Header("Scene Transition")]
        [Tooltip("Name of the battle scene to load after stage is selected.")]
        public string BattleSceneName = "BattleScene";

        [Tooltip("Name of the character select scene (for cancel / back).")]
        public string CharacterSelectSceneName = "CharacterSelectScene";

        [Tooltip("Delay in seconds after confirming before loading the battle.")]
        public float TransitionDelay = 1.5f;

        [Header("Audio")]
        public AudioClip CursorMoveSound;
        public AudioClip ConfirmSound;
        public AudioClip CancelSound;
        public AudioSource AudioSource;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private int _cursorIndex;
        private bool _confirmed;
        private bool _transitioning;

        // References to spawned input handlers
        private StageSelectPlayer[] _inputHandlers = new StageSelectPlayer[2];
        private GameObject[] _spawnedPlayers = new GameObject[2];

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            if (Stages == null || Stages.Length == 0) {
                Debug.LogError("[StageSelect] No stages in array!");
                return;
            }

            _cursorIndex = 0;
            _confirmed = false;
            _transitioning = false;

            if (ReadyBanner != null) ReadyBanner.SetActive(false);

            SpawnPlayers();
            UpdateUI();
        }

        // ──────────────────────────────────────
        //  PLAYER SPAWNING
        // ──────────────────────────────────────

        /// <summary>
        /// Manually spawns both players with their tracked devices.
        /// </summary>
        private void SpawnPlayers() {
            if (StageSelectPlayerPrefab == null) {
                Debug.LogError("[StageSelect] StageSelectPlayerPrefab is not assigned!");
                return;
            }

            for (int idx = 0; idx < 2; idx++) {
                InputDevice device = FindDeviceForPlayer(idx);

                GameObject playerObj;
                if (device != null) {
                    playerObj = PlayerInput.Instantiate(
                        StageSelectPlayerPrefab,
                        playerIndex: idx,
                        pairWithDevice: device
                    ).gameObject;
                }
                else {
                    playerObj = Instantiate(StageSelectPlayerPrefab);
                    Debug.LogWarning($"[StageSelect] No tracked device for P{idx + 1}.");
                }

                playerObj.name = $"StageSelectPlayer_P{idx + 1}";
                _spawnedPlayers[idx] = playerObj;

                var handler = playerObj.GetComponent<StageSelectPlayer>();
                if (handler != null) {
                    _inputHandlers[idx] = handler;
                    handler.Initialize(this, idx);
                }

                Debug.Log($"[StageSelect] Player {idx + 1} spawned for stage select.");
            }
        }

        private InputDevice FindDeviceForPlayer(int playerIndex) {
            int deviceId = MatchSettings.PlayerDeviceIds[playerIndex];
            if (deviceId == 0) return null;

            foreach (var device in InputSystem.devices) {
                if (device.deviceId == deviceId)
                    return device;
            }

            return null;
        }

        // ──────────────────────────────────────
        //  INPUT RECEPTION
        //  Called by StageSelectPlayer when it receives input.
        //  Only P1 (playerIndex 0) can navigate and confirm.
        //  Either player can cancel (returns to char select).
        // ──────────────────────────────────────

        public void OnPlayerNavigate(int playerIndex, Vector2 stick) {
            // Only P1 navigates
            if (playerIndex != 0) return;
            if (_confirmed) return;

            HandleStickInput(stick);
        }

        public void OnPlayerConfirm(int playerIndex) {
            // Only P1 confirms
            if (playerIndex != 0) return;
            if (_confirmed || _transitioning) return;

            ConfirmStage();
        }

        public void OnPlayerCancel(int playerIndex) {
            if (_transitioning) return;

            if (_confirmed) {
                // Undo the stage lock
                CancelConfirm();
            }
            else {
                // Go back to character select
                GoBackToCharacterSelect();
            }
        }

        // ──────────────────────────────────────
        //  STICK NAVIGATION
        // ──────────────────────────────────────

        private bool _stickConsumed;

        private void HandleStickInput(Vector2 stick) {
            if (stick.magnitude < 0.3f) {
                _stickConsumed = false;
                return;
            }

            if (_stickConsumed) return;

            int dx = 0, dy = 0;
            if (stick.x > 0.5f) dx = 1;
            else if (stick.x < -0.5f) dx = -1;
            if (stick.y > 0.5f) dy = -1;  // up = previous row
            else if (stick.y < -0.5f) dy = 1;  // down = next row

            int newIndex = _cursorIndex + dx + (dy * GridColumns);
            newIndex = Mathf.Clamp(newIndex, 0, Stages.Length - 1);

            if (newIndex != _cursorIndex) {
                _cursorIndex = newIndex;
                PlaySound(CursorMoveSound);
                UpdateUI();
            }

            _stickConsumed = true;
        }

        // ──────────────────────────────────────
        //  CONFIRM / CANCEL
        // ──────────────────────────────────────

        private void ConfirmStage() {
            _confirmed = true;
            MatchSettings.SelectedStage = Stages[_cursorIndex];
            PlaySound(ConfirmSound);

            if (ReadyBanner != null) ReadyBanner.SetActive(true);

            _transitioning = true;
            Invoke(nameof(LoadBattleScene), TransitionDelay);
        }

        private void CancelConfirm() {
            _confirmed = false;
            _transitioning = false;
            CancelInvoke(nameof(LoadBattleScene));
            MatchSettings.SelectedStage = null;
            PlaySound(CancelSound);

            if (ReadyBanner != null) ReadyBanner.SetActive(false);
        }

        private void GoBackToCharacterSelect() {
            PlaySound(CancelSound);
            CleanupInputHandlers();
            SceneManager.LoadScene(CharacterSelectSceneName);
        }

        private void LoadBattleScene() {
            CleanupInputHandlers();
            SceneManager.LoadScene(BattleSceneName);
        }

        private void CleanupInputHandlers() {
            for (int i = 0; i < 2; i++) {
                if (_spawnedPlayers[i] != null)
                    Destroy(_spawnedPlayers[i]);
            }
        }

        // ──────────────────────────────────────
        //  UI UPDATES
        // ──────────────────────────────────────

        private void UpdateUI() {
            if (Stages == null || _cursorIndex >= Stages.Length) return;

            StageData stage = Stages[_cursorIndex];

            // Preview image
            if (PreviewImage != null && stage.PreviewImage != null)
                PreviewImage.sprite = stage.PreviewImage;

            // Name
            if (StageNameLabel != null)
                StageNameLabel.text = stage.StageName;

            // Cursor position — move it over the correct grid slot
            if (StageCursor != null && GridContainer != null
                && _cursorIndex < GridContainer.childCount) {
                RectTransform slot = GridContainer.GetChild(_cursorIndex) as RectTransform;
                if (slot != null)
                    StageCursor.position = slot.position;
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