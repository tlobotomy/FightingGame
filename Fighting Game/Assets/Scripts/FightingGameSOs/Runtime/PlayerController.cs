using UnityEngine;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// The core player state machine. Owns the InputBuffer and InputParser,
    /// reads from InputDetector, and resolves moves from the CharacterData asset.
    ///
    /// Designed to be ticked at a fixed 60fps from a central MatchManager,
    /// NOT from Update(). This guarantees frame-deterministic behavior.
    ///
    /// GGXX HITSTOP MODEL:
    ///   Attacker and defender have SEPARATE hitstop counters.
    ///   On hit: attacker freezes for AttackerHitstop frames,
    ///           defender freezes for DefenderHitstop frames.
    ///   After hitstop ends, attacker continues remaining active + recovery,
    ///   and defender enters hitstun or blockstun.
    /// </summary>
    [RequireComponent(typeof(InputDetector))]
    public class PlayerController : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Character")]
        [Tooltip("The character definition. Drag a CharacterData asset here.")]
        public CharacterData Character;

        // ──────────────────────────────────────
        //  INTERNAL REFERENCES
        // ──────────────────────────────────────

        private InputDetector _detector;
        private InputBuffer _buffer;
        private InputParser _parser;
        private Animator _animator;
        private AudioSource _audioSource;
        private Transform _vfxSpawnPoint;
        private Transform _shadowTransform;

        private MoveData[] _specialsSorted;
        private MovesetData _moveset;

        /// <summary>
        /// Back-reference to the match manager for proximity normal lookups.
        /// Set via SetMatchManager() after instantiation.
        /// </summary>
        private MatchManager _matchManager;
        private int _playerIndex;

        // ──────────────────────────────────────
        //  STATE ENUM
        // ──────────────────────────────────────

        public enum PlayerState {
            Idle,
            WalkForward,
            WalkBack,
            Crouching,
            PreJump,
            Airborne,
            AirDashForward,
            AirDashBack,
            JumpLanding,
            DashForward,
            DashBack,
            Startup,        // move is winding up
            Active,         // hitbox is live
            Recovery,       // move is cooling down
            Hitstun,
            Blockstun,
            Knockdown,      // on the ground after a knockdown hit
            Wakeup,         // getting up from knockdown
            Stunned,        // dizzy
            Parry,          // successful parry freeze
            ParryRecovery,  // whiffed parry vulnerability
            Launched,       // popped into the air by a launcher
            Crumple,        // slow collapse before knockdown
            AirTeching,     // recovering from air hit (brief invincibility)
            BackRoll,       // ground tech backward roll
            ThrowStartup,   // throw startup (can be teched)
            Thrown,         // caught in a throw
            GuardCrush,     // guard balance broken — staggered
            KO
        }

        [Header("Debug (read-only)")]
        [SerializeField] private PlayerState _state = PlayerState.Idle;
        [SerializeField] private int _gameFrame;
        [SerializeField] private int _stateFrameCounter;
        [SerializeField] private string _currentMoveName;

        private MoveData _currentMove;
        private int _moveFrame;
        private bool _moveHasConnected;   // true once this attack hit or was blocked (enables jump cancel)
        private bool _moveWasBlocked;     // true if the connection was a block (for AllowJumpCancelOnBlock)
        private bool _moveStartedAirborne; // true if ExecuteMove was called while character was above ground
        private bool _isAirBlocking;      // true when blockstun was entered while airborne (gravity applies)
        private int _airJumpsUsed;        // tracks how many air jumps have been used this airtime
        private int _airDashesUsed;       // tracks air dashes used this airtime
        private int _airborneFrameCount;  // frames spent airborne (for air dash minimum height)

        // GGXX per-player hitstop
        private int _hitstopRemaining;
        private int _hitstopFramesElapsed;       // how many hitstop frames passed (for buffer window expansion)
        private ButtonFlags _hitstopPendingPress; // button presses accumulated during hitstop

        // Stun duration tracking
        private int _stunFramesRemaining;

        // Jump / air physics
        private float _velocityX;
        private float _velocityY;
        private float _groundY;
        private const float GROUND_SNAP_THRESHOLD = 0.02f;

        /// <summary>
        /// Safe setter for _groundY. Only writes if the character is physically
        /// on (or very near) the current ground level. Prevents corruption when
        /// called mid-air from state transitions, hit effects, or move execution.
        /// </summary>
        private void TrySetGroundY() {
            if (transform.position.y <= _groundY + GROUND_SNAP_THRESHOLD)
                _groundY = transform.position.y;
        }

        // Ground pushback (velocity-based slide during hitstun/blockstun)
        private float _pushbackVelocityX;  // current horizontal slide speed (decays per frame)
        private float _pushbackFriction;   // deceleration per frame (auto-calculated so velocity reaches 0 when stun ends)

        // Health / meter / stun
        private int _health;
        private int _meter;
        private int _stunMeter;

        // GGXX Tension system
        private int _negativePenaltyTimer;
        private int _idleNegativeTimer;
        private bool _inNegativePenalty;

        // Combo tracking
        private int _comboHitCount;         // how many hits in current combo
        private float _comboDamageScaling;  // current scaling multiplier (starts at 1.0)
        private int _jugglePointsUsed;      // juggle points consumed so far
        private bool _isBeingComboed;       // true while in a combo (no neutral reset)

        // Knockdown / wakeup
        private const int HARD_KNOCKDOWN_FRAMES = 30;
        private const int SOFT_KNOCKDOWN_FRAMES = 18;
        private const int WAKEUP_FRAMES = 8;
        private const int CRUMPLE_FRAMES = 40;
        private bool _hardKnockdown;

        // Backdash invincibility
        private int _invincibleFramesRemaining;

        // Stun recovery timer (frames since last hit — stun recovers after a delay)
        private int _framesSinceLastHit;
        private const int STUN_RECOVERY_DELAY = 60; // 1 second before stun starts recovering

        // Counter hit tracking (passed from MatchManager via TakeHit)
        private bool _lastHitWasCounterHit;

        // ── FAULTLESS DEFENSE ──
        private bool _faultlessDefenseActive;

        // ── INSTANT BLOCK ──
        private int _blockInputFrame;       // game frame when block input started
        private bool _instantBlocked;       // true if current blockstun was IB'd

        // ── AIR TECH ──
        private bool _canAirTech;           // true when untechable time has expired
        private AirTechDirection _airTechDir;

        // ── THROW ──
        private PlayerController _throwTarget; // who we're throwing (during ThrowStartup)
        private int _throwAttemptFrame;        // game frame when throw started (for tech window)

        // ──────────────────────────────────────
        //  PUBLIC ACCESSORS
        // ──────────────────────────────────────

        public PlayerState State => _state;
        public int GameFrame => _gameFrame;
        public int Health => _health;
        public int Meter => _meter;
        public int StunMeter => _stunMeter;
        public MoveData CurrentMove => _currentMove;
        public int MoveFrame => _moveFrame;
        public int FacingSign => _detector.FacingSign;
        public bool InHitstop => _hitstopRemaining > 0;
        public bool InNegativePenalty => _inNegativePenalty;
        public int ComboHitCount => _comboHitCount;
        public bool IsInvincible => _invincibleFramesRemaining > 0;
        public bool IsFaultlessDefenseActive => _faultlessDefenseActive;
        public bool WasInstantBlocked => _instantBlocked;
        public bool CanAirTech => _canAirTech;
        public bool IsAirBlocking => _isAirBlocking;

        /// <summary>
        /// The most recent InputFrame from this tick. Exposed so MatchManager
        /// can read held/pressed buttons for FD detection, throw teching, etc.
        /// </summary>
        public InputFrame LastInput { get; private set; }

        public void SetHealth(int value) => _health = value;
        public void SetMeter(int value) => _meter = value;

        /// <summary>
        /// Called by MatchManager after instantiating the character's
        /// visual prefab, to link the animator and audio source.
        /// </summary>
        public void SetVisualReferences(Animator animator, AudioSource audioSource,
            Transform vfxSpawnPoint = null, Transform shadow = null) {
            _animator = animator;
            _audioSource = audioSource;
            _vfxSpawnPoint = vfxSpawnPoint;
            _shadowTransform = shadow;
        }

        /// <summary>
        /// World position where hit/block VFX should spawn.
        /// Falls back to character center if no VFXSpawnPoint is set.
        /// </summary>
        public Vector3 VFXPosition => _vfxSpawnPoint != null
            ? _vfxSpawnPoint.position
            : transform.position + Vector3.up * 0.5f;

        /// <summary>
        /// Called by MatchManager after spawning to link back for
        /// proximity normal lookups and other cross-system queries.
        /// </summary>
        public void SetMatchManager(MatchManager manager, int playerIndex) {
            _matchManager = manager;
            _playerIndex = playerIndex;
        }

        /// <summary>
        /// Called by MatchManager when a hit connects. Sets how many frames
        /// this player freezes in place (GGXX per-player hitstop).
        /// </summary>
        public void ApplyHitstop(int frames) {
            if (frames <= 0) return; // safety: never apply zero or negative hitstop
            _hitstopRemaining = frames;
            _hitstopFramesElapsed = 0;
            _hitstopPendingPress = ButtonFlags.None;
#if UNITY_EDITOR
            Debug.Log($"[Hitstop] {gameObject.name} frozen for {frames}f | state={_state} | move={(_currentMove != null ? _currentMove.MoveName : "none")}");
#endif
        }

        /// <summary>
        /// Called by MatchManager on the ATTACKER when their move connects (hit or block).
        /// Enables jump-cancel and other on-contact cancel routes.
        /// </summary>
        public void OnMoveConnected(bool wasBlocked) {
            _moveHasConnected = true;
            _moveWasBlocked = wasBlocked;
        }

        // ──────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────

        private void Awake() {
            _detector = GetComponent<InputDetector>();
            _buffer = new InputBuffer(40);
            _parser = new InputParser(_buffer);
        }

        /// <summary>
        /// Called by MatchManager at round start.
        /// </summary>
        public void Initialize() {
            if (Character == null) {
                Debug.LogError($"[PlayerController] No CharacterData assigned on {gameObject.name}.");
                return;
            }

            _moveset = Character.Moveset;
            if (_moveset != null)
                _specialsSorted = _moveset.GetAllSpecialsSorted();
            else
                Debug.LogWarning($"[PlayerController] No Moveset assigned on {Character.name}.");

            _health = Character.MaxHealth;
            _meter = 0;
            _stunMeter = Character.MaxStun;
            _gameFrame = 0;
            _hitstopRemaining = 0;
            _hitstopFramesElapsed = 0;
            _hitstopPendingPress = ButtonFlags.None;
            _stunFramesRemaining = 0;
            _negativePenaltyTimer = 0;
            _idleNegativeTimer = 0;
            _inNegativePenalty = false;
            _comboHitCount = 0;
            _comboDamageScaling = 1f;
            _jugglePointsUsed = 0;
            _isBeingComboed = false;
            _invincibleFramesRemaining = 0;
            _framesSinceLastHit = 999;
            _faultlessDefenseActive = false;
            _blockInputFrame = -999;
            _instantBlocked = false;
            _canAirTech = false;
            _throwTarget = null;
            _throwAttemptFrame = -999;
            _state = PlayerState.Idle;
            _buffer.Clear();
        }

        /// <summary>
        /// Resets player state for a new round. Health resets to full,
        /// meter is preserved (GGXX convention).
        /// </summary>
        public void ResetForNewRound(bool keepMeter) {
            _health = Character.MaxHealth;
            if (!keepMeter) _meter = 0;
            _stunMeter = Character.MaxStun;
            _hitstopRemaining = 0;
            _hitstopFramesElapsed = 0;
            _hitstopPendingPress = ButtonFlags.None;
            _stunFramesRemaining = 0;
            _negativePenaltyTimer = 0;
            _idleNegativeTimer = 0;
            _inNegativePenalty = false;
            _comboHitCount = 0;
            _comboDamageScaling = 1f;
            _jugglePointsUsed = 0;
            _isBeingComboed = false;
            _invincibleFramesRemaining = 0;
            _framesSinceLastHit = 999;
            _currentMove = null;
            _moveFrame = 0;
            _velocityX = 0f;
            _velocityY = 0f;
            _pushbackVelocityX = 0f;
            _pushbackFriction = 0f;
            _groundY = transform.position.y; // cache ground level for shadow/jump calculations
            _airJumpsUsed = 0;
            _airDashesUsed = 0;
            _airborneFrameCount = 0;
            _moveHasConnected = false;
            _moveWasBlocked = false;
            _moveStartedAirborne = false;
            _isAirBlocking = false;
            _state = PlayerState.Idle;
            _stateFrameCounter = 0;
            _buffer.Clear();

            if (_animator != null && HasAnimatorState("Idle"))
                _animator.Play("Idle", 0, 0f);
        }

        // ──────────────────────────────────────
        //  GAME TICK (called from MatchManager.FixedUpdate)
        // ──────────────────────────────────────

        public void GameTick() {
            _gameFrame++;
            _framesSinceLastHit++;

            // --- HITSTOP: freeze in place ---
            if (_hitstopRemaining > 0) {
                _hitstopRemaining--;
                _hitstopFramesElapsed++;

                // Poll input and push to the buffer so direction history
                // remains intact (needed for charge detection).
                // ALSO accumulate any button presses — these tend to scroll
                // out of ButtonPressWindow (5 frames) during long hitstop
                // (11+ frames), making cancel inputs disappear before any
                // cancel check ever runs.
                InputFrame frozenInput = _detector.Poll(_gameFrame);
                _buffer.Push(frozenInput);
                _hitstopPendingPress |= frozenInput.PressedButtons;
                LastInput = frozenInput;

                if (_hitstopRemaining == 0) {
                    // Hitstop just ended — inject a synthetic frame that
                    // carries ALL button presses from the entire freeze.
                    // This makes them visible to ButtonPressedInWindow on
                    // the first post-hitstop frame, as if the player just
                    // pressed them.
                    if (_hitstopPendingPress != ButtonFlags.None) {
                        InputFrame synthetic = frozenInput;
                        synthetic.PressedButtons = _hitstopPendingPress;
                        _buffer.Push(synthetic);
                    }
                    _hitstopPendingPress = ButtonFlags.None;
                    _hitstopFramesElapsed = 0;
                }

                return;
            }

            // --- INVINCIBILITY COUNTDOWN ---
            if (_invincibleFramesRemaining > 0)
                _invincibleFramesRemaining--;

            _stateFrameCounter++;

            // 1. Poll input
            InputFrame input = _detector.Poll(_gameFrame);
            _buffer.Push(input);
            LastInput = input;

            // 2. Advance current move / state timer
            if (_currentMove != null) {
                _moveFrame++;
                ApplyMoveMovement(); // Apply movement curves
                ApplyAirMoveGravity(); // Gravity during air attacks
                SpawnProjectileCheck(); // Check for projectile spawn frame

                // 2b. Check cancels BEFORE UpdateMovePhase to avoid the edge
                // case where the move completes and clears _currentMove on the
                // same frame the player presses a cancel button.
                //
                // Priority order (highest first):
                //   0. Kara cancel (Startup frames 1-2, into special/super)
                //   1. Gatling (GGACR: Active/Recovery, no hit-confirm needed)
                //   2. Special/Super cancel (Active/Recovery, within CancelData window)
                //   3. Jump cancel (Active/Recovery, requires hit-confirm)
                bool cancelled = TryKaraCancel(input)
                    || TryGatlingCancel(input)
                    || TrySpecialCancel(input)
                    || TryJumpCancel(input);

                if (!cancelled)
                    UpdateMovePhase();
            }

            // 3. Tick state-specific logic
            TickState(input);

            // 4. If no active move, try normal input resolution
            if (_currentMove == null) {
                // Stun meter recovery
                TickStunRecovery();

                // Attempt to resolve a new move (only when actionable)
                if (CanAct())
                    ResolveInput(input);
            }


            // 6. If still idle/walking, handle movement
            if (IsMovementState())
                HandleMovement(input);

            // 7. Update GGXX tension gauge
            TickTension();

            // 8. Pin shadow to ground below character
            UpdateShadow();

            // Debug
            _currentMoveName = _currentMove != null ? _currentMove.MoveName : "—";
        }

        /// <summary>
        /// Keeps the shadow sprite pinned to ground level directly below the character.
        /// During jumps/launches, the shadow stays on the floor and scales down
        /// with distance to give a height cue.
        /// </summary>
        private void UpdateShadow() {
            if (_shadowTransform == null) return;

            // Shadow stays at ground Y, directly below the character
            Vector3 shadowPos = _shadowTransform.position;
            shadowPos.x = transform.position.x;
            shadowPos.y = _groundY;
            _shadowTransform.position = shadowPos;

            // Scale shadow based on height — shrinks as the character goes higher
            float height = transform.position.y - _groundY;
            float scale = Mathf.Clamp01(1f - (height * 0.15f));
            _shadowTransform.localScale = new Vector3(scale, scale * 0.5f, 1f);
        }

        // ──────────────────────────────────────
        //  MOVE MOVEMENT (advancing specials, DPs, etc.)
        // ──────────────────────────────────────

        /// <summary>
        /// Applies the MoveMovement curves to the character's position
        /// each frame while a move is active. This handles things like
        /// DP rising, slide advancing, lunge forward, etc.
        /// </summary>
        private void ApplyMoveMovement() {
            if (_currentMove == null) return;

            MoveMovement movement = _currentMove.Movement;
            if (movement.HorizontalCurve == null && movement.VerticalCurve == null) return;

            int totalFrames = _currentMove.Frames.TotalFrames;
            if (totalFrames <= 0) return;

            float normalizedTime = (float)_moveFrame / totalFrames;
            Vector2 delta = movement.Evaluate(normalizedTime, _detector.FacingSign);

            Vector3 pos = transform.position;
            pos.x += delta.x;
            pos.y += delta.y;

            // Don't go below ground
            if (!movement.Airborne)
                pos.y = Mathf.Max(pos.y, _groundY);

            transform.position = pos;
        }

        /// <summary>
        /// Applies gravity and horizontal drift during air attacks.
        /// Without this, characters freeze in mid-air when performing
        /// a move because HandleMovement and the Airborne tick don't
        /// run while the state is Startup/Active/Recovery.
        /// </summary>
        private void ApplyAirMoveGravity() {
            if (!_moveStartedAirborne) return;

            // Apply gravity
            _velocityY -= Character.Gravity;

            Vector3 pos = transform.position;
            pos.x += _velocityX;
            pos.y += _velocityY;

            // Landing during an air move
            if (pos.y <= _groundY) {
                pos.y = _groundY;
                _velocityX = 0f;
                _velocityY = 0f;
                _moveStartedAirborne = false;
                _airJumpsUsed = 0;
                _airDashesUsed = 0;
                _airborneFrameCount = 0;
                // Don't change state — let UpdateMovePhase finish the move.
                // The move completes on the ground normally.
            }

            transform.position = pos;
        }

        // ──────────────────────────────────────
        //  PROJECTILE SPAWNING
        // ──────────────────────────────────────

        private void SpawnProjectileCheck() {
            if (_currentMove == null) return;
            if (_currentMove.ProjectilePrefab == null) return;
            if (_moveFrame != _currentMove.ProjectileSpawnFrame) return;

            Vector2 pos = transform.position;
            Vector2 offset = _currentMove.ProjectileSpawnOffset;
            Vector3 spawnPos = new Vector3(
                pos.x + offset.x * _detector.FacingSign,
                pos.y + offset.y,
                0f);

            var proj = Instantiate(_currentMove.ProjectilePrefab, spawnPos, Quaternion.identity);

            // If the projectile has a Projectile component, initialize and register it
            var projComp = proj.GetComponent<Projectile>();
            if (projComp != null) {
                projComp.Initialize(_detector.FacingSign, gameObject);

                // Register with MatchManager so it gets ticked and collides
                if (_matchManager != null)
                    _matchManager.RegisterProjectile(projComp);
            }
        }

        // ──────────────────────────────────────
        //  MOVE RESOLUTION (priority order)
        // ──────────────────────────────────────

        private void ResolveInput(InputFrame input) {
            if (_moveset == null) return;

            // --- PARRY ---
            if (TryParry(input)) return;

            // --- SUPERS / OVERDRIVES ---
            if (_moveset.SuperArts != null) {
                foreach (var sa in _moveset.SuperArts) {
                    if (sa.Move != null && _meter >= sa.CostPerUse) {
                        if (_parser.TryMatchMove(sa.Move)) {
                            _meter -= sa.CostPerUse;
                            ExecuteMove(sa.Move);
                            return;
                        }
                    }
                }
            }

            // --- SPECIALS + EX ---
            MoveData special = _parser.TryMatchFirst(_specialsSorted);
            if (special != null) {
                if (special.IsEX) {
                    if (_meter >= special.MeterCost) {
                        _meter -= special.MeterCost;
                        ExecuteMove(special);
                        return;
                    }
                }
                else {
                    ExecuteMove(special);
                    return;
                }
            }

            // (Gatlings and special cancels are handled in the move-active
            //  block of GameTick, before ResolveInput runs.)

            // --- COMMAND NORMALS ---
            if (_moveset.CommandNormals != null) {
                MoveData cmd = _parser.TryMatchFirst(_moveset.CommandNormals);
                if (cmd != null) {
                    ExecuteMove(cmd);
                    return;
                }
            }

            // --- THROW (P+K simultaneously) ---
            if (TryThrow(input)) return;

            // --- TAUNT (HS+D simultaneously) ---
            if (TryTaunt()) return;

            // --- NORMALS (with proximity check) ---
            MoveUsableState stance = GetCurrentStance();
            foreach (ButtonInput btn in System.Enum.GetValues(typeof(ButtonInput))) {
                if (btn == ButtonInput.None) continue;
                if (!_parser.MatchButton(btn)) continue;

                // Use proximity-aware lookup if MatchManager is linked,
                // otherwise fall back to standard normal lookup.
                MoveData normal = (_matchManager != null)
                    ? _matchManager.GetNormalWithProximity(_playerIndex, btn, stance)
                    : _moveset.GetNormal(btn, stance);

                if (normal != null) {
                    ExecuteMove(normal);
                    return;
                }
            }
        }

        // ──────────────────────────────────────
        //  MOVE EXECUTION
        // ──────────────────────────────────────

        private void ExecuteMove(MoveData move) {
            _currentMove = move;
            _moveFrame = 0;
            _moveHasConnected = false;
            _moveWasBlocked = false;

            // Track whether this move was initiated in the air so gravity
            // continues to apply during Startup/Active/Recovery phases.
            _moveStartedAirborne = transform.position.y > _groundY + GROUND_SNAP_THRESHOLD;

            TrySetGroundY();
            SetState(PlayerState.Startup);

            if (_animator != null && !string.IsNullOrEmpty(move.AnimationStateName)
                && HasAnimatorState(move.AnimationStateName))
                _animator.Play(move.AnimationStateName, 0, 0f);

            if (_audioSource != null && move.SwingSound != null)
                _audioSource.PlayOneShot(move.SwingSound);
        }

        /// <summary>
        /// Computes the move phase directly from _moveFrame and the move's
        /// frame data, WITHOUT relying on _state.
        ///
        /// This eliminates the state-lag problem: _state is set by the
        /// PREVIOUS frame's UpdateMovePhase, but cancels now run BEFORE
        /// UpdateMovePhase. On the first Active frame, _state would still
        /// read Startup — causing cancel checks to fail for one frame.
        /// This helper gives the TRUE phase for the current _moveFrame.
        /// </summary>
        private PlayerState GetCurrentMovePhase() {
            if (_currentMove == null) return _state;

            int firstActive = _currentMove.Frames.FirstActiveFrame;
            int lastActive = _currentMove.Frames.LastActiveFrame;
            int totalFrames = _currentMove.Frames.TotalFrames;

            if (_moveFrame < firstActive) return PlayerState.Startup;
            else if (_moveFrame <= lastActive) return PlayerState.Active;
            else if (_moveFrame < totalFrames) return PlayerState.Recovery;
            else return _state; // move finished
        }

        private void UpdateMovePhase() {
            int firstActive = _currentMove.Frames.FirstActiveFrame;
            int lastActive = _currentMove.Frames.LastActiveFrame;
            int totalFrames = _currentMove.Frames.TotalFrames;

            if (_moveFrame < firstActive) {
                SetState(PlayerState.Startup);
            }
            else if (_moveFrame <= lastActive) {
                SetState(PlayerState.Active);
            }
            else if (_moveFrame < totalFrames) {
                SetState(PlayerState.Recovery);
            }
            else {
                // Move finished — return to appropriate state based on
                // what the player is currently holding, not just Idle.
                // This ensures crouching players stay crouching after a
                // move ends, so the next input resolves as 2P not 5P.
                bool wasAirMove = _moveStartedAirborne;
                _currentMove = null;
                _moveFrame = 0;
                _moveStartedAirborne = false;
                if (wasAirMove || transform.position.y > _groundY + GROUND_SNAP_THRESHOLD) {
                    SetState(PlayerState.Airborne);
                }
                else {
                    // Check held direction to return to the correct stance
                    DirectionInput dir = LastInput.Direction;
                    if (dir.HasFlag(DirectionInput.Down))
                        SetState(PlayerState.Crouching);
                    else if (dir.HasFlag(DirectionInput.Forward))
                        SetState(PlayerState.WalkForward);
                    else if (dir.HasFlag(DirectionInput.Back))
                        SetState(PlayerState.WalkBack);
                    else
                        SetState(PlayerState.Idle);
                }
            }
        }

        // ──────────────────────────────────────
        //  CAN-ACT LOGIC
        // ──────────────────────────────────────

        private bool CanAct() {
            switch (_state) {
                case PlayerState.Idle:
                case PlayerState.WalkForward:
                case PlayerState.WalkBack:
                case PlayerState.Crouching:
                    return true;

                case PlayerState.Airborne:
                case PlayerState.Launched:
                    return _currentMove == null;

                case PlayerState.Startup:
                    if (_currentMove != null && _currentMove.Cancel.IsInKaraWindow(_moveFrame))
                        return true;
                    return false;

                case PlayerState.Active:
                case PlayerState.Recovery:
                    if (_currentMove != null && _currentMove.Cancel.IsInCancelWindow(_moveFrame))
                        return true;
                    return false;

                default:
                    return false;
            }
        }

        private bool CanCancelCurrentInto(MoveData candidate) {
            if (_currentMove == null) return true;
            return _currentMove.CanCancelInto(candidate, _moveFrame);
        }

        /// <summary>
        /// Kara cancel: during the first 1-2 startup frames of a normal,
        /// the player can cancel into a special/super. This lets players
        /// use the forward momentum of a normal's startup to extend a
        /// special's range (classic FG technique).
        /// </summary>
        private bool TryKaraCancel(InputFrame input) {
            if (_currentMove == null || _moveset == null) return false;
            if (GetCurrentMovePhase() != PlayerState.Startup) return false;
            if (!_currentMove.Cancel.AllowKaraCancel) return false;
            if (!_currentMove.Cancel.IsInKaraWindow(_moveFrame)) return false;

            // Try supers
            if (_moveset.SuperArts != null) {
                foreach (var sa in _moveset.SuperArts) {
                    if (sa.Move == null || _meter < sa.CostPerUse) continue;
                    if (_parser.TryMatchMove(sa.Move)) {
                        _meter -= sa.CostPerUse;
                        ExecuteMove(sa.Move);
                        return true;
                    }
                }
            }

            // Try specials
            if (_specialsSorted != null) {
                foreach (var special in _specialsSorted) {
                    if (special == null) continue;
                    if (!_parser.TryMatchMove(special)) continue;
                    if (special.IsEX && _meter < special.MeterCost) continue;
                    if (special.IsEX) _meter -= special.MeterCost;
                    ExecuteMove(special);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to cancel the current move into a special or super,
        /// using the CancelData window and hierarchy rules on the current move.
        /// This runs during Active/Recovery and respects MaxCancelLevel and
        /// AlwaysSuperCancellable flags.
        /// </summary>
        private bool TrySpecialCancel(InputFrame input) {
            if (_currentMove == null || _moveset == null) return false;
            PlayerState phase = GetCurrentMovePhase();
            if (phase != PlayerState.Active && phase != PlayerState.Recovery)
                return false;

            // Must be within the per-move cancel window
            if (!_currentMove.Cancel.IsInCancelWindow(_moveFrame))
                return false;

            // Try supers first (highest priority)
            if (_moveset.SuperArts != null) {
                foreach (var sa in _moveset.SuperArts) {
                    if (sa.Move == null || _meter < sa.CostPerUse) continue;
                    if (!_currentMove.CanCancelInto(sa.Move, _moveFrame)) continue;
                    if (_parser.TryMatchMove(sa.Move)) {
                        _meter -= sa.CostPerUse;
                        ExecuteMove(sa.Move);
                        return true;
                    }
                }
            }

            // Try specials + EX
            if (_specialsSorted != null) {
                foreach (var special in _specialsSorted) {
                    if (special == null) continue;
                    if (!_currentMove.CanCancelInto(special, _moveFrame)) continue;
                    if (!_parser.TryMatchMove(special)) continue;

                    if (special.IsEX) {
                        if (_meter >= special.MeterCost) {
                            _meter -= special.MeterCost;
                            ExecuteMove(special);
                            return true;
                        }
                    }
                    else {
                        ExecuteMove(special);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to jump-cancel the current move. In GGXX, most normals are
        /// jump-cancellable on hit. Some are also jump-cancellable on block.
        /// Returns true if the jump cancel was performed.
        /// </summary>
        private bool TryJumpCancel(InputFrame input) {
            // Must be in Active or Recovery of a move
            PlayerState phase = GetCurrentMovePhase();
            if (phase != PlayerState.Active && phase != PlayerState.Recovery)
                return false;
            if (_currentMove == null)
                return false;

            // Must be within cancel window
            if (!_currentMove.Cancel.IsInCancelWindow(_moveFrame))
                return false;

            // Check if the move allows jump cancel for the current connection type
            if (_moveWasBlocked && !_currentMove.Cancel.AllowJumpCancelOnBlock)
                return false;
            if (!_moveWasBlocked && !_currentMove.Cancel.AllowJumpCancel)
                return false;

            // Move must have actually connected
            if (!_moveHasConnected)
                return false;

            // Player must be holding up
            if (!input.Direction.HasFlag(DirectionInput.Up))
                return false;

            // Jump cancel — clear the move and transition to PreJump
            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            bool holdBack = input.Direction.HasFlag(DirectionInput.Back);

            if (holdForward) _jumpDirectionIntent = 1;
            else if (holdBack) _jumpDirectionIntent = -1;
            else _jumpDirectionIntent = 0;

            _currentMove = null;
            _moveFrame = 0;
            _moveHasConnected = false;
            _moveWasBlocked = false;
            _moveStartedAirborne = false;
            // Don't overwrite _groundY here — it was set correctly when
            // we left the ground. Overwriting mid-air corrupts the landing check.
            SetState(PlayerState.PreJump);
            return true;
        }

        /// <summary>
        /// Checks for air jump (double jump) input during the Airborne state.
        /// Requires a fresh up press (wasn't holding up last frame) and
        /// remaining air jumps from Character.AirJumps.
        /// </summary>
        private void TryAirJump(InputFrame input) {
            bool holdUp = input.Direction.HasFlag(DirectionInput.Up);

            if (Character.AirJumps > 0
                && _airJumpsUsed < Character.AirJumps
                && holdUp && !_wasHoldingUp) {
                _airJumpsUsed++;

                // Determine direction
                bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
                bool holdBack = input.Direction.HasFlag(DirectionInput.Back);

                int dirIntent = 0;
                if (holdForward) dirIntent = 1;
                else if (holdBack) dirIntent = -1;

                // Replace velocity entirely (air jump resets vertical momentum)
                _velocityY = Character.AirJumpHeight;
                _velocityX = dirIntent * Character.AirJumpForwardSpeed * _detector.FacingSign;
            }

            // Always update tracking (HandleMovement doesn't run while airborne)
            _wasHoldingUp = holdUp;
        }

        /// <summary>
        /// Checks for air dash input (double-tap forward/back while airborne).
        /// Uses the same release-frame tracking as ground dashes.
        /// Requires remaining air dashes and minimum airborne time.
        /// </summary>
        private bool TryAirDash(InputFrame input) {
            if (Character.AirDashes <= 0) return false;
            if (_airDashesUsed >= Character.AirDashes) return false;
            if (_airborneFrameCount < Character.AirDashMinHeight) return false;

            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            bool holdBack = input.Direction.HasFlag(DirectionInput.Back);
            bool holdDown = input.Direction.HasFlag(DirectionInput.Down);
            bool holdUp = input.Direction.HasFlag(DirectionInput.Up);

            // Forward air dash: double-tap forward (no down/up held)
            if (holdForward && !holdDown && !holdUp
                && (_gameFrame - _lastForwardReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastForwardReleaseFrame > 0) {
                _lastForwardReleaseFrame = 0;
                _airDashesUsed++;
                _velocityY = 0f; // cancel vertical momentum (GGACR-style float)
                _velocityX = Character.AirDashSpeed * _detector.FacingSign;
                SetState(PlayerState.AirDashForward);
                return true;
            }

            // Back air dash: double-tap back
            if (holdBack && !holdDown && !holdUp
                && (_gameFrame - _lastBackReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastBackReleaseFrame > 0) {
                _lastBackReleaseFrame = 0;
                _airDashesUsed++;
                _velocityY = 0f;
                _velocityX = -Character.AirBackDashSpeed * _detector.FacingSign;
                SetState(PlayerState.AirDashBack);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  MOVEMENT
        // ──────────────────────────────────────

        private int _jumpDirectionIntent;

        // Dash detection
        private const int DASH_INPUT_WINDOW = 10;
        private int _lastForwardReleaseFrame;
        private int _lastBackReleaseFrame;
        private bool _wasHoldingForward;
        private bool _wasHoldingBack;
        private bool _wasHoldingUp;

        private void HandleMovement(InputFrame input) {
            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            bool holdBack = input.Direction.HasFlag(DirectionInput.Back);
            bool holdDown = input.Direction.HasFlag(DirectionInput.Down);
            bool holdUp = input.Direction.HasFlag(DirectionInput.Up);

            // --- DASH DETECTION ---
            if (!holdForward && _wasHoldingForward)
                _lastForwardReleaseFrame = _gameFrame;
            if (!holdBack && _wasHoldingBack)
                _lastBackReleaseFrame = _gameFrame;

            _wasHoldingForward = holdForward;
            _wasHoldingBack = holdBack;
            _wasHoldingUp = holdUp;

            if (holdForward && !holdDown && !holdUp
                && (_gameFrame - _lastForwardReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastForwardReleaseFrame > 0) {
                _lastForwardReleaseFrame = 0;
                SetState(PlayerState.DashForward);
                return;
            }

            if (holdBack && !holdDown && !holdUp
                && (_gameFrame - _lastBackReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastBackReleaseFrame > 0) {
                _lastBackReleaseFrame = 0;
                SetState(PlayerState.DashBack);
                _invincibleFramesRemaining = Character.BackDashInvincibleFrames;
                return;
            }

            // --- NORMAL MOVEMENT ---
            if (holdDown) {
                SetState(PlayerState.Crouching);
            }
            else if (holdUp && _state != PlayerState.PreJump && _state != PlayerState.Airborne) {
                if (holdForward) _jumpDirectionIntent = 1;
                else if (holdBack) _jumpDirectionIntent = -1;
                else _jumpDirectionIntent = 0;

                TrySetGroundY();
                SetState(PlayerState.PreJump);
            }
            else if (holdForward) {
                SetState(PlayerState.WalkForward);
                transform.position += new Vector3(
                    Character.WalkForwardSpeed * _detector.FacingSign, 0, 0);
            }
            else if (holdBack) {
                SetState(PlayerState.WalkBack);
                transform.position += new Vector3(
                    -Character.WalkBackwardSpeed * _detector.FacingSign, 0, 0);
            }
            else {
                SetState(PlayerState.Idle);
            }
        }

        // ──────────────────────────────────────
        //  STATE TICKING
        // ──────────────────────────────────────

        private void TickState(InputFrame input) {
            switch (_state) {
                case PlayerState.PreJump:
                    if (_stateFrameCounter >= Character.PreJumpFrames) {
                        _velocityY = Character.JumpHeight;
                        _velocityX = _jumpDirectionIntent * Character.JumpForwardSpeed * _detector.FacingSign;
                        TrySetGroundY();
                        _airJumpsUsed = 0;
                        SetState(PlayerState.Airborne);
                    }
                    break;

                case PlayerState.Airborne: {
                        _airborneFrameCount++;

                        // Track direction releases for air dash detection
                        // (HandleMovement doesn't run while airborne)
                        bool airHoldFwd = input.Direction.HasFlag(DirectionInput.Forward);
                        bool airHoldBck = input.Direction.HasFlag(DirectionInput.Back);
                        if (!airHoldFwd && _wasHoldingForward)
                            _lastForwardReleaseFrame = _gameFrame;
                        if (!airHoldBck && _wasHoldingBack)
                            _lastBackReleaseFrame = _gameFrame;
                        _wasHoldingForward = airHoldFwd;
                        _wasHoldingBack = airHoldBck;

                        _velocityY -= Character.Gravity;
                        Vector3 airPos = transform.position;
                        airPos.x += _velocityX;
                        airPos.y += _velocityY;

                        if (airPos.y <= _groundY) {
                            airPos.y = _groundY;
                            _velocityX = 0f;
                            _velocityY = 0f;
                            _airJumpsUsed = 0;
                            _airDashesUsed = 0;
                            _airborneFrameCount = 0;

                            if (Character.JumpLandingFrames > 0)
                                SetState(PlayerState.JumpLanding);
                            else
                                SetState(PlayerState.Idle);
                        }
                        else {
                            // Air dash check first (consumes double-tap input)
                            if (!TryAirDash(input)) {
                                // Air jump check (double jump / triple jump)
                                TryAirJump(input);
                            }
                        }

                        transform.position = airPos;
                        break;
                    }

                case PlayerState.AirDashForward: {
                        // Fixed-speed horizontal movement, minimal gravity during dash
                        Vector3 adPos = transform.position;
                        adPos.x += _velocityX;
                        _velocityY -= Character.Gravity * 0.2f;
                        adPos.y += _velocityY;

                        if (adPos.y <= _groundY) {
                            adPos.y = _groundY;
                            _velocityX = 0f;
                            _velocityY = 0f;
                            _airDashesUsed = 0;
                            _airJumpsUsed = 0;
                            _airborneFrameCount = 0;
                            SetState(PlayerState.Idle);
                            transform.position = adPos;
                            break;
                        }

                        if (_stateFrameCounter >= Character.AirDashDuration) {
                            _velocityX *= 0.3f; // bleed off horizontal speed
                            SetState(PlayerState.Airborne);
                        }

                        transform.position = adPos;
                        break;
                    }

                case PlayerState.AirDashBack: {
                        Vector3 abdPos = transform.position;
                        abdPos.x += _velocityX;
                        _velocityY -= Character.Gravity * 0.2f;
                        abdPos.y += _velocityY;

                        if (abdPos.y <= _groundY) {
                            abdPos.y = _groundY;
                            _velocityX = 0f;
                            _velocityY = 0f;
                            _airDashesUsed = 0;
                            _airJumpsUsed = 0;
                            _airborneFrameCount = 0;
                            SetState(PlayerState.Idle);
                            transform.position = abdPos;
                            break;
                        }

                        if (_stateFrameCounter >= Character.AirBackDashDuration) {
                            _velocityX = 0f;
                            SetState(PlayerState.Airborne);
                        }

                        transform.position = abdPos;
                        break;
                    }

                case PlayerState.JumpLanding:
                    if (_stateFrameCounter >= Character.JumpLandingFrames)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.DashForward: {
                        float dashSpeed = Character.DashDistance / Character.DashDuration;
                        transform.position += new Vector3(
                            dashSpeed * _detector.FacingSign, 0, 0);

                        if (_stateFrameCounter >= Character.DashDuration)
                            SetState(PlayerState.Idle);
                        break;
                    }

                case PlayerState.DashBack: {
                        float backDashSpeed = Character.BackDashDistance / Character.BackDashDuration;
                        transform.position += new Vector3(
                            -backDashSpeed * _detector.FacingSign, 0, 0);

                        if (_stateFrameCounter >= Character.BackDashDuration) {
                            _invincibleFramesRemaining = 0; // ensure invincibility ends
                            SetState(PlayerState.Idle);
                        }
                        break;
                    }

                case PlayerState.Hitstun:
                    TickPushback(); // slide during hitstun
                    _stunFramesRemaining--;
                    if (_stunFramesRemaining <= 0) {
                        _pushbackVelocityX = 0f;
                        // Hitstun expired — the defender recovered, combo is over.
                        // If the attacker's next hit lands, it starts a NEW combo.
                        ResetComboState();
                        SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.Blockstun:
                    // Tick FD — drains meter while holding 2+ buttons in blockstun
                    TickFaultlessDefense(input);

                    if (_isAirBlocking) {
                        // Air blockstun: gravity applies, land → idle
                        _velocityY -= Character.Gravity;
                        Vector3 abPos = transform.position;
                        abPos.y += _velocityY;

                        if (abPos.y <= _groundY) {
                            abPos.y = _groundY;
                            _velocityY = 0f;
                            _isAirBlocking = false;
                            _faultlessDefenseActive = false;
                            _instantBlocked = false;
                            ResetComboState();
                            SetState(PlayerState.Idle);
                            transform.position = abPos;
                            break;
                        }
                        transform.position = abPos;
                    }
                    else {
                        TickPushback(); // slide during ground blockstun
                    }

                    _stunFramesRemaining--;
                    if (_stunFramesRemaining <= 0) {
                        _pushbackVelocityX = 0f;
                        _faultlessDefenseActive = false;
                        _instantBlocked = false;
                        _isAirBlocking = false;
                        ResetComboState();
                        if (transform.position.y > _groundY)
                            SetState(PlayerState.Airborne); // still in the air after air blockstun
                        else
                            SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.Launched:
                    // Launched = airborne hitstun — gravity applies, can be juggled
                    _velocityY -= Character.Gravity;
                    Vector3 launchPos = transform.position;
                    launchPos.x += _velocityX;
                    launchPos.y += _velocityY;

                    if (launchPos.y <= _groundY) {
                        launchPos.y = _groundY;
                        _velocityX = 0f;
                        _velocityY = 0f;

                        // Land from launch → knockdown
                        _hardKnockdown = true;
                        SetState(PlayerState.Knockdown);
                    }
                    else {
                        // Check for air tech once untechable time expires
                        TickAirTech(input);
                    }

                    transform.position = launchPos;
                    break;

                case PlayerState.AirTeching: {
                        // Brief invincible recovery in the air, then fall normally
                        _velocityY -= Character.Gravity;
                        Vector3 techPos = transform.position;
                        techPos.x += _velocityX;
                        techPos.y += _velocityY;

                        if (techPos.y <= _groundY) {
                            techPos.y = _groundY;
                            _velocityX = 0f;
                            _velocityY = 0f;
                            SetState(PlayerState.Idle);
                        }

                        transform.position = techPos;
                        break;
                    }

                case PlayerState.Crumple:
                    if (_stateFrameCounter >= CRUMPLE_FRAMES) {
                        _hardKnockdown = true;
                        SetState(PlayerState.Knockdown);
                    }
                    break;

                case PlayerState.Knockdown: {
                        TickPushback(); // slide on the ground during knockdown

                        int knockdownDuration = _hardKnockdown
                            ? HARD_KNOCKDOWN_FRAMES
                            : SOFT_KNOCKDOWN_FRAMES;

                        // Ground tech options (soft knockdown only, after half duration)
                        GroundTechType techType = CheckGroundTech(input);
                        if (techType == GroundTechType.BackRoll) {
                            ExecuteBackRoll();
                            break;
                        }
                        else if (techType == GroundTechType.QuickRise) {
                            SetState(PlayerState.Wakeup);
                            break;
                        }

                        if (_stateFrameCounter >= knockdownDuration)
                            SetState(PlayerState.Wakeup);
                        break;
                    }

                case PlayerState.BackRoll: {
                        // Roll backward with invincibility
                        float rollSpeed = Character.BackRollDistance / Character.BackRollFrames;
                        transform.position += new Vector3(
                            -rollSpeed * _detector.FacingSign, 0f, 0f);

                        if (_stateFrameCounter >= Character.BackRollFrames) {
                            ResetComboState();
                            SetState(PlayerState.Idle);
                        }
                        break;
                    }

                case PlayerState.Wakeup:
                    // Allow reversal specials/supers within the reversal window
                    if (TryWakeupReversal(input))
                        break;

                    if (_stateFrameCounter >= WAKEUP_FRAMES) {
                        ResetComboState();
                        SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.ThrowStartup:
                    // MatchManager's ResolveThrows handles ALL throw resolution:
                    //   - Tech check each frame within the tech window
                    //   - Whiff detection if defender is unthrowable
                    //   - Connect when startup frames expire without tech
                    // PlayerController just counts frames here. No fallback needed.
                    break;

                case PlayerState.Thrown:
                    // Defender is caught in a throw. Duration is determined
                    // by _stunFramesRemaining (set by MatchManager when throw
                    // connects, based on the throw move's recovery frames).
                    _stunFramesRemaining--;
                    if (_stunFramesRemaining <= 0) {
                        // Throw ends — transition to knockdown
                        _hardKnockdown = true; // throws cause hard knockdown
                        SetState(PlayerState.Knockdown);
                    }
                    break;

                case PlayerState.GuardCrush:
                    if (_stunFramesRemaining > 0)
                        _stunFramesRemaining--;
                    else
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Stunned:
                    if (_stateFrameCounter >= Character.StunDuration) {
                        _stunMeter = Character.MaxStun;
                        SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.ParryRecovery:
                    if (_stateFrameCounter >= Character.ParryWhiffRecovery)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Parry:
                    if (_stateFrameCounter >= Character.ParryWindowFrames)
                        SetState(PlayerState.ParryRecovery);
                    break;
            }
        }

        // ──────────────────────────────────────
        //  STUN METER RECOVERY
        // ──────────────────────────────────────

        private void TickStunRecovery() {
            if (Character == null) return;
            if (_stunMeter >= Character.MaxStun) return;
            if (_isBeingComboed) return; // no recovery during combos

            // Only recover after a delay since last hit
            if (_framesSinceLastHit >= STUN_RECOVERY_DELAY) {
                _stunMeter = Mathf.Min(
                    _stunMeter + Character.StunRecoveryRate,
                    Character.MaxStun);
            }
        }

        // ──────────────────────────────────────
        //  COMBO TRACKING
        // ──────────────────────────────────────


        /// <summary>
        /// Called when combo ends (opponent returns to neutral).
        /// </summary>
        private void ResetComboState() {
            _comboHitCount = 0;
            _comboDamageScaling = 1f;
            _jugglePointsUsed = 0;
            _isBeingComboed = false;
        }

        // ──────────────────────────────────────
        //  PARRY
        // ──────────────────────────────────────

        private bool TryParry(InputFrame input) {
            if (_state != PlayerState.Idle && _state != PlayerState.WalkBack
                && _state != PlayerState.Crouching)
                return false;

            bool forwardTap = input.Direction.HasFlag(DirectionInput.Forward)
                && !_buffer.DirectionInWindow(DirectionInput.Forward, 2);
            bool downTap = input.Direction.HasFlag(DirectionInput.Down)
                && !_buffer.DirectionInWindow(DirectionInput.Down, 2);

            if (forwardTap || downTap) {
                SetState(PlayerState.Parry);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  THROW (P+K simultaneously)
        // ──────────────────────────────────────

        private bool TryThrow(InputFrame input) {
            // Use the new two-button simultaneous detection
            if (!_parser.MatchTwoButtons(ButtonInput.Punch, ButtonInput.Kick, 3))
                return false;

            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            MoveData throwMove = holdForward ? _moveset.ForwardThrow : _moveset.BackThrow;

            if (throwMove != null) {
                // Store the throw move so ThrowStartup can use it when it connects.
                // Don't ExecuteMove yet — we enter ThrowStartup first so the
                // defender gets a tech window. MatchManager resolves the outcome.
                _currentMove = throwMove;

                // Find the opponent via MatchManager
                if (_matchManager != null) {
                    int defIdx = 1 - _playerIndex;
                    var target = _matchManager.GetPlayer(defIdx);
                    BeginThrowAttempt(target);
                }
                else {
                    // No MatchManager — execute throw directly (editor testing)
                    ExecuteMove(throwMove);
                }
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  TAUNT (HS+D simultaneously)
        // ──────────────────────────────────────

        private bool TryTaunt() {
            if (!_parser.MatchTwoButtons(ButtonInput.HeavySlash, ButtonInput.Dust, 3))
                return false;

            if (_moveset.Taunt != null) {
                ExecuteMove(_moveset.Taunt);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  GATLING CANCEL (GGACR GATLING TABLE)
        // ──────────────────────────────────────

        /// <summary>
        /// Checks the character's Gatling table and fires the next move in
        /// the chain if the player's buffered input matches.
        ///
        /// GGACR Gatling rules:
        ///   - Gatling cancels fire from Active OR Recovery frames.
        ///   - They do NOT require the move to have hit (unlike special/jump cancels).
        ///   - The current move must be within its cancel window.
        ///   - Each Gatling entry is an ordered sequence; the move currently
        ///     executing must match the entry at position [i], and the input
        ///     must match the move at position [i+1].
        ///   - If the current move appears multiple times across different
        ///     sequences, all sequences are checked (e.g. 5S can Gatling into
        ///     both 5HS and 2HS if both sequences are defined).
        /// </summary>
        /// <summary>
        /// GGACR-style Gatling cancel. Fires from the first Active frame through
        /// the end of Recovery — does NOT require the move's CancelData window
        /// (that window governs special/super cancels separately).
        ///
        /// Gatlings fire on whiff too (GGACR rule). Connection is NOT required.
        ///
        /// The Gatling table in MovesetData defines which moves chain into which.
        /// A move can appear as a source in multiple routes (e.g. cS → 5HS and
        /// cS → 2D are both valid if both routes exist). All matching routes are
        /// checked, and the first input match wins.
        /// </summary>
        private bool TryGatlingCancel(InputFrame input) {
            if (_moveset == null || _moveset.TargetCombos == null) return false;
            if (_currentMove == null) return false;

            // Gatlings fire from Active through Recovery (not Startup).
            PlayerState phase = GetCurrentMovePhase();
            if (phase != PlayerState.Active && phase != PlayerState.Recovery)
                return false;

            // Gatlings require contact (hit or block) — no whiff cancels.
            if (!_moveHasConnected)
                return false;

            // Collect all valid "next" moves from every Gatling route where
            // _currentMove is the source. This allows many-to-many routing
            // without the designer needing to duplicate sequences.
            // We check higher-priority targets first (specials > normals)
            // by sorting candidates, but in practice GGACR Gatlings are
            // normal→normal chains, so first-match is fine.
            // Determine the player's current stance from held directions.
            // During Active/Recovery the state is NOT Crouching/Standing,
            // so we read the stick directly to know what the player intends.
            MoveUsableState heldStance;
            if (transform.position.y > _groundY + GROUND_SNAP_THRESHOLD || _moveStartedAirborne)
                heldStance = MoveUsableState.Airborne;
            else if (input.Direction.HasFlag(DirectionInput.Down))
                heldStance = MoveUsableState.Crouching;
            else
                heldStance = MoveUsableState.Standing;

            foreach (var tc in _moveset.TargetCombos) {
                if (tc.Sequence == null || tc.Sequence.Length < 2) continue;

                for (int i = 0; i < tc.Sequence.Length - 1; i++) {
                    if (tc.Sequence[i] != _currentMove) continue;

                    MoveData next = tc.Sequence[i + 1];
                    if (next == null) continue;

                    // Stance filter: the target move's UsableFrom must include
                    // the player's current held direction. This prevents 5D
                    // from matching when the player is holding down (wants 2D).
                    if ((next.UsableFrom & heldStance) == 0) continue;

                    // Check if the player's buffered input matches this target
                    if (_parser.TryMatchMove(next)) {
                        ExecuteMove(next);
                        return true;
                    }
                }
            }

            return false;
        }

        // ──────────────────────────────────────
        //  DAMAGE / HIT RECEPTION
        // ──────────────────────────────────────

        /// <summary>
        /// Called by MatchManager when this player is hit.
        /// Handles damage (with combo scaling), hit effects (knockdown,
        /// launch, crumple, etc.), stun, and state transitions.
        ///
        /// counterHit: true if the defender was in Startup/Recovery when hit.
        ///   - Untechable time doubles on CH (GGACR rule).
        ///   - CH hitstop bonus is handled separately by MatchManager.
        /// </summary>
        public void TakeHit(MoveData move, bool blocked, bool counterHit = false,
            BlockType blockType = BlockType.Normal) {
            _currentMove = null;
            _moveStartedAirborne = false;
            _framesSinceLastHit = 0;
            _lastHitWasCounterHit = counterHit;

            if (blocked) {
                int chipDamage = Mathf.RoundToInt(move.Damage.ChipDamage * Character.DefenseModifier);

                // FD negates chip damage (GGACR rule)
                if (blockType == BlockType.FaultlessDefense)
                    chipDamage = 0;

                _health -= chipDamage;

                // Capture pre-block stance for animation selection
                bool wasCrouching = (_state == PlayerState.Crouching);
                _isAirBlocking = (_state == PlayerState.Airborne || _state == PlayerState.Launched);

                SetState(PlayerState.Blockstun);

                // Play stance-specific block animation
                string blockAnim = _isAirBlocking ? "AirBlockstun"
                    : wasCrouching ? "CrouchBlockstun"
                    : "Blockstun";
                if (_animator != null && HasAnimatorState(blockAnim))
                    _animator.Play(blockAnim, 0, 0f);

                // Determine blockstun based on block type and air/ground
                if (_isAirBlocking) {
                    switch (blockType) {
                        case BlockType.FaultlessDefense:
                            _stunFramesRemaining = move.Frames.GetAirFDBlockstun();
                            _faultlessDefenseActive = true;
                            break;
                        case BlockType.InstantBlock:
                            _stunFramesRemaining = move.Frames.GetAirIBBlockstun();
                            _instantBlocked = true;
                            break;
                        default:
                            _stunFramesRemaining = move.Frames.GetAirBlockstun();
                            break;
                    }
                }
                else {
                    switch (blockType) {
                        case BlockType.FaultlessDefense:
                            _stunFramesRemaining = move.Frames.GetFDBlockstun();
                            _faultlessDefenseActive = true;
                            break;
                        case BlockType.InstantBlock:
                            _stunFramesRemaining = move.Frames.GetIBBlockstun();
                            _instantBlocked = true;
                            break;
                        default:
                            _stunFramesRemaining = move.Frames.GetBlockstun();
                            break;
                    }
                }

                if (_audioSource != null && move.BlockSound != null)
                    _audioSource.PlayOneShot(move.BlockSound);

                // Start velocity-based pushback (slides during blockstun — grounded only)
                if (!_isAirBlocking) {
                    float fdMult = (blockType == BlockType.FaultlessDefense && Character != null)
                        ? Character.FDPushbackMultiplier : 1f;
                    StartBlockPushback(move, _stunFramesRemaining, fdMult);
                }
            }
            else {
                // --- COMBO DAMAGE SCALING ---
                _isBeingComboed = true;
                _comboHitCount++;

                // GGACR initial (forced) proration: if this move STARTS the combo
                // and has InitialProration < 1.0, set the combo scaling floor.
                // This only applies on the first hit of a combo.
                if (_comboHitCount == 1 && move.Damage.InitialProration > 0f
                    && move.Damage.InitialProration < 1f) {
                    _comboDamageScaling = move.Damage.InitialProration;
                }

                float scaling = _comboDamageScaling;

                // Apply the MOVE's per-hit damage scaling to reduce future hits
                if (move.Damage.DamageScaling > 0f && move.Damage.DamageScaling < 1f)
                    _comboDamageScaling *= move.Damage.DamageScaling;

                // Standard GGXX proration: after hit 1, each subsequent hit
                // scales by an additional 10% reduction (floor at 10%)
                if (_comboHitCount > 1) {
                    float proration = Mathf.Max(0.1f, 1f - (_comboHitCount - 1) * 0.1f);
                    scaling *= proration;
                }

                int rawDamage = Mathf.RoundToInt(move.Damage.BaseDamage * Character.DefenseModifier);
                int scaledDamage = Mathf.Max(1, Mathf.RoundToInt(rawDamage * scaling));
                _health -= scaledDamage;

                _stunMeter -= move.Damage.StunDamage;

                if (_audioSource != null && move.HitSound != null)
                    _audioSource.PlayOneShot(move.HitSound);

                if (move.HitEffectPrefab != null)
                    Instantiate(move.HitEffectPrefab, VFXPosition, Quaternion.identity);

                // --- KO CHECK ---
                if (_health <= 0) {
                    _health = 0;
                    SetState(PlayerState.KO);
                    return;
                }

                // --- DIZZY CHECK ---
                if (_stunMeter <= 0) {
                    _stunMeter = 0;
                    SetState(PlayerState.Stunned);
                    return;
                }

                // --- HIT EFFECT HANDLING ---
                ApplyHitEffect(move);
            }
        }

        /// <summary>
        /// Called by MatchManager when a projectile hits this player.
        /// Projectiles have their own hitstun/blockstun/damage values
        /// separate from the MoveData system.
        /// </summary>
        public void TakeProjectileHit(Projectile proj, bool blocked,
            BlockType blockType = BlockType.Normal) {
            _currentMove = null;
            _moveStartedAirborne = false;
            _framesSinceLastHit = 0;

            if (blocked) {
                int chipDamage = Mathf.RoundToInt(proj.ChipDamage * Character.DefenseModifier);
                if (blockType == BlockType.FaultlessDefense)
                    chipDamage = 0;

                _health -= chipDamage;

                bool wasCrouchingProj = (_state == PlayerState.Crouching);
                _isAirBlocking = (_state == PlayerState.Airborne || _state == PlayerState.Launched);
                SetState(PlayerState.Blockstun);

                // Play stance-specific block animation
                string projBlockAnim = _isAirBlocking ? "AirBlockstun"
                    : wasCrouchingProj ? "CrouchBlockstun"
                    : "Blockstun";
                if (_animator != null && HasAnimatorState(projBlockAnim))
                    _animator.Play(projBlockAnim, 0, 0f);

                // Use projectile's blockstun value directly
                _stunFramesRemaining = proj.Blockstun;

                if (blockType == BlockType.FaultlessDefense)
                    _faultlessDefenseActive = true;
                else if (blockType == BlockType.InstantBlock) {
                    _instantBlocked = true;
                    // IB reduces blockstun by a few frames
                    _stunFramesRemaining = Mathf.Max(1, _stunFramesRemaining - 3);
                }

                if (_audioSource != null && proj.BlockSound != null)
                    _audioSource.PlayOneShot(proj.BlockSound);
            }
            else {
                // Projectile hit
                bool wasCrouchingHit = (_state == PlayerState.Crouching);
                _isBeingComboed = true;
                _comboHitCount++;

                int rawDmg = Mathf.RoundToInt(proj.Damage * Character.DefenseModifier);
                float scaling = _comboDamageScaling;
                if (_comboHitCount > 1) {
                    float proration = Mathf.Max(0.1f, 1f - (_comboHitCount - 1) * 0.1f);
                    scaling *= proration;
                }
                int scaledDmg = Mathf.Max(1, Mathf.RoundToInt(rawDmg * scaling));
                _health -= scaledDmg;

                if (_audioSource != null && proj.HitSound != null)
                    _audioSource.PlayOneShot(proj.HitSound);

                if (_health <= 0) {
                    _health = 0;
                    SetState(PlayerState.KO);
                    return;
                }

                // Projectile hitstun — enter Hitstun state
                _stunFramesRemaining = proj.Hitstun;
                SetState(PlayerState.Hitstun);
                PlayStanceHitstunAnim(wasCrouchingHit);
            }

            // Hitstop for projectile hits (use a base value)
            _hitstopRemaining = 8;
        }

        /// <summary>
        /// Applies the move's OnHitEffect to determine the defender's state
        /// after being hit. Different effects create different reactions:
        /// launches, knockdowns, crumples, wall bounces, etc.
        ///
        /// GGACR hitstun rules:
        ///   - Grounded hitstun depends on standing vs crouching state.
        ///   - Airborne hits use untechable time (doubled on counter hit).
        ///   - Stagger adds 30% more hitstun.
        /// </summary>
        private void ApplyHitEffect(MoveData move) {
            // Determine the correct hitstun based on stance before being hit
            bool wasCrouching = (_state == PlayerState.Crouching);
            int groundHitstun = wasCrouching
                ? move.Frames.GetCrouchingHitstun()
                : move.Frames.GetStandingHitstun();
            int airUntechable = move.Frames.GetUntechableTime(_lastHitWasCounterHit);

            switch (move.OnHitEffect) {
                case HitEffect.Launch:
                    // Pop the defender into the air for juggle follow-ups.
                    // Uses air velocity (not pushback) since they're airborne.
                    _velocityX = -move.HitPushbackVelocity.x * _detector.FacingSign * 0.5f;
                    _velocityY = move.HitPushbackVelocity.y;
                    if (_velocityY <= 0) _velocityY = 0.15f;
                    TrySetGroundY();
                    SetState(PlayerState.Launched);
                    _stunFramesRemaining = airUntechable;
                    break;

                case HitEffect.Knockdown:
                    _hardKnockdown = true;
                    SetState(PlayerState.Knockdown);
                    StartHitPushback(move, HARD_KNOCKDOWN_FRAMES);
                    break;

                case HitEffect.SoftKnockdown:
                    _hardKnockdown = false;
                    SetState(PlayerState.Knockdown);
                    StartHitPushback(move, SOFT_KNOCKDOWN_FRAMES);
                    break;

                case HitEffect.Crumple:
                    SetState(PlayerState.Crumple);
                    break;

                case HitEffect.WallBounce:
                    SetState(PlayerState.Hitstun);
                    _stunFramesRemaining = groundHitstun;
                    StartHitPushback(move, groundHitstun, 1.5f);
                    PlayStanceHitstunAnim(wasCrouching);
                    break;

                case HitEffect.GroundBounce:
                    // Air physics — similar to Launch but with a bounce arc
                    _velocityX = -move.HitPushbackVelocity.x * _detector.FacingSign * 0.3f;
                    _velocityY = move.HitPushbackVelocity.y * 1.5f;
                    if (_velocityY <= 0) _velocityY = 0.2f;
                    TrySetGroundY();
                    SetState(PlayerState.Launched);
                    _stunFramesRemaining = airUntechable;
                    break;

                case HitEffect.Stagger:
                    SetState(PlayerState.Hitstun);
                    _stunFramesRemaining = Mathf.RoundToInt(groundHitstun * 1.3f);
                    StartHitPushback(move, _stunFramesRemaining);
                    PlayStanceHitstunAnim(wasCrouching);
                    break;

                case HitEffect.SpinOut:
                    // Air physics — spinning launch
                    _velocityX = -move.HitPushbackVelocity.x * _detector.FacingSign * 0.7f;
                    _velocityY = move.HitPushbackVelocity.y * 0.8f;
                    if (_velocityY <= 0) _velocityY = 0.1f;
                    TrySetGroundY();
                    SetState(PlayerState.Launched);
                    _stunFramesRemaining = airUntechable;
                    break;

                case HitEffect.None:
                default:
                    // Standard grounded hitstun with velocity-based slide
                    SetState(PlayerState.Hitstun);
                    _stunFramesRemaining = groundHitstun;
                    StartHitPushback(move, groundHitstun);
                    PlayStanceHitstunAnim(wasCrouching);
                    break;
            }
        }

        /// <summary>
        /// Plays the correct hitstun animation based on the defender's stance
        /// at the moment of impact. Standing uses "Hitstun", crouching uses
        /// "CrouchHitstun". Falls back to the default if the variant doesn't
        /// exist in the Animator.
        /// </summary>
        private void PlayStanceHitstunAnim(bool wasCrouching) {
            if (_animator == null) return;

            string hitAnim = wasCrouching ? "CrouchHitstun" : "Hitstun";
            if (HasAnimatorState(hitAnim))
                _animator.Play(hitAnim, 0, 0f);
            else if (wasCrouching && HasAnimatorState("Hitstun"))
                _animator.Play("Hitstun", 0, 0f); // fallback if CrouchHitstun doesn't exist
        }

        /// <summary>
        /// Starts velocity-based pushback. The defender slides during stun,
        /// decelerating linearly so velocity reaches zero when stun expires.
        ///
        /// initialVelocity: starting speed (units/frame). Positive = away from attacker.
        /// stunDuration: how many frames the stun lasts (friction is derived from this).
        /// multiplier: extra scale factor (e.g. 1.5x for wall bounce).
        /// </summary>
        private void StartPushback(float initialVelocity, int stunDuration, float multiplier = 1f) {
            // Push direction: away from attacker (negative facing = away)
            _pushbackVelocityX = -initialVelocity * _detector.FacingSign * multiplier;

            // Calculate friction so velocity reaches 0 at exactly stunDuration frames.
            // Using linear deceleration: friction = initialVelocity / stunDuration
            if (stunDuration > 0 && Mathf.Abs(initialVelocity) > 0.0001f)
                _pushbackFriction = Mathf.Abs(_pushbackVelocityX) / stunDuration;
            else
                _pushbackFriction = 0f;
        }

        /// <summary>
        /// Sets pushback from a move's hit values, automatically calculating
        /// friction from the stun duration.
        /// </summary>
        private void StartHitPushback(MoveData move, int stunDuration, float multiplier = 1f) {
            StartPushback(move.HitPushbackVelocity.x, stunDuration, multiplier);
        }

        /// <summary>
        /// Sets pushback from a move's block values, automatically calculating
        /// friction from blockstun duration.
        /// </summary>
        private void StartBlockPushback(MoveData move, int stunDuration, float fdMultiplier = 1f) {
            StartPushback(move.BlockPushbackVelocity.x, stunDuration, fdMultiplier);
        }

        /// <summary>
        /// Ticks pushback each frame during hitstun/blockstun.
        /// Applies the current pushback velocity and decelerates.
        /// Call this in TickState for Hitstun, Blockstun, Knockdown, etc.
        /// </summary>
        private void TickPushback() {
            if (Mathf.Abs(_pushbackVelocityX) < 0.0001f) return;

            // Apply current velocity
            Vector3 pos = transform.position;
            pos.x += _pushbackVelocityX;
            transform.position = pos;

            // Decelerate toward zero
            if (_pushbackVelocityX > 0f) {
                _pushbackVelocityX -= _pushbackFriction;
                if (_pushbackVelocityX < 0f) _pushbackVelocityX = 0f;
            }
            else {
                _pushbackVelocityX += _pushbackFriction;
                if (_pushbackVelocityX > 0f) _pushbackVelocityX = 0f;
            }
        }

        /// <summary>
        /// Called by MatchManager to check if this hit exceeds juggle limits.
        /// Returns true if the hit should be allowed.
        /// </summary>
        public bool CanBeJuggled(MoveData move) {
            if (_state != PlayerState.Launched) return true; // not in juggle state

            // Check juggle point limit (GGXX doesn't have a strict one,
            // but this provides the infrastructure)
            int maxJuggle = 15; // configurable per character if needed
            return (_jugglePointsUsed + move.JuggleCost) <= maxJuggle;
        }

        /// <summary>
        /// Called by MatchManager after a juggle hit connects to track points.
        /// </summary>
        public void ConsumeJugglePoints(int cost) {
            _jugglePointsUsed += cost;
        }

        // ──────────────────────────────────────
        //  FAULTLESS DEFENSE
        // ──────────────────────────────────────

        /// <summary>
        /// Called every frame during blockstun by MatchManager or internally.
        /// FD is active when the player holds two buttons while blocking
        /// and has meter to spend.
        /// GGACR input: hold any two attack buttons while blocking.
        /// </summary>
        public void TickFaultlessDefense(InputFrame input) {
            _faultlessDefenseActive = false;

            if (_state != PlayerState.Blockstun) return;

            // FD requires holding at least 2 buttons simultaneously
            int heldCount = CountHeldButtons(input.HeldButtons);
            if (heldCount < 2) return;

            // Must have meter to spend
            if (_meter <= 0) return;

            _faultlessDefenseActive = true;
            _meter = Mathf.Max(0, _meter - Character.FaultlessDefenseCostPerFrame);
        }

        private int CountHeldButtons(ButtonFlags flags) {
            int count = 0;
            if (flags.HasFlag(ButtonFlags.Punch)) count++;
            if (flags.HasFlag(ButtonFlags.Kick)) count++;
            if (flags.HasFlag(ButtonFlags.Slash)) count++;
            if (flags.HasFlag(ButtonFlags.HeavySlash)) count++;
            if (flags.HasFlag(ButtonFlags.Dust)) count++;
            return count;
        }

        // ──────────────────────────────────────
        //  INSTANT BLOCK
        // ──────────────────────────────────────

        /// <summary>
        /// Called by MatchManager when transitioning to blockstun.
        /// Checks whether the block input was within the IB window.
        /// If so, sets _instantBlocked = true so MatchManager uses IB blockstun.
        /// </summary>
        public void CheckInstantBlock(int currentGameFrame) {
            int window = Character != null ? Character.InstantBlockWindow : 8;
            _instantBlocked = (currentGameFrame - _blockInputFrame) <= window;
        }

        /// <summary>
        /// Called when the player starts holding back (enters a blocking state).
        /// Records the frame for IB window comparison.
        /// </summary>
        public void RecordBlockInput(int gameFrame) {
            // Only record if we're transitioning TO blocking (not while already blocking)
            if (_state != PlayerState.WalkBack && _state != PlayerState.Blockstun)
                _blockInputFrame = gameFrame;
        }

        // ──────────────────────────────────────
        //  AIR TECHING
        // ──────────────────────────────────────

        /// <summary>
        /// Called each frame in the Launched state to check whether
        /// the player can tech out of the air once untechable time expires.
        /// </summary>
        private void TickAirTech(InputFrame input) {
            if (_state != PlayerState.Launched) return;

            // Untechable countdown is tracked via _stunFramesRemaining
            if (_stunFramesRemaining > 0) {
                _stunFramesRemaining--;
                _canAirTech = false;
                return;
            }

            // Untechable time expired — player can now tech
            _canAirTech = true;

            // Any button press triggers air tech
            if (input.PressedButtons != ButtonFlags.None) {
                // Determine tech direction from held direction
                if (input.Direction.HasFlag(DirectionInput.Forward))
                    _airTechDir = AirTechDirection.Forward;
                else if (input.Direction.HasFlag(DirectionInput.Back))
                    _airTechDir = AirTechDirection.Back;
                else
                    _airTechDir = AirTechDirection.Neutral;

                ExecuteAirTech();
            }
        }

        private void ExecuteAirTech() {
            SetState(PlayerState.AirTeching);
            _invincibleFramesRemaining = 8; // brief invincibility during tech

            // Apply directional velocity based on tech direction
            switch (_airTechDir) {
                case AirTechDirection.Forward:
                    _velocityX = Character.WalkForwardSpeed * 0.5f * _detector.FacingSign;
                    break;
                case AirTechDirection.Back:
                    _velocityX = -Character.WalkBackwardSpeed * 0.5f * _detector.FacingSign;
                    break;
                default:
                    _velocityX = 0f;
                    break;
            }

            _velocityY = 0.03f; // slight upward float on tech
            _stunFramesRemaining = 0;
            _canAirTech = false;
            ResetComboState();
        }

        // ──────────────────────────────────────
        //  GROUND TECH (Knockdown Recovery)
        // ──────────────────────────────────────

        /// <summary>
        /// Enhanced knockdown handling with ground tech options.
        /// Called from TickState during Knockdown.
        /// - Quick rise: any button during soft knockdown
        /// - Back roll: Back + button during soft knockdown
        /// </summary>
        private GroundTechType CheckGroundTech(InputFrame input) {
            if (_hardKnockdown) return GroundTechType.None;
            if (_stateFrameCounter < SOFT_KNOCKDOWN_FRAMES / 2) return GroundTechType.None;

            if (input.PressedButtons == ButtonFlags.None) return GroundTechType.None;

            // Back + button = back roll
            if (input.Direction.HasFlag(DirectionInput.Back))
                return GroundTechType.BackRoll;

            // Button only = quick rise
            return GroundTechType.QuickRise;
        }

        private void ExecuteBackRoll() {
            SetState(PlayerState.BackRoll);
            _invincibleFramesRemaining = Character.BackRollInvincibleFrames;
        }

        // ──────────────────────────────────────
        //  THROW TECH
        // ──────────────────────────────────────

        /// <summary>
        /// Called when this player initiates a throw attempt.
        /// Enters ThrowStartup state. MatchManager calls ResolveThrows
        /// each frame to check for tech or connect.
        /// </summary>
        public void BeginThrowAttempt(PlayerController target) {
            _throwTarget = target;
            _throwAttemptFrame = _gameFrame;
            SetState(PlayerState.ThrowStartup);
        }

        /// <summary>The game frame this throw attempt began (for tech window calculation).</summary>
        public int ThrowAttemptFrame => _throwAttemptFrame;

        /// <summary>The target being thrown (null if not in ThrowStartup).</summary>
        public PlayerController ThrowTarget => _throwTarget;

        /// <summary>
        /// Checks if the given input contains a throw tech (P+K pressed).
        /// Called by MatchManager on the defender each frame during the tech window.
        /// </summary>
        public static bool IsThrowTechInput(InputFrame input) {
            return input.PressedButtons.HasFlag(ButtonFlags.Punch)
                && input.PressedButtons.HasFlag(ButtonFlags.Kick);
        }

        /// <summary>
        /// Called by MatchManager when the throw is successfully teched.
        /// Both players return to neutral with a brief pushback apart.
        /// </summary>
        public void OnThrowTeched() {
            _throwTarget = null;
            SetState(PlayerState.Idle);
            // Small pushback on tech — push away from opponent
            transform.position += new Vector3(
                -0.3f * _detector.FacingSign, 0f, 0f);
        }

        /// <summary>
        /// Called by MatchManager when the throw whiffs (target was in
        /// an unthrowable state, out of range, etc.).
        /// Attacker enters a brief punishable recovery.
        /// </summary>
        public void OnThrowWhiff() {
            _throwTarget = null;
            _currentMove = null;
            // Use Hitstun state as a generic "stuck" timer — attacker is
            // punishable for a short window after a whiffed throw.
            SetState(PlayerState.Hitstun);
            _stunFramesRemaining = 12; // ~12f of whiff recovery
        }

        /// <summary>
        /// Called by MatchManager when the throw connects (not teched).
        /// Applies damage and puts the defender in Thrown state.
        /// The attacker transitions to executing the throw move animation.
        /// </summary>
        public void OnThrown(int thrownDuration) {
            _stunFramesRemaining = thrownDuration;
            _currentMove = null;
            SetState(PlayerState.Thrown);
        }

        /// <summary>
        /// Called by MatchManager on the ATTACKER when their throw connects.
        /// Transitions from ThrowStartup to executing the throw move.
        /// </summary>
        public void ExecuteThrowConnect() {
            MoveData throwMove = _currentMove ?? Character.Moveset.ForwardThrow;
            _throwTarget = null;
            if (throwMove != null)
                ExecuteMove(throwMove);
            else
                SetState(PlayerState.Idle);
        }

        // ──────────────────────────────────────
        //  GUARD CRUSH
        // ──────────────────────────────────────

        /// <summary>
        /// Called by MatchManager when a guard-crush move connects on block,
        /// or when another guard crush condition is met.
        /// Puts the defender in a long stagger with no blocking.
        /// </summary>
        public void ApplyGuardCrush() {
            SetState(PlayerState.GuardCrush);
            _stunFramesRemaining = Character.GuardCrushStaggerFrames;
        }

        // ──────────────────────────────────────
        //  WAKEUP REVERSAL
        // ──────────────────────────────────────

        /// <summary>
        /// During the wakeup state, allows reversal specials/supers
        /// within the reversal window. The reversal comes out on the
        /// first actionable frame with invincibility if the move has it.
        /// </summary>
        private bool TryWakeupReversal(InputFrame input) {
            if (_state != PlayerState.Wakeup) return false;
            if (_stateFrameCounter > Character.WakeupReversalWindow) return false;

            // Check supers first (highest priority reversal)
            if (_moveset.SuperArts != null) {
                foreach (var super in _moveset.SuperArts) {
                    if (super.Move != null && _parser.TryMatchMove(super.Move)) {
                        if (_meter >= super.CostPerUse) {
                            _meter -= super.CostPerUse;
                            ResetComboState();
                            SetState(PlayerState.Idle); // brief transition
                            ExecuteMove(super.Move);
                            return true;
                        }
                    }
                }
            }

            // Check specials
            if (_specialsSorted != null) {
                MoveData special = _parser.TryMatchFirst(_specialsSorted);
                if (special != null && special.MeterCost <= _meter) {
                    _meter -= special.MeterCost;
                    ResetComboState();
                    SetState(PlayerState.Idle);
                    ExecuteMove(special);
                    return true;
                }
            }

            return false;
        }

        // ──────────────────────────────────────
        //  TENSION GAUGE (GGXX)
        // ──────────────────────────────────────

        public void AddMeter(int amount) {
            if (_inNegativePenalty)
                amount /= 2;

            int max = Character.GetTotalMeterCapacity();
            _meter = Mathf.Min(_meter + amount, max);
        }

        private void TickTension() {
            if (Character == null) return;

            if (_inNegativePenalty) {
                _negativePenaltyTimer--;
                _meter = Mathf.Max(0, _meter - Character.TensionDrainRate);

                if (_negativePenaltyTimer <= 0) {
                    _inNegativePenalty = false;
                    _idleNegativeTimer = 0;
                }
                return;
            }

            switch (_state) {
                case PlayerState.WalkForward:
                    AddMeter(Character.TensionGainForwardWalk);
                    _idleNegativeTimer = 0;
                    break;

                case PlayerState.DashForward:
                    AddMeter(Character.TensionGainForwardDash);
                    _idleNegativeTimer = 0;
                    break;

                case PlayerState.Startup:
                case PlayerState.Active:
                    AddMeter(Character.TensionGainPerAttackFrame);
                    _idleNegativeTimer = 0;
                    break;

                case PlayerState.WalkBack:
                    _idleNegativeTimer++;
                    break;

                case PlayerState.Idle:
                    _idleNegativeTimer++;
                    break;

                default:
                    break;
            }

            if (_idleNegativeTimer >= Character.TensionPulseThreshold) {
                _inNegativePenalty = true;
                _negativePenaltyTimer = Character.NegativePenaltyDuration;
            }
        }

        // ──────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────

        private void SetState(PlayerState newState) {
            if (_state == newState) return;
            _state = newState;
            _stateFrameCounter = 0;

            if (_animator != null && _currentMove == null) {
                string stateName = GetAnimationStateForPlayerState(newState);
                if (stateName != null && HasAnimatorState(stateName))
                    _animator.Play(stateName, 0, 0f);
            }
        }

        private string GetAnimationStateForPlayerState(PlayerState state) {
            switch (state) {
                case PlayerState.Idle: return "Idle";
                case PlayerState.WalkForward: return "WalkForward";
                case PlayerState.WalkBack: return "WalkBack";
                case PlayerState.Crouching: return "Crouch";
                case PlayerState.PreJump: return "PreJump";
                case PlayerState.Airborne: return "Jump";
                case PlayerState.JumpLanding: return "Landing";
                case PlayerState.AirDashForward: return "AirDashForward";
                case PlayerState.AirDashBack: return "AirDashBack";
                case PlayerState.DashForward: return "DashForward";
                case PlayerState.DashBack: return "DashBack";
                case PlayerState.Hitstun: return "Hitstun";
                case PlayerState.Blockstun: return "Blockstun";
                case PlayerState.Knockdown: return "Knockdown";
                case PlayerState.Wakeup: return "Wakeup";
                case PlayerState.Stunned: return "Stunned";
                case PlayerState.Launched: return "Launched";
                case PlayerState.Crumple: return "Crumple";
                case PlayerState.AirTeching: return "AirTech";
                case PlayerState.BackRoll: return "BackRoll";
                case PlayerState.ThrowStartup: return "ThrowStartup";
                case PlayerState.Thrown: return "Thrown";
                case PlayerState.GuardCrush: return "GuardCrush";
                case PlayerState.KO: return "KO";
                default: return null;
            }
        }

        private bool HasAnimatorState(string stateName) {
            if (_animator == null) return false;
            return _animator.HasState(0, Animator.StringToHash(stateName));
        }

        private bool IsMovementState() {
            return _state == PlayerState.Idle
                || _state == PlayerState.WalkForward
                || _state == PlayerState.WalkBack
                || _state == PlayerState.Crouching;
        }

        private MoveUsableState GetCurrentStance() {
            switch (_state) {
                case PlayerState.Crouching: return MoveUsableState.Crouching;
                case PlayerState.Airborne:
                case PlayerState.Launched: return MoveUsableState.Airborne;
                default: return MoveUsableState.Standing;
            }
        }
    }
}