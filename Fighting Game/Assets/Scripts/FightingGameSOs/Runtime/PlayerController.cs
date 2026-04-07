using UnityEngine;
using FightingGame.Data;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// The core player state machine. Owns the InputBuffer and InputParser,
    /// reads from InputDetector, and resolves moves from the CharacterData asset.
    ///
    /// Designed to be ticked at a fixed 60fps from a central MatchManager,
    /// NOT from Update(). This guarantees frame-deterministic behavior
    /// required for fighting games.
    ///
    /// GGXX HITSTOP MODEL:
    ///   Attacker and defender have SEPARATE hitstop counters.
    ///   On hit: attacker freezes for AttackerHitstop frames,
    ///           defender freezes for DefenderHitstop frames.
    ///   On block: attacker freezes for AttackerHitstop frames,
    ///             defender freezes for DefenderBlockstop frames.
    ///   After hitstop ends, attacker continues remaining active + recovery,
    ///   and defender enters hitstun or blockstun.
    ///
    ///   Advantage = defender's stun - (attacker's remainingActive + recovery)
    /// </summary>
    [RequireComponent(typeof(InputDetector))]
    public class PlayerController : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR — drag your character asset here
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

        // Cached from Character.Moveset at startup — avoids
        // repeated array allocations every frame.
        private MoveData[] _specialsSorted;
        private MovesetData _moveset;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        public enum PlayerState {
            Idle,
            WalkForward,
            WalkBack,
            Crouching,
            PreJump,
            Airborne,
            JumpLanding,
            DashForward,
            DashBack,
            Startup,        // move is winding up
            Active,         // hitbox is live
            Recovery,       // move is cooling down
            Hitstun,
            Blockstun,
            Knockdown,
            Wakeup,
            Stunned,        // dizzy
            Parry,          // successful parry freeze
            ParryRecovery,  // whiffed parry vulnerability
            KO
        }

        [Header("Debug (read-only)")]
        [SerializeField] private PlayerState _state = PlayerState.Idle;
        [SerializeField] private int _gameFrame;
        [SerializeField] private int _stateFrameCounter; // frames spent in current state
        [SerializeField] private string _currentMoveName;

        private MoveData _currentMove;
        private int _moveFrame;

        // GGXX per-player hitstop: each player has their own freeze counter.
        // Set by MatchManager when a hit connects. While > 0, this player
        // does not advance their state or move frames.
        private int _hitstopRemaining;

        // Stun duration tracking: how many frames of hitstun/blockstun remain.
        private int _stunFramesRemaining;

        // Jump physics
        private float _velocityX;
        private float _velocityY;
        private float _groundY; // cached from position at jump start

        // Health / meter / stun
        private int _health;
        private int _meter;
        private int _stunMeter;

        // ──────────────────────────────────────
        //  PUBLIC ACCESSORS
        // ──────────────────────────────────────

        public PlayerState State => _state;
        public int GameFrame => _gameFrame;
        public int Health => _health;
        public int Meter => _meter;
        public MoveData CurrentMove => _currentMove;
        public int MoveFrame => _moveFrame;
        public int FacingSign => _detector.FacingSign;
        public bool InHitstop => _hitstopRemaining > 0;

        /// <summary>
        /// Called by MatchManager after instantiating the character's
        /// visual prefab, to link the animator and audio source.
        /// </summary>
        public void SetVisualReferences(Animator animator, AudioSource audioSource) {
            _animator = animator;
            _audioSource = audioSource;
        }

        /// <summary>
        /// Called by MatchManager when a hit connects. Sets how many frames
        /// this player freezes in place (GGXX per-player hitstop).
        /// </summary>
        public void ApplyHitstop(int frames) {
            _hitstopRemaining = frames;
        }

        // ──────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────

        private void Awake() {
            _detector = GetComponent<InputDetector>();
            _buffer = new InputBuffer(60);
            _parser = new InputParser(_buffer);
        }

        /// <summary>
        /// Called by MatchManager at round start (after character selection
        /// determines which CharacterData is assigned).
        /// </summary>
        public void Initialize() {
            if (Character == null) {
                Debug.LogError($"[PlayerController] No CharacterData assigned on {gameObject.name}. " +
                    "Either assign one on the prefab for testing, or go through Character Select.");
                return;
            }

            _moveset = Character.Moveset;
            if (_moveset != null)
                _specialsSorted = _moveset.GetAllSpecialsSorted();
            else
                Debug.LogWarning($"[PlayerController] No Moveset assigned on {Character.name}. Moves won't work.");
            _health = Character.MaxHealth;
            _meter = 0;
            _stunMeter = Character.MaxStun;
            _gameFrame = 0;
            _hitstopRemaining = 0;
            _stunFramesRemaining = 0;
            _state = PlayerState.Idle;
            _buffer.Clear();
        }

        // ──────────────────────────────────────
        //  GAME TICK (called from MatchManager.FixedUpdate)
        // ──────────────────────────────────────

        /// <summary>
        /// Advance the player by one game frame.
        /// Call order across both players must be deterministic.
        /// </summary>
        public void GameTick() {
            _gameFrame++;

            // --- HITSTOP: freeze in place ---
            // During hitstop we still poll input (so buffered inputs
            // register) but we don't advance state or move frames.
            if (_hitstopRemaining > 0) {
                _hitstopRemaining--;

                // Still poll input so the buffer captures it
                InputFrame frozenInput = _detector.Poll(_gameFrame);
                _buffer.Push(frozenInput);

                // When hitstop ends for the DEFENDER, transition into stun
                if (_hitstopRemaining == 0 && _stunFramesRemaining > 0) {
                    // _state was already set to Hitstun/Blockstun by TakeHit,
                    // _stunFramesRemaining was set — now the stun countdown begins.
                }

                return;
            }

            _stateFrameCounter++;

            // 1. Poll input from hardware → push to buffer
            InputFrame input = _detector.Poll(_gameFrame);
            _buffer.Push(input);

            // 2. Advance current move / state timer
            if (_currentMove != null) {
                _moveFrame++;
                UpdateMovePhase();
            }

            // 3. Tick state-specific logic (dash timers, jump arcs, stun countdowns, etc.)
            TickState(input);

            // 4. Attempt to resolve a new move (only when actionable)
            if (CanAct())
                ResolveInput(input);

            // 5. If still idle/walking, handle movement
            if (IsMovementState())
                HandleMovement(input);

            // Debug
            _currentMoveName = _currentMove != null ? _currentMove.MoveName : "—";
        }

        // ──────────────────────────────────────
        //  MOVE RESOLUTION (priority order)
        // ──────────────────────────────────────

        /// <summary>
        /// Checks for input matches in strict priority order:
        /// Super > EX Special > Special > Command Normal > Normal
        ///
        /// The first match wins. Arrays are pre-sorted by InputPriority.
        /// </summary>
        private void ResolveInput(InputFrame input) {
            if (_moveset == null) return;

            // --- PARRY ---
            if (TryParry(input)) return;

            // --- SUPERS / OVERDRIVES (highest motion priority) ---
            if (_moveset.SuperArts != null) {
                // In GGXX all supers are available. Check all of them.
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

            // --- SPECIALS + EX (sorted by InputPriority desc) ---
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

            // --- GATLING / TARGET COMBO continuation ---
            if (TryTargetCombo()) return;

            // --- COMMAND NORMALS ---
            if (_moveset.CommandNormals != null) {
                MoveData cmd = _parser.TryMatchFirst(_moveset.CommandNormals);
                if (cmd != null) {
                    ExecuteMove(cmd);
                    return;
                }
            }

            // --- THROW (3S: P+K simultaneously) ---
            if (TryThrow(input)) return;

            // --- TAUNT (HS+D simultaneously) ---
            if (TryTaunt()) return;

            // --- NORMALS ---
            MoveUsableState stance = GetCurrentStance();
            foreach (ButtonInput btn in System.Enum.GetValues(typeof(ButtonInput))) {
                if (btn == ButtonInput.None) continue;
                if (!_parser.MatchButton(btn)) continue;

                MoveData normal = _moveset.GetNormal(btn, stance);
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
            SetState(PlayerState.Startup);

            // Trigger animation
            if (_animator != null && !string.IsNullOrEmpty(move.AnimationStateName)
                && HasAnimatorState(move.AnimationStateName))
                _animator.Play(move.AnimationStateName, 0, 0f);

            // Play swing sound
            if (_audioSource != null && move.SwingSound != null)
                _audioSource.PlayOneShot(move.SwingSound);
        }

        /// <summary>
        /// Updates the player state based on which phase of the current
        /// move we're in (startup → active → recovery → idle).
        ///
        /// Startup does NOT include the first active frame:
        ///   Startup phase:   frame 0 to (Startup - 1)
        ///   Active phase:    frame Startup to (Startup + Active - 1)
        ///   Recovery phase:  frame (Startup + Active) to (TotalFrames - 1)
        /// </summary>
        private void UpdateMovePhase() {
            int firstActive = _currentMove.Frames.FirstActiveFrame;  // Startup (0-indexed)
            int lastActive = _currentMove.Frames.LastActiveFrame;    // Startup + Active - 1
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
                // Move finished
                _currentMove = null;
                _moveFrame = 0;
                SetState(PlayerState.Idle);
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

        // ──────────────────────────────────────
        //  MOVEMENT
        // ──────────────────────────────────────

        // Stored during PreJump so we know which direction to jump
        private int _jumpDirectionIntent; // -1 = back, 0 = neutral, 1 = forward

        // Dash detection: double-tap forward or back within a window.
        // Tracks the frame when forward/back was last RELEASED (went to neutral),
        // so a second tap within the window triggers a dash.
        private const int DASH_INPUT_WINDOW = 10; // frames to double-tap
        private int _lastForwardReleaseFrame;
        private int _lastBackReleaseFrame;
        private bool _wasHoldingForward;
        private bool _wasHoldingBack;

        private void HandleMovement(InputFrame input) {
            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            bool holdBack = input.Direction.HasFlag(DirectionInput.Back);
            bool holdDown = input.Direction.HasFlag(DirectionInput.Down);
            bool holdUp = input.Direction.HasFlag(DirectionInput.Up);

            // --- DASH DETECTION (double-tap) ---
            // Track when forward/back is released to neutral
            if (!holdForward && _wasHoldingForward)
                _lastForwardReleaseFrame = _gameFrame;
            if (!holdBack && _wasHoldingBack)
                _lastBackReleaseFrame = _gameFrame;

            _wasHoldingForward = holdForward;
            _wasHoldingBack = holdBack;

            // Check for double-tap dash (second tap within window of release)
            if (holdForward && !holdDown && !holdUp
                && (_gameFrame - _lastForwardReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastForwardReleaseFrame > 0) {
                _lastForwardReleaseFrame = 0; // consume the input
                SetState(PlayerState.DashForward);
                return;
            }

            if (holdBack && !holdDown && !holdUp
                && (_gameFrame - _lastBackReleaseFrame) <= DASH_INPUT_WINDOW
                && _lastBackReleaseFrame > 0) {
                _lastBackReleaseFrame = 0;
                SetState(PlayerState.DashBack);
                return;
            }

            // --- NORMAL MOVEMENT ---
            if (holdDown) {
                SetState(PlayerState.Crouching);
            }
            else if (holdUp && _state != PlayerState.PreJump && _state != PlayerState.Airborne) {
                // Lock in jump direction at the moment Up is pressed
                if (holdForward) _jumpDirectionIntent = 1;
                else if (holdBack) _jumpDirectionIntent = -1;
                else _jumpDirectionIntent = 0;

                _groundY = transform.position.y;
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
                    // PreJump is grounded commit frames — can't block, can't act.
                    // After the prejump frames expire, launch into the air.
                    if (_stateFrameCounter >= Character.PreJumpFrames) {
                        // Set jump velocity based on direction locked at jump start
                        _velocityY = Character.JumpHeight;
                        _velocityX = _jumpDirectionIntent * Character.JumpForwardSpeed * _detector.FacingSign;

                        SetState(PlayerState.Airborne);
                    }
                    break;

                case PlayerState.Airborne:
                    // Apply gravity each frame
                    _velocityY -= Character.Gravity;

                    // Move the character
                    Vector3 pos = transform.position;
                    pos.x += _velocityX;
                    pos.y += _velocityY;

                    // Landing check — have we reached or passed the ground?
                    if (pos.y <= _groundY) {
                        pos.y = _groundY;
                        _velocityX = 0f;
                        _velocityY = 0f;

                        if (Character.JumpLandingFrames > 0)
                            SetState(PlayerState.JumpLanding);
                        else
                            SetState(PlayerState.Idle);
                    }

                    transform.position = pos;
                    break;

                case PlayerState.JumpLanding:
                    if (_stateFrameCounter >= Character.JumpLandingFrames)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.DashForward: {
                        // Move forward each frame (constant speed over dash duration)
                        float dashSpeed = Character.DashDistance / Character.DashDuration;
                        transform.position += new Vector3(
                            dashSpeed * _detector.FacingSign, 0, 0);

                        if (_stateFrameCounter >= Character.DashDuration)
                            SetState(PlayerState.Idle);
                        break;
                    }

                case PlayerState.DashBack: {
                        // Move backward each frame
                        float backDashSpeed = Character.BackDashDistance / Character.BackDashDuration;
                        transform.position += new Vector3(
                            -backDashSpeed * _detector.FacingSign, 0, 0);

                        if (_stateFrameCounter >= Character.BackDashDuration)
                            SetState(PlayerState.Idle);
                        break;
                    }

                case PlayerState.Hitstun:
                    _stunFramesRemaining--;
                    if (_stunFramesRemaining <= 0)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Blockstun:
                    _stunFramesRemaining--;
                    if (_stunFramesRemaining <= 0)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Stunned:
                    // Dizzy state — count down stun duration, then recover
                    if (_stateFrameCounter >= Character.StunDuration) {
                        _stunMeter = Character.MaxStun; // reset stun meter on recovery
                        SetState(PlayerState.Idle);
                    }
                    break;

                case PlayerState.ParryRecovery:
                    if (_stateFrameCounter >= Character.ParryWhiffRecovery)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Parry:
                    // Parry window — if nothing hits us within the window, go to recovery
                    if (_stateFrameCounter >= Character.ParryWindowFrames)
                        SetState(PlayerState.ParryRecovery);
                    break;
            }
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
        //  THROW (3S: P+K simultaneously)
        // ──────────────────────────────────────

        private bool TryThrow(InputFrame input) {
            // 3S throw: Punch + Kick pressed within a few frames of each other
            bool pPressed = _buffer.ButtonPressedInWindow(ButtonInput.Punch, 3);
            bool kPressed = _buffer.ButtonPressedInWindow(ButtonInput.Kick, 3);

            if (!pPressed || !kPressed) return false;

            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            MoveData throwMove = holdForward ? _moveset.ForwardThrow : _moveset.BackThrow;

            if (throwMove != null) {
                ExecuteMove(throwMove);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  TAUNT (HS+D simultaneously)
        // ──────────────────────────────────────

        private bool TryTaunt() {
            bool hsPressed = _buffer.ButtonPressedInWindow(ButtonInput.HeavySlash, 3);
            bool dPressed = _buffer.ButtonPressedInWindow(ButtonInput.Dust, 3);

            if (!hsPressed || !dPressed) return false;

            if (_moveset.Taunt != null) {
                ExecuteMove(_moveset.Taunt);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  GATLING / TARGET COMBOS
        // ──────────────────────────────────────

        private bool TryTargetCombo() {
            if (_moveset.TargetCombos == null) return false;
            if (_currentMove == null) return false;

            foreach (var tc in _moveset.TargetCombos) {
                if (tc.Sequence == null || tc.Sequence.Length < 2) continue;

                for (int i = 0; i < tc.Sequence.Length - 1; i++) {
                    if (tc.Sequence[i] == _currentMove) {
                        MoveData next = tc.Sequence[i + 1];
                        if (_parser.TryMatchMove(next)
                            && _currentMove.Cancel.IsInCancelWindow(_moveFrame)) {
                            ExecuteMove(next);
                            return true;
                        }
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
        ///
        /// GGXX flow:
        ///   1. MatchManager detects hit, calls TakeHit on defender.
        ///   2. TakeHit sets the state to Hitstun/Blockstun and records
        ///      how many stun frames to count down AFTER hitstop ends.
        ///   3. MatchManager calls ApplyHitstop on BOTH players with their
        ///      respective hitstop durations (attacker and defender differ).
        ///   4. During hitstop, both players freeze (GameTick early-returns).
        ///   5. When hitstop ends, attacker resumes active+recovery, defender
        ///      begins counting down hitstun/blockstun in TickState.
        /// </summary>
        public void TakeHit(MoveData move, bool blocked) {
            // Cancel any current move — we're being interrupted
            _currentMove = null;

            if (blocked) {
                _health -= Mathf.RoundToInt(move.Damage.ChipDamage * Character.DefenseModifier);
                SetState(PlayerState.Blockstun);

                // Blockstun begins counting AFTER blockstop ends
                _stunFramesRemaining = move.Frames.GetBlockstun();

                if (_audioSource != null && move.BlockSound != null)
                    _audioSource.PlayOneShot(move.BlockSound);

                // Block pushback
                transform.position += new Vector3(
                    -move.BlockKnockback.x * _detector.FacingSign,
                    move.BlockKnockback.y, 0);
            }
            else {
                int damage = Mathf.RoundToInt(move.Damage.BaseDamage * Character.DefenseModifier);
                _health -= damage;
                _stunMeter -= move.Damage.StunDamage;

                if (_audioSource != null && move.HitSound != null)
                    _audioSource.PlayOneShot(move.HitSound);

                if (move.HitEffectPrefab != null)
                    Instantiate(move.HitEffectPrefab, transform.position, Quaternion.identity);

                if (_health <= 0) {
                    SetState(PlayerState.KO);
                    return;
                }

                if (_stunMeter <= 0) {
                    _stunMeter = Character.MaxStun;
                    SetState(PlayerState.Stunned);
                    return;
                }

                SetState(PlayerState.Hitstun);

                // Hitstun begins counting AFTER hitstop ends
                _stunFramesRemaining = move.Frames.GetHitstun();

                // Apply knockback
                transform.position += new Vector3(
                    -move.HitKnockback.x * _detector.FacingSign,
                    move.HitKnockback.y, 0);
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
                case PlayerState.DashForward: return "DashForward";
                case PlayerState.DashBack: return "DashBack";
                case PlayerState.Hitstun: return "Hitstun";
                case PlayerState.Blockstun: return "Blockstun";
                case PlayerState.Knockdown: return "Knockdown";
                case PlayerState.Wakeup: return "Wakeup";
                case PlayerState.Stunned: return "Stunned";
                case PlayerState.KO: return "KO";
                default: return null;
            }
        }

        /// <summary>
        /// Checks if the Animator has a state with the given name on layer 0.
        /// Prevents crashes when animation states haven't been created yet.
        /// </summary>
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
                case PlayerState.Airborne: return MoveUsableState.Airborne;
                default: return MoveUsableState.Standing;
            }
        }

        /// <summary>
        /// Adds meter (called by MatchManager when this player's moves
        /// hit or are blocked).
        /// </summary>
        public void AddMeter(int amount) {
            int max = Character.GetTotalMeterCapacity();
            _meter = Mathf.Min(_meter + amount, max);
        }
    }
}