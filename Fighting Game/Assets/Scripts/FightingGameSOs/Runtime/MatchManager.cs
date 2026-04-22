using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Central game manager for a single match.
    ///
    /// FIXED IN THIS VERSION:
    ///   - Crouch-back now blocks mid attacks (down-back = valid standing block).
    ///   - Proximity normals: checks distance to decide close vs far slash/HS.
    ///   - Projectiles are ticked and resolved alongside players.
    ///   - Juggle limit enforcement on airborne opponents.
    ///   - Super flash freeze (brief game-freeze before super executes).
    ///   - Screen shake on heavy hits.
    ///   - Counter hit detection (hitting during startup).
    ///   - Combo counter sent to UI each hit.
    /// </summary>
    public class MatchManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Player Prefab")]
        public GameObject BattlePlayerPrefab;

        [Header("Fallback Characters (for testing without CharSelect)")]
        public CharacterData FallbackP1Character;
        public CharacterData FallbackP2Character;

        [Header("Stage (fallback — overridden by MatchSettings.SelectedStage)")]
        public float StageLeftBound = -3.5f;
        public float StageRightBound = 3.5f;
        public float GroundY = 0f;

        [Header("Spawn Points")]
        public Transform[] SpawnPoints;

        [Header("Round Settings")]
        [Min(1)] public int RoundsToWin = 2;
        public int RoundTimeSeconds = 99;
        public int IntroDelayFrames = 90;
        public int FightBannerFrames = 60;
        public int KOFreezeFrames = 120;
        public int BetweenRoundFrames = 60;

        [Header("Proximity Normal Distance")]
        [Tooltip("If players are within this distance, close normals are used.")]
        public float CloseNormalRange = 1.2f;

        [Header("Super Flash")]
        [Tooltip("Frames of game-freeze when a super activates.")]
        public int SuperFlashFrames = 30;

        [Header("Screen Shake")]
        [Tooltip("Shake intensity on heavy hits (attack level 4+).")]
        public float HeavyShakeIntensity = 0.15f;
        [Tooltip("Shake duration in frames.")]
        public int ShakeDurationFrames = 8;

        [Header("UI & Camera")]
        public BattleUIManager BattleUI;
        public BattleCameraController BattleCamera;
        public PostMatchUI PostMatchPanel;

        [Header("Stage Visuals")]
        public Transform BackgroundParent;

        [Header("Stage Audio")]
        public AudioSource BGMSource;

        // ──────────────────────────────────────
        //  ROUND STATE MACHINE
        // ──────────────────────────────────────

        public enum MatchPhase {
            RoundIntro,
            RoundFight,
            Playing,
            SuperFlash,   // brief freeze for super activation
            KO,
            RoundEnd,
            MatchEnd
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

        private int _roundTimerFrames;
        private int _lastDisplayedSecond = -1;

        private MoveData[] _lastMoveHit = new MoveData[2];
        private int _roundWinner = -1;

        // Projectile tracking
        private List<Projectile> _activeProjectiles = new List<Projectile>();

        // Screen shake
        private int _shakeFramesRemaining;
        private float _shakeIntensity;

        // Super flash state
        private int _superFlashPlayerIndex; // who triggered the super

        // Proximity check cache
        private float _playerDistance;

        /// <summary>Read-only access to players for UI/camera.</summary>
        public PlayerController GetPlayer(int index) => _players[index];
        public int GameFrame => _gameFrame;
        public MatchPhase Phase => _phase;
        public int CurrentRound => _currentRound;

        /// <summary>
        /// Distance between the two players. Updated every frame.
        /// Used by PlayerController for proximity normal resolution.
        /// </summary>
        public float PlayerDistance => _playerDistance;

        /// <summary>
        /// Whether close normals should be used based on proximity.
        /// </summary>
        public bool InCloseRange => _playerDistance <= CloseNormalRange;

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

            foreach (var device in InputSystem.devices)
                if (device.deviceId == deviceId)
                    return device;

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

            detector.FacingSign = (idx == 0) ? 1 : -1;

            if (SpawnPoints != null && idx < SpawnPoints.Length && SpawnPoints[idx] != null)
                controller.transform.position = SpawnPoints[idx].position;

            controller.Initialize();
            controller.SetMatchManager(this, idx);

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

                // Find optional VFXSpawnPoint and Shadow children by name
                Transform vfxPoint = visual.transform.Find("VFXSpawnPoint");
                Transform shadow = visual.transform.Find("Shadow");

                controller.SetVisualReferences(animator, audioSource, vfxPoint, shadow);

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
                    Debug.LogWarning($"[MatchManager] Action '{actionName}' not found.");
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

        private void StartRound() {
            _roundWinner = -1;
            _roundTimerFrames = RoundTimeSeconds * 60;
            _lastDisplayedSecond = -1;
            _lastMoveHit[0] = null;
            _lastMoveHit[1] = null;

            // Destroy lingering projectiles
            foreach (var proj in _activeProjectiles)
                if (proj != null) Destroy(proj.gameObject);
            _activeProjectiles.Clear();

            for (int i = 0; i < 2; i++) {
                if (_players[i] == null) continue;
                _players[i].ResetForNewRound(keepMeter: true);

                if (SpawnPoints != null && i < SpawnPoints.Length && SpawnPoints[i] != null)
                    _players[i].transform.position = SpawnPoints[i].position;

                _detectors[i].FacingSign = (i == 0) ? 1 : -1;
                Vector3 scale = _players[i].transform.localScale;
                scale.x = Mathf.Abs(scale.x) * _detectors[i].FacingSign;
                _players[i].transform.localScale = scale;
            }

            if (BattleCamera != null)
                BattleCamera.SnapToTarget();

            SetPhase(MatchPhase.RoundIntro);

            if (BattleUI != null) {
                BattleUI.ShowBanner($"ROUND {_currentRound}", IntroDelayFrames / 60f);
                BattleUI.SetTimer(RoundTimeSeconds);
                BattleUI.SetP1RoundWins(_roundWins[0]);
                BattleUI.SetP2RoundWins(_roundWins[1]);
            }
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
                case MatchPhase.SuperFlash:
                    TickSuperFlash();
                    break;
                case MatchPhase.KO:
                    TickKO();
                    break;
                case MatchPhase.RoundEnd:
                    TickRoundEnd();
                    break;
                case MatchPhase.MatchEnd:
                    break;
            }

            // Screen shake (runs during any phase)
            ApplyScreenShake();
        }

        // ──────────────────────────────────────
        //  PHASE TICKING
        // ──────────────────────────────────────

        private void TickRoundIntro() {
            if (_phaseTimer >= IntroDelayFrames) {
                SetPhase(MatchPhase.RoundFight);
                if (BattleUI != null)
                    BattleUI.ShowBanner("FIGHT!", FightBannerFrames / 60f);
            }
        }

        private void TickRoundFight() {
            if (_phaseTimer >= FightBannerFrames)
                SetPhase(MatchPhase.Playing);
        }

        private void TickPlaying() {
            _gameFrame++;

            // --- UPDATE PROXIMITY ---
            _playerDistance = Mathf.Abs(
                _players[0].transform.position.x - _players[1].transform.position.x);

            // --- TICK BOTH PLAYERS ---
            _players[0].GameTick();
            _players[1].GameTick();

            // --- RECORD BLOCK INPUT FOR IB TIMING ---
            // When a player transitions to a blocking state (holding back),
            // record the frame so IB window can be checked on hit.
            for (int i = 0; i < 2; i++) {
                var state = _players[i].State;
                if (state == PlayerController.PlayerState.WalkBack
                    || (state == PlayerController.PlayerState.Crouching
                        && CheckDirectionHeld(i, DirectionInput.Back))) {
                    _players[i].RecordBlockInput(_gameFrame);
                }
            }

            // --- THROW TECH RESOLUTION ---
            ResolveThrows();

            // --- TICK PROJECTILES ---
            TickProjectiles();

            // --- PUSHBOX RESOLUTION ---
            if (!_players[0].InHitstop && !_players[1].InHitstop)
                ResolvePushboxes();

            // --- HITBOX vs HURTBOX ---
            ResolveHitboxes();

            // --- PROJECTILE vs PLAYER ---
            ResolveProjectileHits();

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

        private void TickSuperFlash() {
            // During super flash, NOTHING moves — both players and projectiles freeze.
            // The camera can zoom/pan to the super user if desired.
            if (_phaseTimer >= SuperFlashFrames) {
                SetPhase(MatchPhase.Playing);
            }
        }

        private void TickKO() {
            if (_phaseTimer >= KOFreezeFrames) {
                if (_roundWinner == 0 || _roundWinner == 1) {
                    _roundWins[_roundWinner]++;
                    if (BattleUI != null) {
                        BattleUI.SetP1RoundWins(_roundWins[0]);
                        BattleUI.SetP2RoundWins(_roundWins[1]);
                    }
                }
                else if (_roundWinner == 2) {
                    _roundWins[0]++;
                    _roundWins[1]++;
                    if (BattleUI != null) {
                        BattleUI.SetP1RoundWins(_roundWins[0]);
                        BattleUI.SetP2RoundWins(_roundWins[1]);
                    }
                }

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
                        BattleUI.ShowBanner(winner, 3f);

                    // Trigger post-match menu (appears after a short delay)
                    if (PostMatchPanel != null)
                        PostMatchPanel.Show(winner);
                }
                else {
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
        //  SUPER FLASH
        // ──────────────────────────────────────

        /// <summary>
        /// Called externally or internally when a super activates.
        /// Freezes the game for SuperFlashFrames.
        /// </summary>
        public void TriggerSuperFlash(int playerIndex) {
            _superFlashPlayerIndex = playerIndex;
            SetPhase(MatchPhase.SuperFlash);

            if (BattleUI != null)
                BattleUI.ShowBanner("", 0.1f); // brief flash effect — customize as needed
        }

        // ──────────────────────────────────────
        //  SCREEN SHAKE
        // ──────────────────────────────────────

        public void TriggerScreenShake(float intensity, int durationFrames) {
            _shakeIntensity = intensity;
            _shakeFramesRemaining = durationFrames;
        }

        private void ApplyScreenShake() {
            if (BattleCamera == null) return;

            if (_shakeFramesRemaining > 0) {
                _shakeFramesRemaining--;
                float t = (float)_shakeFramesRemaining / ShakeDurationFrames;
                float offset = Random.Range(-_shakeIntensity, _shakeIntensity) * t;
                Vector3 camPos = BattleCamera.transform.position;
                camPos.y += offset;
                camPos.x += Random.Range(-_shakeIntensity * 0.5f, _shakeIntensity * 0.5f) * t;
                BattleCamera.transform.position = camPos;
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
                _roundWinner = 2;
                TriggerKO("DOUBLE KO");
            }
            else if (p1Dead) {
                _roundWinner = 1;
                TriggerKO("KO");
            }
            else if (p2Dead) {
                _roundWinner = 0;
                TriggerKO("KO");
            }
            else if (timeUp) {
                int p1Health = _players[0].Health;
                int p2Health = _players[1].Health;

                if (p1Health > p2Health) _roundWinner = 0;
                else if (p2Health > p1Health) _roundWinner = 1;
                else _roundWinner = 2;

                TriggerKO("TIME");
            }
        }

        private void TriggerKO(string bannerText) {
            SetPhase(MatchPhase.KO);
            if (BattleUI != null)
                BattleUI.ShowBanner(bannerText, KOFreezeFrames / 60f);
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
            for (int attacker = 0; attacker < 2; attacker++)
                ResolveAttack(attacker, 1 - attacker);
        }

        private void ResolveAttack(int attackerIdx, int defenderIdx) {
            var atk = _players[attackerIdx];
            var def = _players[defenderIdx];

            if (atk.State != PlayerController.PlayerState.Active) return;
            if (atk.InHitstop) return;
            if (atk.CurrentMove == null) return;

            if (_lastMoveHit[attackerIdx] == atk.CurrentMove && atk.CurrentMove.HitCount <= 1)
                return;

            // --- JUGGLE LIMIT CHECK ---
            if (!def.CanBeJuggled(atk.CurrentMove))
                return;

            Rect[] hitRects = GetActiveHitboxRects(atk);
            if (hitRects == null || hitRects.Length == 0) return;

            Rect[] hurtRects = GetActiveHurtboxRects(def);
            if (hurtRects == null || hurtRects.Length == 0) return;

            HurtboxLayout defLayout = GetActiveHurtboxLayout(def);
            if (defLayout.Invincible) return;

            // Check defender invincibility (backdash, etc.)
            if (def.IsInvincible) return;

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

            // --- DETERMINE BLOCK TYPE (FD / IB / NORMAL) ---
            BlockType blockType = BlockType.Normal;

            if (blocked) {
                // Check Instant Block first — block input must have been within
                // the IB window (recorded by RecordBlockInput earlier).
                def.CheckInstantBlock(_gameFrame);

                if (def.WasInstantBlocked) {
                    blockType = BlockType.InstantBlock;
                }
                // Check Faultless Defense — holding 2+ buttons while blocking with meter.
                // FD takes precedence over normal block but not over IB.
                else {
                    InputFrame defInput = def.LastInput;
                    int heldCount = 0;
                    if (defInput.HeldButtons.HasFlag(ButtonFlags.Punch)) heldCount++;
                    if (defInput.HeldButtons.HasFlag(ButtonFlags.Kick)) heldCount++;
                    if (defInput.HeldButtons.HasFlag(ButtonFlags.Slash)) heldCount++;
                    if (defInput.HeldButtons.HasFlag(ButtonFlags.HeavySlash)) heldCount++;
                    if (defInput.HeldButtons.HasFlag(ButtonFlags.Dust)) heldCount++;

                    if (heldCount >= 2 && def.Meter > 0)
                        blockType = BlockType.FaultlessDefense;
                }
            }

            // --- COUNTER HIT CHECK ---
            bool counterHit = !blocked && (
                def.State == PlayerController.PlayerState.Startup ||
                def.State == PlayerController.PlayerState.Recovery);

            // --- GUARD CRUSH CHECK ---
            // If the move is unblockable and was "blocked", it actually guard-crushes
            if (blocked && atk.CurrentMove.Height == AttackHeight.Unblockable) {
                def.ApplyGuardCrush();
                // Treat as a hit for damage purposes
                def.TakeHit(atk.CurrentMove, false, false);
                _lastMoveHit[attackerIdx] = atk.CurrentMove;
                return;
            }

            // Apply hit/block to defender (pass counter hit and block type)
            def.TakeHit(atk.CurrentMove, blocked, counterHit, blockType);

            // Track juggle points
            if (def.State == PlayerController.PlayerState.Launched)
                def.ConsumeJugglePoints(atk.CurrentMove.JuggleCost);

            // --- GGACR PER-PLAYER HITSTOP ---
            FrameData frames = atk.CurrentMove.Frames;
            int baseHitstop = frames.GetHitstop();

            if (blocked) {
                // On block: both players freeze for blockstop frames
                int blockstop = frames.GetBlockstop();
                atk.ApplyHitstop(blockstop);
                def.ApplyHitstop(blockstop);

                // FD pushback multiplier is now applied inside PlayerController.TakeHit
                // via StartBlockPushback(), which multiplies the initial velocity by
                // Character.FDPushbackMultiplier when blockType is FaultlessDefense.
            }
            else {
                // On hit: symmetric hitstop for both players
                int attackerHitstop = baseHitstop;
                int defenderHitstop = baseHitstop;

                // Counter hit: extra hitstop to DEFENDER only (GGACR rules)
                // CH bonus is halved if move's normal hitstop < bonus, absent if hitstop is 0
                if (counterHit) {
                    int chBonus = AttackLevelData.GetCounterHitHitstop(
                        frames.AttackLevel, baseHitstop);
                    defenderHitstop += chBonus;
                }

                atk.ApplyHitstop(attackerHitstop);
                def.ApplyHitstop(defenderHitstop);
            }

            // --- NOTIFY ATTACKER OF CONNECTION (enables jump cancel, etc.) ---
            atk.OnMoveConnected(blocked);

            // --- TENSION GAIN ---
            if (blocked)
                atk.AddMeter(atk.Character.TensionGainOnBlock);
            else
                atk.AddMeter(atk.Character.TensionGainOnHit);

            _lastMoveHit[attackerIdx] = atk.CurrentMove;

            // --- SCREEN SHAKE ON HEAVY HITS ---
            if (frames.AttackLevel >= 4 && !blocked)
                TriggerScreenShake(HeavyShakeIntensity, ShakeDurationFrames);

            // --- UPDATE UI COMBO COUNTER ---
            // Show on the ATTACKER's side — they're the one doing the combo.
            // The hit count is tracked on the defender (they receive the hits).
            if (!blocked && BattleUI != null)
                BattleUI.SetComboCount(attackerIdx, def.ComboHitCount);
        }

        // ──────────────────────────────────────
        //  PROJECTILE MANAGEMENT
        // ──────────────────────────────────────

        /// <summary>
        /// Registers a projectile for tracking. Called automatically when
        /// projectiles are found in the scene, or can be called manually.
        /// </summary>
        public void RegisterProjectile(Projectile proj) {
            if (!_activeProjectiles.Contains(proj))
                _activeProjectiles.Add(proj);
        }

        private void TickProjectiles() {
            // Find any new projectiles spawned by players this frame
            var allProjectiles = FindObjectsByType<Projectile>(FindObjectsSortMode.None);
            foreach (var proj in allProjectiles) {
                if (!_activeProjectiles.Contains(proj))
                    _activeProjectiles.Add(proj);
            }

            // Tick and clean up
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--) {
                if (_activeProjectiles[i] == null || !_activeProjectiles[i].IsAlive) {
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }
                _activeProjectiles[i].GameTick();
            }

            // Projectile vs Projectile (cancel out)
            for (int i = 0; i < _activeProjectiles.Count; i++) {
                for (int j = i + 1; j < _activeProjectiles.Count; j++) {
                    var a = _activeProjectiles[i];
                    var b = _activeProjectiles[j];
                    if (a.Owner == b.Owner) continue; // same player's projectiles don't clash

                    if (a.GetHitboxRect().Overlaps(b.GetHitboxRect())) {
                        a.OnProjectileClash();
                        b.OnProjectileClash();
                    }
                }
            }
        }

        private void ResolveProjectileHits() {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--) {
                var proj = _activeProjectiles[i];
                if (proj == null || !proj.IsAlive) continue;

                Rect projRect = proj.GetHitboxRect();

                for (int p = 0; p < 2; p++) {
                    // Don't hit the player who spawned the projectile
                    if (proj.Owner == _players[p].gameObject) continue;

                    Rect[] hurtRects = GetActiveHurtboxRects(_players[p]);
                    if (hurtRects == null) continue;

                    HurtboxLayout layout = GetActiveHurtboxLayout(_players[p]);
                    if (layout.Invincible || _players[p].IsInvincible) continue;

                    bool hit = false;
                    foreach (var hurtRect in hurtRects) {
                        if (projRect.Overlaps(hurtRect)) {
                            hit = true;
                            break;
                        }
                    }

                    if (hit) {
                        // Projectile hit confirmed
                        bool blocked = IsBlockingProjectile(_players[p], proj);

                        // Determine block type for projectile blocks
                        BlockType projBlockType = BlockType.Normal;
                        if (blocked) {
                            _players[p].CheckInstantBlock(_gameFrame);
                            if (_players[p].WasInstantBlocked) {
                                projBlockType = BlockType.InstantBlock;
                            }
                            else {
                                InputFrame pInput = _players[p].LastInput;
                                int hc = 0;
                                if (pInput.HeldButtons.HasFlag(ButtonFlags.Punch)) hc++;
                                if (pInput.HeldButtons.HasFlag(ButtonFlags.Kick)) hc++;
                                if (pInput.HeldButtons.HasFlag(ButtonFlags.Slash)) hc++;
                                if (pInput.HeldButtons.HasFlag(ButtonFlags.HeavySlash)) hc++;
                                if (pInput.HeldButtons.HasFlag(ButtonFlags.Dust)) hc++;

                                if (hc >= 2 && _players[p].Meter > 0)
                                    projBlockType = BlockType.FaultlessDefense;
                            }
                        }

                        // Route through TakeProjectileHit for proper hitstun/blockstun
                        _players[p].TakeProjectileHit(proj, blocked, projBlockType);

                        // Update combo counter UI for projectile hits
                        // Show on the ATTACKER's side (1 - p, since p is the defender).
                        if (!blocked && BattleUI != null)
                            BattleUI.SetComboCount(1 - p, _players[p].ComboHitCount);

                        proj.OnHitConfirmed();
                        break;
                    }
                }
            }
        }

        private bool IsBlockingProjectile(PlayerController defender, Projectile proj) {
            var state = defender.State;
            int idx = (_players[0] == defender) ? 0 : 1;

            // States that absolutely cannot block
            if (state == PlayerController.PlayerState.Startup
                || state == PlayerController.PlayerState.Active
                || state == PlayerController.PlayerState.Recovery
                || state == PlayerController.PlayerState.PreJump
                || state == PlayerController.PlayerState.Hitstun
                || state == PlayerController.PlayerState.Launched
                || state == PlayerController.PlayerState.Knockdown
                || state == PlayerController.PlayerState.Stunned
                || state == PlayerController.PlayerState.Crumple
                || state == PlayerController.PlayerState.Thrown
                || state == PlayerController.PlayerState.KO
                || state == PlayerController.PlayerState.AirDashForward
                || state == PlayerController.PlayerState.AirDashBack)
                return false;

            bool holdBack = CheckDirectionHeld(idx, DirectionInput.Back);
            bool holdDown = CheckDirectionHeld(idx, DirectionInput.Down);

            // Air blocking projectiles (Airborne or already air-blockstunned)
            if (state == PlayerController.PlayerState.Airborne
                || (state == PlayerController.PlayerState.Blockstun && defender.IsAirBlocking)) {
                if (!holdBack) return false;
                switch (proj.Height) {
                    case AttackHeight.Low:
                    case AttackHeight.Unblockable:
                        return false;
                    default:
                        return true;
                }
            }

            // Already in blockstun (blockstring) — continue blocking if still holding back
            // This is how multi-hit blockstrings work in fighting games.
            if (state == PlayerController.PlayerState.Blockstun) {
                if (!holdBack) return false;
                switch (proj.Height) {
                    case AttackHeight.Low:
                        return holdDown; // must be holding down-back
                    case AttackHeight.Overhead:
                    case AttackHeight.High:
                        return !holdDown; // must be standing back
                    case AttackHeight.Unblockable:
                        return false;
                    default:
                        return true; // mid — either works
                }
            }

            // Ground blocking
            bool standingBack = (state == PlayerController.PlayerState.WalkBack)
                || (state == PlayerController.PlayerState.Idle && holdBack);
            bool crouchBack = (state == PlayerController.PlayerState.Crouching && holdBack);

            switch (proj.Height) {
                case AttackHeight.Low:
                    return crouchBack;
                case AttackHeight.Overhead:
                case AttackHeight.High:
                    return standingBack;
                case AttackHeight.Mid:
                    return standingBack || crouchBack;
                case AttackHeight.Unblockable:
                    return false;
                default:
                    return standingBack || crouchBack;
            }
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
                case PlayerController.PlayerState.Launched:
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
        //  BLOCKING (FIXED: crouch-back now blocks mid)
        // ──────────────────────────────────────

        /// <summary>
        /// Checks if the player is holding back on their input.
        /// Uses InputDetector.LastDirection which is set during Poll()
        /// and already accounts for facing (Back = away from opponent).
        /// </summary>
        private bool IsHoldingBack(PlayerController player) {
            int idx = (_players[0] == player) ? 0 : 1;
            return CheckDirectionHeld(idx, DirectionInput.Back);
        }

        private bool IsBlocking(PlayerController defender, MoveData attack) {
            var state = defender.State;

            // States that CAN'T block at all
            if (state == PlayerController.PlayerState.Startup
                || state == PlayerController.PlayerState.Active
                || state == PlayerController.PlayerState.Recovery
                || state == PlayerController.PlayerState.PreJump
                || state == PlayerController.PlayerState.ParryRecovery
                || state == PlayerController.PlayerState.Stunned
                || state == PlayerController.PlayerState.Launched
                || state == PlayerController.PlayerState.Knockdown
                || state == PlayerController.PlayerState.Wakeup
                || state == PlayerController.PlayerState.AirDashForward
                || state == PlayerController.PlayerState.AirDashBack
                || state == PlayerController.PlayerState.Crumple)
                return false;

            int idx = (_players[0] == defender) ? 0 : 1;

            // --- AIR BLOCKING ---
            // In GGXX: airborne defenders can block mids and highs by holding back.
            // Lows and unblockables cannot be air blocked.
            if (state == PlayerController.PlayerState.Airborne) {
                bool airBack = CheckDirectionHeld(idx, DirectionInput.Back);
                if (!airBack) return false;

                switch (attack.Height) {
                    case AttackHeight.Low:
                    case AttackHeight.Unblockable:
                        return false; // can't air block lows or unblockables
                    default:
                        return true; // air block mids, highs, overheads
                }
            }

            // --- GROUND BLOCKING ---
            bool holdingBack = state == PlayerController.PlayerState.WalkBack;

            bool crouchBlocking = state == PlayerController.PlayerState.Crouching;
            bool crouchBack = false;

            if (crouchBlocking)
                crouchBack = CheckDirectionHeld(idx, DirectionInput.Back);

            switch (attack.Height) {
                case AttackHeight.Low:
                    return crouchBack; // must be holding down-back to block lows
                case AttackHeight.Overhead:
                case AttackHeight.High:
                    return holdingBack; // must be standing back
                case AttackHeight.Mid:
                    return holdingBack || crouchBack;
                case AttackHeight.Unblockable:
                    return false;
                default:
                    return holdingBack || crouchBack;
            }
        }

        /// <summary>
        /// Checks if the given player was holding a specific direction
        /// on the most recent input frame. Reads from InputDetector.LastDirection,
        /// which is set during Poll() each game tick.
        /// </summary>
        private bool CheckDirectionHeld(int playerIndex, DirectionInput dir) {
            if (playerIndex < 0 || playerIndex >= _detectors.Length) return false;
            if (_detectors[playerIndex] == null) return false;

            return _detectors[playerIndex].LastDirection.HasFlag(dir);
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
        //  THROW TECH RESOLUTION
        // ──────────────────────────────────────

        /// <summary>
        /// Single source of truth for all throw resolution. Called each
        /// frame during TickPlaying AFTER GameTick (so inputs are fresh).
        ///
        /// GGACR throw flow:
        ///   Frame 0: Attacker inputs throw → enters ThrowStartup, stores target.
        ///   Frames 1–N (ThrowStartupFrames):
        ///     - If defender is in an unthrowable state → throw whiffs.
        ///     - If defender inputs P+K → throw teched, both separate.
        ///   Frame N+1 (startup expires):
        ///     - Throw connects → defender enters Thrown, attacker plays throw anim.
        ///     - Attacker deals throw damage via the throw MoveData.
        ///
        /// The defender is NOT locked into a special state during the tech window.
        /// In GGACR, throws are fast (2–3f startup) so the defender barely has
        /// time to react — the tech window is what matters, not animation lock.
        /// </summary>
        private void ResolveThrows() {
            for (int i = 0; i < 2; i++) {
                if (_players[i].State != PlayerController.PlayerState.ThrowStartup)
                    continue;

                var atk = _players[i];
                int defIdx = 1 - i;
                var def = _players[defIdx];

                // --- RANGE CHECK ---
                // Throws only connect at close range. If the defender moved
                // out of range, the throw whiffs.
                float throwRange = CloseNormalRange; // reuse proximity threshold
                float dist = Mathf.Abs(atk.transform.position.x - def.transform.position.x);
                if (dist > throwRange) {
                    atk.OnThrowWhiff();
                    continue;
                }

                // --- UNTHROWABLE STATE CHECK ---
                // Defender can't be thrown if airborne, knocked down, already
                // thrown, KO'd, invincible, or in their own ThrowStartup
                // (simultaneous throws = both whiff in GGACR).
                var defState = def.State;
                if (defState == PlayerController.PlayerState.Launched
                    || defState == PlayerController.PlayerState.Knockdown
                    || defState == PlayerController.PlayerState.AirTeching
                    || defState == PlayerController.PlayerState.Thrown
                    || defState == PlayerController.PlayerState.ThrowStartup
                    || defState == PlayerController.PlayerState.KO
                    || defState == PlayerController.PlayerState.GuardCrush
                    || defState == PlayerController.PlayerState.Airborne
                    || defState == PlayerController.PlayerState.PreJump
                    || def.IsInvincible) {
                    atk.OnThrowWhiff();
                    continue;
                }

                // --- TECH CHECK ---
                // Defender can tech during the entire throw startup window
                // by pressing P+K (same input as initiating a throw).
                int framesElapsed = _gameFrame - atk.ThrowAttemptFrame;
                int techWindow = atk.Character.ThrowTechWindow;

                if (framesElapsed <= techWindow) {
                    InputFrame defInput = def.LastInput;
                    if (PlayerController.IsThrowTechInput(defInput)) {
                        // Throw teched — both players push apart
                        atk.OnThrowTeched();
                        def.OnThrowTeched();
                        continue;
                    }
                }

                // --- CONNECT CHECK ---
                // If throw startup frames have elapsed without a tech, it connects.
                if (framesElapsed >= atk.Character.ThrowStartupFrames) {
                    // Apply throw damage to defender
                    MoveData throwMove = atk.CurrentMove;

                    // Defender enters Thrown state — locked for the throw move's total duration
                    int thrownDuration = (throwMove != null) ? throwMove.Frames.TotalFrames : 30;
                    def.OnThrown(thrownDuration);
                    if (throwMove != null) {
                        int throwDmg = Mathf.RoundToInt(
                            throwMove.Damage.BaseDamage * def.Character.DefenseModifier);
                        def.SetHealth(def.Health - throwDmg);
                    }

                    // Attacker plays the throw animation
                    atk.ExecuteThrowConnect();

                    // Tension gain for landing a throw
                    atk.AddMeter(atk.Character.TensionGainOnHit);

                    continue;
                }

                // Still within startup — do nothing, wait for next frame.
            }
        }

        // ──────────────────────────────────────
        //  PROXIMITY NORMALS
        // ──────────────────────────────────────

        /// <summary>
        /// Returns the correct normal for the given button and stance,
        /// checking proximity for close normals when applicable.
        /// Called from outside or can be used by PlayerController.
        /// </summary>
        public MoveData GetNormalWithProximity(int playerIndex, ButtonInput button, MoveUsableState stance) {
            var moveset = _players[playerIndex].Character.Moveset;
            if (moveset == null) return null;

            // Check proximity for close normals (standing only)
            if (stance == MoveUsableState.Standing && InCloseRange) {
                if (button == ButtonInput.Slash && moveset.CloseSlash != null)
                    return moveset.CloseSlash;
            }

            return moveset.GetNormal(button, stance);
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