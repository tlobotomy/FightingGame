using UnityEngine;
using UnityEngine.InputSystem;

namespace FightingGame.Runtime {
    /// <summary>
    /// Lightweight input receiver for the character select screen.
    /// Spawned by PlayerInputManager when a player joins.
    ///
    /// This sits on a tiny prefab that only has:
    ///   - PlayerInput (Behavior = "Send Messages")
    ///   - CharacterSelectPlayer (this script)
    ///
    /// It receives input via Send Messages (OnNavigate, OnSubmit, OnCancel)
    /// and forwards everything to CharacterSelectManager using the
    /// player index assigned at join time.
    ///
    /// Your Input Action Asset needs a "UI" action map with:
    ///   - Navigate (Value, Vector2) — stick/dpad
    ///   - Submit (Button) — confirm
    ///   - Cancel (Button) — back
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class CharacterSelectPlayer : MonoBehaviour {
        private CharacterSelectManager _manager;
        private int _playerIndex;
        private bool _initialized;

        /// <summary>
        /// Called by CharacterSelectManager.OnPlayerJoined after
        /// PlayerInputManager spawns this prefab.
        /// </summary>
        public void Initialize(CharacterSelectManager manager, int playerIndex) {
            _manager = manager;
            _playerIndex = playerIndex;
            _initialized = true;
        }

        // ──────────────────────────────────────
        //  INPUT SYSTEM CALLBACKS (Send Messages)
        //
        //  These method names must match the action names
        //  in your Input Action Asset's UI action map.
        // ──────────────────────────────────────

        /// <summary>
        /// Stick / dpad movement. Called continuously while held.
        /// </summary>
        public void OnNavigate(InputValue value) {
            if (!_initialized) return;
            _manager.OnPlayerNavigate(_playerIndex, value.Get<Vector2>());
        }

        /// <summary>
        /// Confirm button (mapped to your "Submit" action).
        /// </summary>
        public void OnSubmit(InputValue value) {
            if (!_initialized) return;
            if (value.isPressed)
                _manager.OnPlayerConfirm(_playerIndex);
        }

        /// <summary>
        /// Cancel / back button.
        /// </summary>
        public void OnCancel(InputValue value) {
            if (!_initialized) return;
            if (value.isPressed)
                _manager.OnPlayerCancel(_playerIndex);
        }
    }
}