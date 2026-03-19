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
    /// Setup: place this on a GameObject in your match scene. Attach a
    /// PlayerInputManager component (set Join Behavior to "Join Players When
    /// Button Is Pressed" or "Join Players Manually" for testing).
    /// The Player Prefab on PlayerInputManager should have:
    ///   - PlayerInput
    ///   - InputDetector
    ///   - PlayerController
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public class MatchManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Stage")]
        [Tooltip("Left boundary of the stage (world X).")]
        public float StageLeftBound = -6f;

        [Tooltip("Right boundary of the stage (world X).")]
        public float StageRightBound = 6f;

        [Tooltip("Ground Y position.")]
        public float GroundY = 0f;

        [Header("Spawn Points")]
        [Tooltip("Where P1 and P2 appear. Element 0 = P1, Element 1 = P2.")]
        public Transform[] SpawnPoints;

        [Header("Round Settings")]
        [Tooltip("Round timer in seconds (99 in 3S).")]
        public int RoundTimeSeconds = 99;

        [Header("Hit Resolution")]
        [Tooltip("Frames of hit freeze on a normal hit (both players freeze).")]
        public int DefaultHitStop = 8;

        [Tooltip("Frames of hit freeze on a heavy/special hit.")]
        public int HeavyHitStop = 12;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private PlayerController[] _players = new PlayerController[2];
        private InputDetector[] _detectors = new InputDetector[2];
        private int _gameFrame;
        private int _hitStopFramesRemaining;

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
        }

        /// <summary>
        /// Called automatically by PlayerInputManager when a player joins.
        /// Wire this in the PlayerInputManager's "Player Joined Event" in the inspector,
        /// or use [SerializeField] and call manually.
        /// </summary>
        public void OnPlayerJoined(PlayerInput playerInput) {
            int idx = playerInput.playerIndex;
            if (idx > 1) {
                Debug.LogWarning("[MatchManager] More than 2 players attempted to join.");
                return;
            }

            var controller = playerInput.GetComponent<PlayerController>();
            var detector = playerInput.GetComponent<InputDetector>();

            if (controller == null || detector == null) {
                Debug.LogError($"[MatchManager] Player prefab missing PlayerController or InputDetector.");
                return;
            }

            _players[idx] = controller;
            _detectors[idx] = detector;

            // Apply character selection from character select screen
            if (MatchSettings.SelectedCharacters[idx] != null) {
                controller.Character = MatchSettings.SelectedCharacters[idx];
                controller.Character.SelectedSuperArt = MatchSettings.SelectedSuperArts[idx];
            }

            // P1 faces right, P2 faces left
            detector.FacingSign = (idx == 0) ? 1 : -1;

            // Position at spawn point
            if (SpawnPoints != null && idx < SpawnPoints.Length && SpawnPoints[idx] != null)
                controller.transform.position = SpawnPoints[idx].position;

            controller.Initialize();

            // Spawn character visual prefab as child
            if (controller.Character != null && controller.Character.CharacterPrefab != null) {
                Instantiate(controller.Character.CharacterPrefab, controller.transform);
            }

            Debug.Log($"[MatchManager] Player {idx + 1} joined ({controller.Character.CharacterName}).");
        }

        // ──────────────────────────────────────
        //  FIXED UPDATE — THE GAME LOOP
        // ──────────────────────────────────────

        private void FixedUpdate() {
            if (_players[0] == null || _players[1] == null) return;

            _gameFrame++;

            // --- HIT STOP ---
            // During hit stop, nobody acts — both players are frozen.
            // This is the "hit freeze" effect that gives impacts weight.
            if (_hitStopFramesRemaining > 0) {
                _hitStopFramesRemaining--;
                return;
            }

            // --- RESET PER-FRAME HIT TRACKING ---
            _hasHitThisFrame[0] = false;
            _hasHitThisFrame[1] = false;

            // --- TICK BOTH PLAYERS ---
            // Both act on the same data — no P1 advantage.
            _players[0].GameTick();
            _players[1].GameTick();

            // --- PUSHBOX RESOLUTION (before hitboxes) ---
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

        /// <summary>
        /// Prevents characters from overlapping by checking their
        /// pushbox rects and pushing them apart equally.
        /// Runs BEFORE hitbox checks so you can't clip through
        /// someone and hit from behind.
        /// </summary>
        private void ResolvePushboxes() {
            Vector2 pos0 = _players[0].transform.position;
            Vector2 pos1 = _players[1].transform.position;

            Rect box0 = _players[0].Character.Pushbox.GetWorldRect(pos0, _players[0].FacingSign);
            Rect box1 = _players[1].Character.Pushbox.GetWorldRect(pos1, _players[1].FacingSign);

            if (!box0.Overlaps(box1)) return;

            // Calculate horizontal overlap
            float overlapX;
            if (box0.center.x < box1.center.x)
                overlapX = box0.xMax - box1.xMin;
            else
                overlapX = box1.xMax - box0.xMin;

            if (overlapX <= 0f) return;

            // Push apart equally
            float halfPush = overlapX / 2f;
            float sign = pos0.x <= pos1.x ? -1f : 1f;

            _players[0].transform.position += new Vector3(sign * halfPush, 0, 0);
            _players[1].transform.position += new Vector3(-sign * halfPush, 0, 0);
        }

        // ──────────────────────────────────────
        //  HITBOX vs HURTBOX RESOLUTION
        // ──────────────────────────────────────

        /// <summary>
        /// Checks each player's active hitboxes against the opponent's
        /// hurtboxes. Both players are checked as potential attackers.
        /// </summary>
        private void ResolveHitboxes() {
            for (int attacker = 0; attacker < 2; attacker++) {
                int defender = 1 - attacker;
                ResolveAttack(attacker, defender);
            }
        }

        private void ResolveAttack(int attackerIdx, int defenderIdx) {
            var atk = _players[attackerIdx];
            var def = _players[defenderIdx];

            // Only check if the attacker is in the Active phase of a move
            if (atk.State != PlayerController.PlayerState.Active) return;
            if (atk.CurrentMove == null) return;

            // Don't hit again if this move already connected (single-hit moves)
            if (_lastMoveHit[attackerIdx] == atk.CurrentMove && atk.CurrentMove.HitCount <= 1)
                return;

            // Get attacker's hitboxes for the current frame of the move
            Rect[] hitRects = GetActiveHitboxRects(atk);
            if (hitRects == null || hitRects.Length == 0) return;

            // Get defender's hurtboxes (default stance or move override)
            Rect[] hurtRects = GetActiveHurtboxRects(def);
            if (hurtRects == null || hurtRects.Length == 0) return;

            // Check for invincibility
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

            // Check if defender is blocking
            bool blocked = IsBlocking(def, atk.CurrentMove);

            // Check if defender is parrying (3S)
            bool parried = IsParrying(def, atk.CurrentMove);

            if (parried) {
                HandleParry(attackerIdx, defenderIdx, atk.CurrentMove);
                return;
            }

            // Apply hit/block
            def.TakeHit(atk.CurrentMove, blocked);

            // Grant meter to attacker
            if (blocked)
                atk.AddMeter(atk.CurrentMove.Damage.MeterGainOnHit / 2);
            else
                atk.AddMeter(atk.CurrentMove.Damage.MeterGainOnHit);

            // Apply hit stop (both players freeze)
            _hitStopFramesRemaining = blocked ? DefaultHitStop / 2 : DefaultHitStop;

            // Track that this move has connected
            _lastMoveHit[attackerIdx] = atk.CurrentMove;
            _hasHitThisFrame[attackerIdx] = true;
        }

        // ──────────────────────────────────────
        //  HITBOX / HURTBOX RECT EXTRACTION
        // ──────────────────────────────────────

        /// <summary>
        /// Reads the attacker's current move's HitboxFrame[] array and
        /// returns the world-space rects that are active on the current
        /// frame of the move.
        /// </summary>
        private Rect[] GetActiveHitboxRects(PlayerController player) {
            MoveData move = player.CurrentMove;
            if (move.HitboxFrames == null || move.HitboxFrames.Length == 0)
                return null;

            // The move frame relative to the first active frame
            // (HitboxFrame.StartFrame is 0-indexed from first active frame)
            int moveFrame = player.GameFrame; // We need the internal move frame
            // Since PlayerController doesn't expose _moveFrame directly,
            // we derive it from the state frame counter or add an accessor.
            // For now, we calculate based on the move's startup:
            int activeFrame = GetMoveActiveFrame(player);
            if (activeFrame < 0) return null;

            Vector2 pos = player.transform.position;
            int facing = player.FacingSign;

            // Find which HitboxFrame entry covers this active frame
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

        /// <summary>
        /// Returns the defender's current hurtbox rects in world space.
        /// Uses move-specific overrides if available, otherwise defaults
        /// from CharacterData based on stance.
        /// </summary>
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

        /// <summary>
        /// Determines which HurtboxLayout is active — move override
        /// takes priority, then falls back to CharacterData defaults.
        /// </summary>
        private HurtboxLayout GetActiveHurtboxLayout(PlayerController player) {
            // Check move-specific hurtbox overrides
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

            // Fall back to stance defaults
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
        /// Returns -1 if the move is not in active phase.
        /// NOTE: This requires PlayerController to expose its internal
        /// move frame. We add a MoveFrame accessor for this.
        /// </summary>
        private int GetMoveActiveFrame(PlayerController player) {
            if (player.CurrentMove == null) return -1;

            // We need the move's internal frame counter.
            // PlayerController exposes MoveFrame for this purpose.
            int moveFrame = player.MoveFrame;
            int startup = player.CurrentMove.Frames.Startup;
            int active = player.CurrentMove.Frames.Active;

            if (moveFrame < startup) return -1;
            if (moveFrame >= startup + active) return -1;

            return moveFrame - startup; // 0-indexed active frame
        }

        // ──────────────────────────────────────
        //  BLOCKING
        // ──────────────────────────────────────

        /// <summary>
        /// Determines if the defender is blocking the incoming attack.
        /// In 3S: hold back to block standing, hold down-back to block
        /// crouching. Must match the attack's height.
        /// </summary>
        private bool IsBlocking(PlayerController defender, MoveData attack) {
            var state = defender.State;

            // Can't block during these states
            if (state == PlayerController.PlayerState.Startup
                || state == PlayerController.PlayerState.Active
                || state == PlayerController.PlayerState.Recovery
                || state == PlayerController.PlayerState.PreJump
                || state == PlayerController.PlayerState.ParryRecovery
                || state == PlayerController.PlayerState.Stunned)
                return false;

            // Read the defender's current directional input
            // We need to check if they're holding back
            var detector = _detectors[GetPlayerIndex(defender)];
            // The detector already flips based on facing, so "Back" = holding away
            // We check the buffer's most recent frame
            // For simplicity we check the defender's state:

            bool holdingBack = state == PlayerController.PlayerState.WalkBack;
            bool crouchBlocking = state == PlayerController.PlayerState.Crouching;
            // Crouch blocking requires down-back, but our state machine puts
            // the player in Crouching for any down input. A true crouch-block
            // needs back held as well. For now, we treat Crouching as crouch-block
            // if the attack is coming. This is a simplification you can refine.

            switch (attack.Height) {
                case AttackHeight.Low:
                    // Must block low (crouching)
                    return crouchBlocking;

                case AttackHeight.Overhead:
                case AttackHeight.High:
                    // Must block standing
                    return holdingBack;

                case AttackHeight.Mid:
                    // Either works
                    return holdingBack || crouchBlocking;

                case AttackHeight.Unblockable:
                    return false;

                default:
                    return holdingBack || crouchBlocking;
            }
        }

        // ──────────────────────────────────────
        //  PARRY (3S)
        // ──────────────────────────────────────

        /// <summary>
        /// Checks if the defender is in a valid parry state.
        /// The parry window is managed by PlayerController's TryParry —
        /// we just check the state here.
        /// </summary>
        private bool IsParrying(PlayerController defender, MoveData attack) {
            if (!attack.Parryable) return false;
            return defender.State == PlayerController.PlayerState.Parry;
        }

        /// <summary>
        /// Handles a successful parry: both players recover, meter is
        /// granted to the defender, and hit stop is applied.
        /// </summary>
        private void HandleParry(int attackerIdx, int defenderIdx, MoveData move) {
            var def = _players[defenderIdx];

            // Grant meter to the defender
            def.AddMeter(def.Character.ParryMeterGain);

            // Apply parry-specific hit stop
            _hitStopFramesRemaining = def.Character.ParryHitStop;

            // The defender recovers from parry (handled by their state machine)
            // The attacker continues their move normally
        }

        // ──────────────────────────────────────
        //  FACING
        // ──────────────────────────────────────

        /// <summary>
        /// Updates which direction each player faces based on
        /// relative position. Skips during certain states
        /// (mid-move, hitstun) to prevent weird side-switches.
        /// </summary>
        private void UpdateFacing() {
            float delta = _players[1].transform.position.x - _players[0].transform.position.x;

            // Only update facing when players are in neutral states
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
            }
        }

        // ──────────────────────────────────────
        //  STAGE BOUNDS
        // ──────────────────────────────────────

        /// <summary>
        /// Clamps both players within the stage boundaries.
        /// </summary>
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