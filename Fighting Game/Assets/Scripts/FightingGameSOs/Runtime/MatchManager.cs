using UnityEngine;
using UnityEngine.InputSystem;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Central game manager for a single match. Owns both players,
    /// runs the deterministic fixed-timestep loop, and resolves
    /// all physics (pushboxes) and combat (hitboxes vs hurtboxes)
    /// AFTER both players have acted each frame.
    ///
    /// GGXX HITSTOP MODEL:
    ///   Hitstop is per-player, not global. On hit:
    ///     - Attacker freezes for FrameData.GetAttackerHitstop() frames.
    ///     - Defender freezes for FrameData.GetDefenderHitstop() (on hit)
    ///       or FrameData.GetDefenderBlockstop() (on block) frames.
    ///   Each player's hitstop is managed by PlayerController.ApplyHitstop().
    ///   The game loop still ticks every frame; each PlayerController
    ///   checks its own hitstop counter and early-returns if frozen.
    ///
    /// PLAYER SPAWNING:
    ///   Unlike the menu scenes, the battle scene does NOT use a
    ///   PlayerInputManager. Instead, MatchManager manually instantiates
    ///   both BattlePlayer prefabs on Start and pairs each with the
    ///   correct input device using MatchSettings.PlayerDeviceIds.
    ///   This guarantees P1/P2 assignment is consistent with character
    ///   select regardless of device join order.
    ///
    /// Setup:
    ///   - Place on a GameObject in the battle scene (no PlayerInputManager needed).
    ///   - Assign the BattlePlayerPrefab in the inspector.
    ///   - The prefab must have: PlayerInput, InputDetector, PlayerController.
    /// </summary>
    public class MatchManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Player Prefab")]
        [Tooltip("The universal battle player prefab (PlayerInput + InputDetector + PlayerController).")]
        public GameObject BattlePlayerPrefab;

        [Header("Fallback Characters (for testing without CharSelect)")]
        [Tooltip("If MatchSettings has no character for P1, use this. Leave null to require CharSelect flow.")]
        public CharacterData FallbackP1Character;
        public CharacterData FallbackP2Character;

        [Header("Stage (fallback — overridden by MatchSettings.SelectedStage)")]
        [Tooltip("Left boundary. Overridden by StageData if a stage was selected.")]
        public float StageLeftBound = -6f;

        [Tooltip("Right boundary. Overridden by StageData if a stage was selected.")]
        public float StageRightBound = 6f;

        [Tooltip("Ground Y position. Overridden by StageData if a stage was selected.")]
        public float GroundY = 0f;

        [Header("Spawn Points")]
        [Tooltip("Where P1 and P2 appear. Element 0 = P1, Element 1 = P2.")]
        public Transform[] SpawnPoints;

        [Header("Round Settings")]
        [Tooltip("Round timer in seconds (99 standard). Overridden by StageData.RoundTimeOverride if > 0.")]
        public int RoundTimeSeconds = 99;

        [Header("Stage Visuals")]
        [Tooltip("Parent transform where the stage background prefab is instantiated.")]
        public Transform BackgroundParent;

        [Header("Stage Audio")]
        [Tooltip("AudioSource used for BGM. If null, one is created automatically.")]
        public AudioSource BGMSource;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private PlayerController[] _players = new PlayerController[2];
        private InputDetector[] _detectors = new InputDetector[2];
        private int _gameFrame;

        // Per-frame hit tracking: prevents the same move from hitting
        // multiple times on a single active frame.
        private bool[] _hasHitThisFrame = new bool[2];

        // Track which moves have already connected (for multi-active-frame
        // moves that should only hit once unless they're multi-hit).
        private MoveData[] _lastMoveHit = new MoveData[2];

        /// <summary>Read-only access to players for UI/camera.</summary>
        public PlayerController GetPlayer(int index) => _players[index];
        public int GameFrame => _gameFrame;

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            Time.fixedDeltaTime = 1f / 60f;
            ApplyStageData();
            SpawnPlayers();
        }

        /// <summary>
        /// Reads MatchSettings.SelectedStage and applies bounds, background,
        /// and BGM. Falls back to inspector values if no stage was selected
        /// (useful for testing the battle scene in isolation).
        /// </summary>
        private void ApplyStageData() {
            StageData stage = MatchSettings.SelectedStage;
            if (stage == null) return;

            // Apply bounds
            StageLeftBound = stage.LeftBound;
            StageRightBound = stage.RightBound;
            GroundY = stage.GroundY;

            // Apply round time override
            if (stage.RoundTimeOverride > 0)
                RoundTimeSeconds = stage.RoundTimeOverride;

            // Instantiate background prefab
            if (stage.BackgroundPrefab != null) {
                Transform parent = BackgroundParent != null ? BackgroundParent : transform;
                Instantiate(stage.BackgroundPrefab, parent);
            }

            // Play BGM
            if (stage.BGM != null) {
                if (BGMSource == null) {
                    BGMSource = gameObject.AddComponent<AudioSource>();
                    BGMSource.playOnAwake = false;
                }

                BGMSource.clip = stage.BGM;
                BGMSource.loop = stage.BGMLoop;
                BGMSource.volume = stage.BGMVolume;
                BGMSource.Play();
            }
        }

        // ──────────────────────────────────────
        //  PLAYER SPAWNING
        // ──────────────────────────────────────

        /// <summary>
        /// Manually instantiates both players and pairs each with the
        /// input device they used in character select. No PlayerInputManager
        /// needed — devices are assigned directly via PlayerInput API.
        /// </summary>
        private void SpawnPlayers() {
            if (BattlePlayerPrefab == null) {
                Debug.LogError("[MatchManager] BattlePlayerPrefab is not assigned!");
                return;
            }

            for (int idx = 0; idx < 2; idx++) {
                // Find the device this player used in character select
                InputDevice device = FindDeviceForPlayer(idx);

                // Instantiate the prefab
                GameObject playerObj;
                if (device != null) {
                    // Spawn with specific device pairing — this creates the
                    // PlayerInput and immediately assigns the device to it.
                    playerObj = PlayerInput.Instantiate(
                        BattlePlayerPrefab,
                        playerIndex: idx,
                        pairWithDevice: device
                    ).gameObject;
                }
                else {
                    // No device tracked (testing battle scene in isolation).
                    // Just instantiate normally — player won't have input
                    // unless a device is available.
                    playerObj = Instantiate(BattlePlayerPrefab);
                    Debug.LogWarning($"[MatchManager] No tracked device for P{idx + 1}. " +
                        "Input may not work. Go through CharSelect to pair devices.");
                }

                playerObj.name = $"BattlePlayer_P{idx + 1}";
                SetupPlayer(idx, playerObj);
            }
        }

        /// <summary>
        /// Finds the InputDevice that matches the deviceId stored in
        /// MatchSettings during character select.
        /// </summary>
        private InputDevice FindDeviceForPlayer(int playerIndex) {
            int deviceId = MatchSettings.PlayerDeviceIds[playerIndex];
            if (deviceId == 0) return null; // not set

            foreach (var device in InputSystem.devices) {
                if (device.deviceId == deviceId)
                    return device;
            }

            Debug.LogWarning($"[MatchManager] Device ID {deviceId} for P{playerIndex + 1} " +
                "not found. It may have been disconnected.");
            return null;
        }

        /// <summary>
        /// Configures a spawned player object: assigns character data,
        /// wires input, sets facing, positions at spawn point, and
        /// instantiates the character visual prefab.
        /// </summary>
        private void SetupPlayer(int idx, GameObject playerObj) {
            var playerInput = playerObj.GetComponent<PlayerInput>();
            var controller = playerObj.GetComponent<PlayerController>();
            var detector = playerObj.GetComponent<InputDetector>();

            if (controller == null || detector == null) {
                Debug.LogError("[MatchManager] BattlePlayerPrefab missing PlayerController or InputDetector.");
                return;
            }

            _players[idx] = controller;
            _detectors[idx] = detector;

            // Wire input events (Invoke C# Events mode)
            if (playerInput != null)
                WireInputEvents(playerInput, detector);

            // Apply character from MatchSettings, or fall back to inspector defaults
            CharacterData character = MatchSettings.SelectedCharacters[idx];
            if (character == null)
                character = idx == 0 ? FallbackP1Character : FallbackP2Character;

            if (character != null)
                controller.Character = character;

            Debug.Log($"[MatchManager] P{idx + 1} — Character: " +
                $"{(controller.Character != null ? controller.Character.CharacterName : "NULL")}, " +
                $"Device: {(playerInput != null && playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "none")}");

            // P1 faces right, P2 faces left
            detector.FacingSign = (idx == 0) ? 1 : -1;

            // Position at spawn point
            if (SpawnPoints != null && idx < SpawnPoints.Length && SpawnPoints[idx] != null)
                controller.transform.position = SpawnPoints[idx].position;

            controller.Initialize();

            // Spawn character visual prefab as child
            if (controller.Character != null && controller.Character.CharacterPrefab != null) {
                var visual = Instantiate(controller.Character.CharacterPrefab, controller.transform);

                var animator = visual.GetComponentInChildren<Animator>();
                var audioSource = visual.GetComponentInChildren<AudioSource>();

                if (audioSource == null)
                    audioSource = visual.AddComponent<AudioSource>();

                controller.SetVisualReferences(animator, audioSource);

                // Apply palette swap
                int palette = MatchSettings.SelectedPalettes[idx];
                if (controller.Character.ColorPalettes != null
                    && palette < controller.Character.ColorPalettes.Length) {
                    var renderers = visual.GetComponentsInChildren<SpriteRenderer>();
                    foreach (var renderer in renderers)
                        renderer.material = controller.Character.ColorPalettes[palette];
                }
            }

            Debug.Log($"[MatchManager] Player {idx + 1} ready.");
        }

        // ──────────────────────────────────────
        //  INPUT EVENT WIRING
        // ──────────────────────────────────────

        /// <summary>
        /// Subscribes InputDetector callbacks to PlayerInput action events.
        /// Called once per player during setup. Action names must match your
        /// Input Action Asset's Gameplay map (Move, Punch, Kick, Slash,
        /// HeavySlash, Dust).
        /// </summary>
        private void WireInputEvents(PlayerInput playerInput, InputDetector detector) {
            var actions = playerInput.actions;
            if (actions == null) {
                Debug.LogError("[MatchManager] PlayerInput has no action asset assigned.");
                return;
            }

            // Helper: find action, subscribe if it exists
            void Bind(string actionName, System.Action<InputAction.CallbackContext> callback) {
                var action = actions.FindAction(actionName);
                if (action != null) {
                    action.started += callback;
                    action.performed += callback;
                    action.canceled += callback;
                }
                else {
                    Debug.LogWarning($"[MatchManager] Action '{actionName}' not found in input asset.");
                }
            }

            Bind("Move", detector.OnMove);
            Bind("Punch", detector.OnPunch);
            Bind("Kick", detector.OnKick);
            Bind("Slash", detector.OnSlash);
            Bind("HeavySlash", detector.OnHeavySlash);
            Bind("Dust", detector.OnDust);
        }

        // ──────────────────────────────────────
        //  FIXED UPDATE — THE GAME LOOP
        // ──────────────────────────────────────

        private void FixedUpdate() {
            if (_players[0] == null || _players[1] == null) return;

            _gameFrame++;

            // --- RESET PER-FRAME HIT TRACKING ---
            _hasHitThisFrame[0] = false;
            _hasHitThisFrame[1] = false;

            // --- TICK BOTH PLAYERS ---
            // Each player's GameTick checks their own hitstop counter.
            // If in hitstop, they freeze but still buffer input.
            _players[0].GameTick();
            _players[1].GameTick();

            // --- PUSHBOX RESOLUTION (before hitboxes) ---
            // Only resolve if neither player is in hitstop (positions shouldn't
            // change during freeze). Both frozen = skip. One frozen = skip
            // (the non-frozen one might walk but pushbox push during hitstop
            // looks wrong visually).
            if (!_players[0].InHitstop && !_players[1].InHitstop)
                ResolvePushboxes();

            // --- HITBOX vs HURTBOX ---
            ResolveHitboxes();

            // --- UPDATE FACING ---
            UpdateFacing();

            // --- CLAMP TO STAGE ---
            ClampToStageBounds();
        }

        // ──────────────────────────────────────
        //  PUSHBOX RESOLUTION
        // ──────────────────────────────────────

        private void ResolvePushboxes() {
            Vector2 pos0 = _players[0].transform.position;
            Vector2 pos1 = _players[1].transform.position;

            Rect box0 = _players[0].Character.Pushbox.GetWorldRect(pos0, _players[0].FacingSign);
            Rect box1 = _players[1].Character.Pushbox.GetWorldRect(pos1, _players[1].FacingSign);

            if (!box0.Overlaps(box1)) return;

            float overlapX;
            if (box0.center.x < box1.center.x)
                overlapX = box0.xMax - box1.xMin;
            else
                overlapX = box1.xMax - box0.xMin;

            if (overlapX <= 0f) return;

            float halfPush = overlapX / 2f;
            float sign = pos0.x <= pos1.x ? -1f : 1f;

            _players[0].transform.position += new Vector3(sign * halfPush, 0, 0);
            _players[1].transform.position += new Vector3(-sign * halfPush, 0, 0);
        }

        // ──────────────────────────────────────
        //  HITBOX vs HURTBOX RESOLUTION
        // ──────────────────────────────────────

        private void ResolveHitboxes() {
            for (int attacker = 0; attacker < 2; attacker++) {
                int defender = 1 - attacker;
                ResolveAttack(attacker, defender);
            }
        }

        private void ResolveAttack(int attackerIdx, int defenderIdx) {
            var atk = _players[attackerIdx];
            var def = _players[defenderIdx];

            // Only check if the attacker is in Active phase and NOT in hitstop
            if (atk.State != PlayerController.PlayerState.Active) return;
            if (atk.InHitstop) return;
            if (atk.CurrentMove == null) return;

            // Don't hit again if this move already connected (single-hit moves)
            if (_lastMoveHit[attackerIdx] == atk.CurrentMove && atk.CurrentMove.HitCount <= 1)
                return;

            Rect[] hitRects = GetActiveHitboxRects(atk);
            if (hitRects == null || hitRects.Length == 0) return;

            Rect[] hurtRects = GetActiveHurtboxRects(def);
            if (hurtRects == null || hurtRects.Length == 0) return;

            HurtboxLayout defLayout = GetActiveHurtboxLayout(def);
            if (defLayout.Invincible) return;

            // Check overlap
            bool hit = false;
            foreach (var hitRect in hitRects) {
                foreach (var hurtRect in hurtRects) {
                    if (hitRect.Overlaps(hurtRect)) {
                        hit = true;
                        break;
                    }
                }
                if (hit) break;
            }

            if (!hit) return;

            // --- HIT CONFIRMED ---

            bool blocked = IsBlocking(def, atk.CurrentMove);
            bool parried = IsParrying(def, atk.CurrentMove);

            if (parried) {
                HandleParry(attackerIdx, defenderIdx, atk.CurrentMove);
                return;
            }

            // Apply hit/block to defender
            def.TakeHit(atk.CurrentMove, blocked);

            // --- GGXX PER-PLAYER HITSTOP ---
            FrameData frames = atk.CurrentMove.Frames;
            int attackerHitstop = frames.GetAttackerHitstop();
            int defenderHitstop = blocked
                ? frames.GetDefenderBlockstop()
                : frames.GetDefenderHitstop();

            atk.ApplyHitstop(attackerHitstop);
            def.ApplyHitstop(defenderHitstop);

            // Grant meter to attacker
            if (blocked)
                atk.AddMeter(atk.CurrentMove.Damage.MeterGainOnHit / 2);
            else
                atk.AddMeter(atk.CurrentMove.Damage.MeterGainOnHit);

            // Track that this move has connected
            _lastMoveHit[attackerIdx] = atk.CurrentMove;
            _hasHitThisFrame[attackerIdx] = true;
        }

        // ──────────────────────────────────────
        //  HITBOX / HURTBOX RECT EXTRACTION
        // ──────────────────────────────────────

        private Rect[] GetActiveHitboxRects(PlayerController player) {
            MoveData move = player.CurrentMove;
            if (move.HitboxFrames == null || move.HitboxFrames.Length == 0)
                return null;

            int activeFrame = GetMoveActiveFrame(player);
            if (activeFrame < 0) return null;

            Vector2 pos = player.transform.position;
            int facing = player.FacingSign;

            foreach (var hbf in move.HitboxFrames) {
                if (activeFrame >= hbf.StartFrame && activeFrame <= hbf.EndFrame) {
                    if (hbf.Hitboxes == null) continue;

                    Rect[] rects = new Rect[hbf.Hitboxes.Length];
                    for (int i = 0; i < hbf.Hitboxes.Length; i++)
                        rects[i] = hbf.Hitboxes[i].GetWorldRect(pos, facing);
                    return rects;
                }
            }

            return null;
        }

        private Rect[] GetActiveHurtboxRects(PlayerController player) {
            HurtboxLayout layout = GetActiveHurtboxLayout(player);
            if (layout.Hurtboxes == null || layout.Hurtboxes.Length == 0)
                return null;

            Vector2 pos = player.transform.position;
            int facing = player.FacingSign;

            Rect[] rects = new Rect[layout.Hurtboxes.Length];
            for (int i = 0; i < layout.Hurtboxes.Length; i++)
                rects[i] = layout.Hurtboxes[i].GetWorldRect(pos, facing);
            return rects;
        }

        private HurtboxLayout GetActiveHurtboxLayout(PlayerController player) {
            if (player.CurrentMove != null
                && player.CurrentMove.HurtboxOverrides != null
                && player.CurrentMove.HurtboxOverrideFrameRanges != null) {
                int activeFrame = GetMoveActiveFrame(player);
                for (int i = 0; i < player.CurrentMove.HurtboxOverrides.Length; i++) {
                    if (i >= player.CurrentMove.HurtboxOverrideFrameRanges.Length) break;
                    var range = player.CurrentMove.HurtboxOverrideFrameRanges[i];
                    if (activeFrame >= range.x && activeFrame <= range.y)
                        return player.CurrentMove.HurtboxOverrides[i];
                }
            }

            switch (player.State) {
                case PlayerController.PlayerState.Crouching:
                    return player.Character.CrouchingHurtbox;
                case PlayerController.PlayerState.Airborne:
                case PlayerController.PlayerState.PreJump:
                    return player.Character.AirborneHurtbox;
                default:
                    return player.Character.StandingHurtbox;
            }
        }

        /// <summary>
        /// Calculates which frame of the active phase the move is on.
        /// FirstActiveFrame = Startup (startup does not include first active frame).
        /// Returns -1 if the move is not in active phase.
        /// </summary>
        private int GetMoveActiveFrame(PlayerController player) {
            if (player.CurrentMove == null) return -1;

            int moveFrame = player.MoveFrame;
            int firstActive = player.CurrentMove.Frames.FirstActiveFrame;
            int lastActive = player.CurrentMove.Frames.LastActiveFrame;

            if (moveFrame < firstActive) return -1;
            if (moveFrame > lastActive) return -1;

            return moveFrame - firstActive; // 0-indexed active frame
        }

        // ──────────────────────────────────────
        //  BLOCKING
        // ──────────────────────────────────────

        private bool IsBlocking(PlayerController defender, MoveData attack) {
            var state = defender.State;

            if (state == PlayerController.PlayerState.Startup
                || state == PlayerController.PlayerState.Active
                || state == PlayerController.PlayerState.Recovery
                || state == PlayerController.PlayerState.PreJump
                || state == PlayerController.PlayerState.ParryRecovery
                || state == PlayerController.PlayerState.Stunned)
                return false;

            bool holdingBack = state == PlayerController.PlayerState.WalkBack;
            bool crouchBlocking = state == PlayerController.PlayerState.Crouching;

            switch (attack.Height) {
                case AttackHeight.Low:
                    return crouchBlocking;
                case AttackHeight.Overhead:
                case AttackHeight.High:
                    return holdingBack;
                case AttackHeight.Mid:
                    return holdingBack || crouchBlocking;
                case AttackHeight.Unblockable:
                    return false;
                default:
                    return holdingBack || crouchBlocking;
            }
        }

        // ──────────────────────────────────────
        //  PARRY
        // ──────────────────────────────────────

        private bool IsParrying(PlayerController defender, MoveData attack) {
            if (!attack.Parryable) return false;
            return defender.State == PlayerController.PlayerState.Parry;
        }

        private void HandleParry(int attackerIdx, int defenderIdx, MoveData move) {
            var def = _players[defenderIdx];

            def.AddMeter(def.Character.ParryMeterGain);

            // Parry uses the defender's character-specific parry hitstop
            int parryHitstop = def.Character.ParryHitStop;
            _players[attackerIdx].ApplyHitstop(parryHitstop);
            def.ApplyHitstop(parryHitstop);
        }

        // ──────────────────────────────────────
        //  FACING
        // ──────────────────────────────────────

        private void UpdateFacing() {
            float delta = _players[1].transform.position.x - _players[0].transform.position.x;

            for (int i = 0; i < 2; i++) {
                var state = _players[i].State;
                bool canFlip = state == PlayerController.PlayerState.Idle
                    || state == PlayerController.PlayerState.WalkForward
                    || state == PlayerController.PlayerState.WalkBack
                    || state == PlayerController.PlayerState.Crouching;

                if (!canFlip) continue;

                if (i == 0)
                    _detectors[i].FacingSign = delta >= 0 ? 1 : -1;
                else
                    _detectors[i].FacingSign = delta >= 0 ? -1 : 1;

                // Flip the visual to match facing direction.
                // Scale.x = FacingSign so the sprite faces the right way.
                Vector3 scale = _players[i].transform.localScale;
                scale.x = Mathf.Abs(scale.x) * _detectors[i].FacingSign;
                _players[i].transform.localScale = scale;
            }
        }

        // ──────────────────────────────────────
        //  STAGE BOUNDS
        // ──────────────────────────────────────

        private void ClampToStageBounds() {
            for (int i = 0; i < 2; i++) {
                Vector3 pos = _players[i].transform.position;
                pos.x = Mathf.Clamp(pos.x, StageLeftBound, StageRightBound);
                pos.y = Mathf.Max(pos.y, GroundY);
                _players[i].transform.position = pos;
            }
        }

        // ──────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────

        private int GetPlayerIndex(PlayerController player) {
            if (_players[0] == player) return 0;
            if (_players[1] == player) return 1;
            return -1;
        }
    }
}