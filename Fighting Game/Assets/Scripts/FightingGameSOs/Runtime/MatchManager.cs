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
    /// ROUND SYSTEM:
    ///   Best of 3 (first to 2 round wins). Each round:
    ///     1. RoundIntro — banner "ROUND N", players can't act
    ///     2. RoundFight — banner "FIGHT!", brief pause then gameplay
    ///     3. Playing — normal gameplay with timer countdown
    ///     4. KO — a player's health hit 0 or timer ran out
    ///     5. RoundEnd — KO banner, brief pause, then reset or match end
    ///     6. MatchEnd — "YOU WIN" banner, match is over
    ///
    ///   Between rounds: health resets, meter carries over, positions reset.
    ///
    /// GGXX HITSTOP MODEL:
    ///   Hitstop is per-player, not global. On hit:
    ///     - Attacker freezes for FrameData.GetAttackerHitstop() frames.
    ///     - Defender freezes for FrameData.GetDefenderHitstop() (on hit)
    ///       or FrameData.GetDefenderBlockstop() (on block) frames.
    ///
    /// PLAYER SPAWNING:
    ///   Unlike the menu scenes, the battle scene does NOT use a
    ///   PlayerInputManager. Instead, MatchManager manually instantiates
    ///   both BattlePlayer prefabs on Start and pairs each with the
    ///   correct input device using MatchSettings.PlayerDeviceIds.
    ///
    /// Setup:
    ///   - Place on a GameObject in the battle scene.
    ///   - Assign BattlePlayerPrefab, BattleUI, and BattleCamera in inspector.
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
        [Tooltip("If MatchSettings has no character for P1, use this.")]
        public CharacterData FallbackP1Character;
        public CharacterData FallbackP2Character;

        [Header("Stage (fallback — overridden by MatchSettings.SelectedStage)")]
        public float StageLeftBound = -6f;
        public float StageRightBound = 6f;
        public float GroundY = 0f;

        [Header("Spawn Points")]
        [Tooltip("Where P1 and P2 appear. Element 0 = P1, Element 1 = P2.")]
        public Transform[] SpawnPoints;

        [Header("Round Settings")]
        [Tooltip("Rounds needed to win the match.")]
        [Min(1)] public int RoundsToWin = 2;

        [Tooltip("Round timer in seconds (99 standard).")]
        public int RoundTimeSeconds = 99;

        [Tooltip("Frames of intro banner before 'FIGHT!' (60 = 1 second).")]
        public int IntroDelayFrames = 90;

        [Tooltip("Frames the 'FIGHT!' banner stays up.")]
        public int FightBannerFrames = 60;

        [Tooltip("Frames of KO slowdown before the round-end sequence.")]
        public int KOFreezeFrames = 120;

        [Tooltip("Frames between rounds (after KO banner, before next round intro).")]
        public int BetweenRoundFrames = 60;

        [Header("UI & Camera")]
        [Tooltip("Reference to the BattleUIManager in the scene.")]
        public BattleUIManager BattleUI;

        [Tooltip("Reference to the BattleCameraController in the scene.")]
        public BattleCameraController BattleCamera;

        [Header("Stage Visuals")]
        public Transform BackgroundParent;

        [Header("Stage Audio")]
        public AudioSource BGMSource;

        // ──────────────────────────────────────
        //  ROUND STATE MACHINE
        // ──────────────────────────────────────

        public enum MatchPhase {
            RoundIntro,     // "ROUND 1" banner, players frozen
            RoundFight,     // "FIGHT!" banner, brief pause
            Playing,        // Gameplay active
            KO,             // KO freeze
            RoundEnd,       // Pause between rounds
            MatchEnd        // Match is over
        }

        [Header("Debug (read-only)")]
        [SerializeField] private MatchPhase _phase = MatchPhase.RoundIntro;
        [SerializeField] private int _currentRound = 1;
        [SerializeField] private int _phaseTimer;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private PlayerController[] _players = new PlayerController[2];
        private InputDetector[] _detectors = new InputDetector[2];
        private int _gameFrame;
        private int[] _roundWins = new int[2];

        // Round timer (counts down in game frames; 60 frames = 1 second)
        private int _roundTimerFrames;
        private int _lastDisplayedSecond = -1;

        // Track which moves have already connected
        private MoveData[] _lastMoveHit = new MoveData[2];

        // Who won the current round (-1 = undecided, 0 = P1, 1 = P2, 2 = draw)
        private int _roundWinner = -1;

        /// <summary>Read-only access to players for UI/camera.</summary>
        public PlayerController GetPlayer(int index) => _players[index];
        public int GameFrame => _gameFrame;
        public MatchPhase Phase => _phase;
        public int CurrentRound => _currentRound;

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            Time.fixedDeltaTime = 1f / 60f;
            ApplyStageData();
            SpawnPlayers();
            StartRound();
        }

        private void ApplyStageData() {
            StageData stage = MatchSettings.SelectedStage;
            if (stage == null) return;

            StageLeftBound = stage.LeftBound;
            StageRightBound = stage.RightBound;
            GroundY = stage.GroundY;

            if (stage.RoundTimeOverride > 0)
                RoundTimeSeconds = stage.RoundTimeOverride;

            if (stage.BackgroundPrefab != null) {
                Transform parent = BackgroundParent != null ? BackgroundParent : transform;
                Instantiate(stage.BackgroundPrefab, parent);
            }

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

        private void SpawnPlayers() {
            if (BattlePlayerPrefab == null) {
                Debug.LogError("[MatchManager] BattlePlayerPrefab is not assigned!");
                return;
            }

            for (int idx = 0; idx < 2; idx++) {
                InputDevice device = FindDeviceForPlayer(idx);

                GameObject playerObj;
                if (device != null) {
                    playerObj = PlayerInput.Instantiate(
                        BattlePlayerPrefab,
                        playerIndex: idx,
                        pairWithDevice: device
                    ).gameObject;
                }
                else {
                    playerObj = Instantiate(BattlePlayerPrefab);
                    Debug.LogWarning($"[MatchManager] No tracked device for P{idx + 1}.");
                }

                playerObj.name = $"BattlePlayer_P{idx + 1}";
                SetupPlayer(idx, playerObj);
            }
        }

        private InputDevice FindDeviceForPlayer(int playerIndex) {
            int deviceId = MatchSettings.PlayerDeviceIds[playerIndex];
            if (deviceId == 0) return null;

            foreach (var device in InputSystem.devices) {
                if (device.deviceId == deviceId)
                    return device;
            }

            Debug.LogWarning($"[MatchManager] Device ID {deviceId} for P{playerIndex + 1} not found.");
            return null;
        }

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

            if (playerInput != null)
                WireInputEvents(playerInput, detector);

            CharacterData character = MatchSettings.SelectedCharacters[idx];
            if (character == null)
                character = idx == 0 ? FallbackP1Character : FallbackP2Character;

            if (character != null)
                controller.Character = character;

            Debug.Log($"[MatchManager] P{idx + 1} — Character: " +
                $"{(controller.Character != null ? controller.Character.CharacterName : "NULL")}, " +
                $"Device: {(playerInput != null && playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "none")}");

            detector.FacingSign = (idx == 0) ? 1 : -1;

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

                if (animator != null && animator.runtimeAnimatorController == null) {
                    animator.enabled = false;
                    animator = null;
                }

                controller.SetVisualReferences(animator, audioSource);

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

        private void WireInputEvents(PlayerInput playerInput, InputDetector detector) {
            var actions = playerInput.actions;
            if (actions == null) {
                Debug.LogError("[MatchManager] PlayerInput has no action asset assigned.");
                return;
            }

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
        //  ROUND MANAGEMENT
        // ──────────────────────────────────────

        /// <summary>
        /// Begins a new round. Resets health, positions, and starts the
        /// intro sequence. Meter carries over between rounds.
        /// </summary>
        private void StartRound() {
            _roundWinner = -1;
            _roundTimerFrames = RoundTimeSeconds * 60;
            _lastDisplayedSecond = -1;
            _lastMoveHit[0] = null;
            _lastMoveHit[1] = null;

            // Reset players for new round (meter carries over)
            for (int i = 0; i < 2; i++) {
                if (_players[i] == null) continue;

                int savedMeter = _players[i].Meter;
                _players[i].ResetForNewRound(keepMeter: true);

                // Reposition
                if (SpawnPoints != null && i < SpawnPoints.Length && SpawnPoints[i] != null)
                    _players[i].transform.position = SpawnPoints[i].position;

                // Reset facing
                _detectors[i].FacingSign = (i == 0) ? 1 : -1;
                Vector3 scale = _players[i].transform.localScale;
                scale.x = Mathf.Abs(scale.x) * _detectors[i].FacingSign;
                _players[i].transform.localScale = scale;
            }

            // Snap camera to reset position
            if (BattleCamera != null)
                BattleCamera.SnapToTarget();

            // Start intro sequence
            SetPhase(MatchPhase.RoundIntro);

            if (BattleUI != null) {
                BattleUI.ShowBanner($"ROUND {_currentRound}", IntroDelayFrames / 60f);
                BattleUI.SetTimer(RoundTimeSeconds);
                BattleUI.SetP1RoundWins(_roundWins[0]);
                BattleUI.SetP2RoundWins(_roundWins[1]);
            }

            Debug.Log($"[MatchManager] === ROUND {_currentRound} START ===");
        }

        private void SetPhase(MatchPhase newPhase) {
            _phase = newPhase;
            _phaseTimer = 0;
        }

        // ──────────────────────────────────────
        //  FIXED UPDATE — THE GAME LOOP
        // ──────────────────────────────────────

        private void FixedUpdate() {
            if (_players[0] == null || _players[1] == null) return;

            _phaseTimer++;

            switch (_phase) {
                case MatchPhase.RoundIntro:
                    TickRoundIntro();
                    break;

                case MatchPhase.RoundFight:
                    TickRoundFight();
                    break;

                case MatchPhase.Playing:
                    TickPlaying();
                    break;

                case MatchPhase.KO:
                    TickKO();
                    break;

                case MatchPhase.RoundEnd:
                    TickRoundEnd();
                    break;

                case MatchPhase.MatchEnd:
                    // Match is over — do nothing (or return to menu)
                    break;
            }
        }

        // ──────────────────────────────────────
        //  PHASE TICKING
        // ──────────────────────────────────────

        private void TickRoundIntro() {
            // Players frozen during intro
            if (_phaseTimer >= IntroDelayFrames) {
                SetPhase(MatchPhase.RoundFight);
                if (BattleUI != null)
                    BattleUI.ShowBanner("FIGHT!", FightBannerFrames / 60f);
            }
        }

        private void TickRoundFight() {
            if (_phaseTimer >= FightBannerFrames) {
                SetPhase(MatchPhase.Playing);
                Debug.Log("[MatchManager] FIGHT!");
            }
        }

        private void TickPlaying() {
            _gameFrame++;

            // --- TICK BOTH PLAYERS ---
            _players[0].GameTick();
            _players[1].GameTick();

            // --- PUSHBOX RESOLUTION ---
            if (!_players[0].InHitstop && !_players[1].InHitstop)
                ResolvePushboxes();

            // --- HITBOX vs HURTBOX ---
            ResolveHitboxes();

            // --- UPDATE FACING ---
            UpdateFacing();

            // --- CLAMP TO STAGE ---
            ClampToStageBounds();

            // --- TIMER ---
            _roundTimerFrames--;
            int displaySecond = Mathf.CeilToInt(_roundTimerFrames / 60f);
            displaySecond = Mathf.Max(0, displaySecond);

            if (displaySecond != _lastDisplayedSecond) {
                _lastDisplayedSecond = displaySecond;
                if (BattleUI != null)
                    BattleUI.SetTimer(displaySecond);
            }

            // --- CHECK WIN CONDITIONS ---
            CheckWinConditions();
        }

        private void TickKO() {
            if (_phaseTimer >= KOFreezeFrames) {
                // Award round win
                if (_roundWinner == 0 || _roundWinner == 1) {
                    _roundWins[_roundWinner]++;

                    if (BattleUI != null) {
                        BattleUI.SetP1RoundWins(_roundWins[0]);
                        BattleUI.SetP2RoundWins(_roundWins[1]);
                    }

                    Debug.Log($"[MatchManager] Round {_currentRound} winner: P{_roundWinner + 1} " +
                        $"(Score: P1={_roundWins[0]}, P2={_roundWins[1]})");
                }
                else if (_roundWinner == 2) {
                    // Double KO — both get a win (GGXX behavior)
                    _roundWins[0]++;
                    _roundWins[1]++;

                    if (BattleUI != null) {
                        BattleUI.SetP1RoundWins(_roundWins[0]);
                        BattleUI.SetP2RoundWins(_roundWins[1]);
                    }

                    Debug.Log($"[MatchManager] Round {_currentRound}: DOUBLE KO!");
                }

                // Check if match is over
                if (_roundWins[0] >= RoundsToWin || _roundWins[1] >= RoundsToWin) {
                    SetPhase(MatchPhase.MatchEnd);

                    string winner;
                    if (_roundWins[0] >= RoundsToWin && _roundWins[1] >= RoundsToWin)
                        winner = "DRAW GAME";
                    else if (_roundWins[0] >= RoundsToWin)
                        winner = "P1 WINS";
                    else
                        winner = "P2 WINS";

                    if (BattleUI != null)
                        BattleUI.ShowBanner(winner, 5f);

                    Debug.Log($"[MatchManager] === MATCH OVER: {winner} ===");
                }
                else {
                    // More rounds to play
                    SetPhase(MatchPhase.RoundEnd);
                }
            }
        }

        private void TickRoundEnd() {
            if (_phaseTimer >= BetweenRoundFrames) {
                _currentRound++;
                StartRound();
            }
        }

        // ──────────────────────────────────────
        //  WIN CONDITION CHECKS
        // ──────────────────────────────────────

        private void CheckWinConditions() {
            bool p1Dead = _players[0].Health <= 0
                       || _players[0].State == PlayerController.PlayerState.KO;
            bool p2Dead = _players[1].Health <= 0
                       || _players[1].State == PlayerController.PlayerState.KO;
            bool timeUp = _roundTimerFrames <= 0;

            if (p1Dead && p2Dead) {
                // Double KO
                _roundWinner = 2;
                TriggerKO("DOUBLE KO");
            }
            else if (p1Dead) {
                _roundWinner = 1; // P2 wins
                TriggerKO("KO");
            }
            else if (p2Dead) {
                _roundWinner = 0; // P1 wins
                TriggerKO("KO");
            }
            else if (timeUp) {
                // Time over — player with more health wins
                int p1Health = _players[0].Health;
                int p2Health = _players[1].Health;

                if (p1Health > p2Health)
                    _roundWinner = 0;
                else if (p2Health > p1Health)
                    _roundWinner = 1;
                else
                    _roundWinner = 2; // Draw

                TriggerKO("TIME");
            }
        }

        private void TriggerKO(string bannerText) {
            SetPhase(MatchPhase.KO);

            if (BattleUI != null)
                BattleUI.ShowBanner(bannerText, KOFreezeFrames / 60f);

            Debug.Log($"[MatchManager] {bannerText}!");
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

            if (atk.State != PlayerController.PlayerState.Active) return;
            if (atk.InHitstop) return;
            if (atk.CurrentMove == null) return;

            if (_lastMoveHit[attackerIdx] == atk.CurrentMove && atk.CurrentMove.HitCount <= 1)
                return;

            Rect[] hitRects = GetActiveHitboxRects(atk);
            if (hitRects == null || hitRects.Length == 0) return;

            Rect[] hurtRects = GetActiveHurtboxRects(def);
            if (hurtRects == null || hurtRects.Length == 0) return;

            HurtboxLayout defLayout = GetActiveHurtboxLayout(def);
            if (defLayout.Invincible) return;

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

            // --- TENSION GAIN ---
            if (blocked) {
                atk.AddMeter(atk.Character.TensionGainOnBlock);
            }
            else {
                atk.AddMeter(atk.Character.TensionGainOnHit);
            }

            _lastMoveHit[attackerIdx] = atk.CurrentMove;
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

        private int GetMoveActiveFrame(PlayerController player) {
            if (player.CurrentMove == null) return -1;

            int moveFrame = player.MoveFrame;
            int firstActive = player.CurrentMove.Frames.FirstActiveFrame;
            int lastActive = player.CurrentMove.Frames.LastActiveFrame;

            if (moveFrame < firstActive) return -1;
            if (moveFrame > lastActive) return -1;

            return moveFrame - firstActive;
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
    }
}