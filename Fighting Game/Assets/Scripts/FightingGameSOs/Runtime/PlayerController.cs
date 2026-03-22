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

        /// <summary>
        /// Called by MatchManager after instantiating the character's
        /// visual prefab, to link the animator and audio source.
        /// </summary>
        public void SetVisualReferences(Animator animator, AudioSource audioSource) {
            _animator = animator;
            _audioSource = audioSource;
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
                Debug.LogError($"[PlayerController] No CharacterData assigned on {gameObject.name}");
                return;
            }

            _moveset = Character.Moveset;
            _specialsSorted = _moveset.GetAllSpecialsSorted();
            _health = Character.MaxHealth;
            _meter = 0;
            _stunMeter = Character.MaxStun;
            _gameFrame = 0;
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
            _stateFrameCounter++;

            // 1. Poll input from hardware → push to buffer
            InputFrame input = _detector.Poll(_gameFrame);
            _buffer.Push(input);

            // 2. Advance current move / state timer
            if (_currentMove != null) {
                _moveFrame++;
                UpdateMovePhase();
            }

            // 3. Tick state-specific logic (dash timers, jump arcs, etc.)
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
            // --- PARRY (3S) ---
            // Forward tap with no button = parry attempt
            if (TryParry(input)) return;

            // --- SUPERS (highest motion priority) ---
            if (_moveset.SuperArts != null && Character.SelectedSuperArt < _moveset.SuperArts.Length) {
                var sa = _moveset.SuperArts[Character.SelectedSuperArt];
                if (sa.Move != null && _meter >= sa.CostPerUse) {
                    if (_parser.TryMatchMove(sa.Move)) {
                        _meter -= sa.CostPerUse;
                        ExecuteMove(sa.Move);
                        return;
                    }
                }
            }

            // --- SPECIALS + EX (sorted by InputPriority desc) ---
            MoveData special = _parser.TryMatchFirst(_specialsSorted);
            if (special != null) {
                // EX moves cost meter
                if (special.IsEX) {
                    if (_meter >= special.MeterCost) {
                        _meter -= special.MeterCost;
                        ExecuteMove(special);
                        return;
                    }
                    // Not enough meter — fall through to normals
                }
                else {
                    ExecuteMove(special);
                    return;
                }
            }

            // --- TARGET COMBO continuation ---
            if (TryTargetCombo()) return;

            // --- COMMAND NORMALS ---
            if (_moveset.CommandNormals != null) {
                MoveData cmd = _parser.TryMatchFirst(_moveset.CommandNormals);
                if (cmd != null) {
                    ExecuteMove(cmd);
                    return;
                }
            }

            // --- THROW ---
            if (TryThrow(input)) return;

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
            if (_animator != null && !string.IsNullOrEmpty(move.AnimationStateName))
                _animator.Play(move.AnimationStateName, 0, 0f);

            // Play swing sound
            if (_audioSource != null && move.SwingSound != null)
                _audioSource.PlayOneShot(move.SwingSound);
        }

        /// <summary>
        /// Updates the player state based on which phase of the current
        /// move we're in (startup → active → recovery → idle).
        /// </summary>
        private void UpdateMovePhase() {
            if (_moveFrame < _currentMove.Frames.Startup) {
                SetState(PlayerState.Startup);
            }
            else if (_moveFrame < _currentMove.Frames.Startup + _currentMove.Frames.Active) {
                SetState(PlayerState.Active);
                // TODO: enable hitboxes on first active frame, disable on last
            }
            else if (_moveFrame < _currentMove.Frames.TotalFrames) {
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

        /// <summary>
        /// Returns true if the player is allowed to start a new move.
        /// Handles idle states, cancel windows, and kara cancels.
        /// </summary>
        private bool CanAct() {
            switch (_state) {
                case PlayerState.Idle:
                case PlayerState.WalkForward:
                case PlayerState.WalkBack:
                case PlayerState.Crouching:
                    return true;

                case PlayerState.Airborne:
                    // Air normals / air specials
                    return _currentMove == null;

                case PlayerState.Startup:
                    // Kara cancel: first 1-2 startup frames of a normal
                    // can cancel into a special/super
                    if (_currentMove != null && _currentMove.Cancel.IsInKaraWindow(_moveFrame))
                        return true;
                    return false;

                case PlayerState.Active:
                case PlayerState.Recovery:
                    // Standard cancel window
                    if (_currentMove != null && _currentMove.Cancel.IsInCancelWindow(_moveFrame))
                        return true;
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// When in a cancel window, the new move must be allowed by
        /// the current move's cancel rules. This wraps ResolveInput
        /// with the additional cancel-level check.
        /// </summary>
        private bool CanCancelCurrentInto(MoveData candidate) {
            if (_currentMove == null) return true;
            return _currentMove.CanCancelInto(candidate, _moveFrame);
        }

        // ──────────────────────────────────────
        //  MOVEMENT
        // ──────────────────────────────────────

        private void HandleMovement(InputFrame input) {
            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            bool holdBack = input.Direction.HasFlag(DirectionInput.Back);
            bool holdDown = input.Direction.HasFlag(DirectionInput.Down);
            bool holdUp = input.Direction.HasFlag(DirectionInput.Up);

            if (holdDown) {
                SetState(PlayerState.Crouching);
            }
            else if (holdUp && _state != PlayerState.PreJump && _state != PlayerState.Airborne) {
                // Commit to jump (pre-jump frames)
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

        /// <summary>
        /// Per-frame logic for states that have timers
        /// (jump arc, dash, hitstun, blockstun, etc.)
        /// </summary>
        private void TickState(InputFrame input) {
            switch (_state) {
                case PlayerState.PreJump:
                    if (_stateFrameCounter >= Character.PreJumpFrames) {
                        SetState(PlayerState.Airborne);
                        // TODO: apply jump velocity based on input direction
                    }
                    break;

                case PlayerState.Airborne:
                    // TODO: apply gravity, check landing
                    break;

                case PlayerState.JumpLanding:
                    if (_stateFrameCounter >= Character.JumpLandingFrames)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.DashForward:
                    if (_stateFrameCounter >= Character.DashDuration)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.DashBack:
                    if (_stateFrameCounter >= Character.BackDashDuration)
                        SetState(PlayerState.Idle);
                    break;

                case PlayerState.Hitstun:
                    // Hitstun duration is set when the hit is received
                    // (see TakeHit below)
                    break;

                case PlayerState.Blockstun:
                    break;

                case PlayerState.ParryRecovery:
                    if (_stateFrameCounter >= Character.ParryWhiffRecovery)
                        SetState(PlayerState.Idle);
                    break;
            }
        }

        // ──────────────────────────────────────
        //  PARRY (3S)
        // ──────────────────────────────────────

        /// <summary>
        /// Detects a parry attempt: a clean forward tap (within the
        /// parry window) with no button held.
        /// </summary>
        private bool TryParry(InputFrame input) {
            // Only from neutral / walkback (can't parry while attacking)
            if (_state != PlayerState.Idle && _state != PlayerState.WalkBack
                && _state != PlayerState.Crouching)
                return false;

            // Forward tap for standing parry, down tap for low parry
            bool forwardTap = input.Direction.HasFlag(DirectionInput.Forward)
                && !_buffer.DirectionInWindow(DirectionInput.Forward, 2);
            bool downTap = input.Direction.HasFlag(DirectionInput.Down)
                && !_buffer.DirectionInWindow(DirectionInput.Down, 2);

            if (forwardTap || downTap) {
                // Enter parry state — the hit resolution system checks
                // for this state to grant the parry.
                SetState(PlayerState.Parry);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  THROW
        // ──────────────────────────────────────

        /// <summary>
        /// Throw: LP+LK simultaneously (3S convention).
        /// </summary>
        private bool TryThrow(InputFrame input) {
            // Check for two buttons pressed on the same frame
            bool lpPressed = _buffer.ButtonPressedInWindow(ButtonInput.LightPunch, 3);
            bool lkPressed = _buffer.ButtonPressedInWindow(ButtonInput.LightKick, 3);

            if (!lpPressed || !lkPressed) return false;

            bool holdForward = input.Direction.HasFlag(DirectionInput.Forward);
            MoveData throwMove = holdForward ? _moveset.ForwardThrow : _moveset.BackThrow;

            if (throwMove != null) {
                ExecuteMove(throwMove);
                return true;
            }

            return false;
        }

        // ──────────────────────────────────────
        //  TARGET COMBOS
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
        /// Called by the hit resolution system when this player is hit.
        /// </summary>
        public void TakeHit(MoveData move, bool blocked) {
            // Cancel any current move — we're being interrupted
            _currentMove = null;

            if (blocked) {
                _health -= Mathf.RoundToInt(move.Damage.ChipDamage * Character.DefenseModifier);
                SetState(PlayerState.Blockstun);

                // Block sound
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

                // Hit sound
                if (_audioSource != null && move.HitSound != null)
                    _audioSource.PlayOneShot(move.HitSound);

                // Spawn hit effect
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

                // Apply knockback
                transform.position += new Vector3(
                    -move.HitKnockback.x * _detector.FacingSign,
                    move.HitKnockback.y, 0);
            }

            // Grant meter to the attacker (handled by MatchManager)
        }

        // ──────────────────────────────────────
        //  UTILITY
        // ──────────────────────────────────────

        private void SetState(PlayerState newState) {
            if (_state == newState) return;
            _state = newState;
            _stateFrameCounter = 0;

            // Drive non-move animations (idle, walk, crouch, etc.)
            // Move animations are handled in ExecuteMove instead.
            if (_animator != null && _currentMove == null) {
                string stateName = GetAnimationStateForPlayerState(newState);
                if (stateName != null)
                    _animator.Play(stateName, 0, 0f);
            }
        }

        /// <summary>
        /// Maps player states to Animator state names.
        /// These must match state names in your Animator Controller.
        /// </summary>
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