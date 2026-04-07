using UnityEngine;
using UnityEngine.InputSystem;

namespace FightingGame.Runtime {
    /// <summary>
    /// Lightweight input forwarder for the stage select screen.
    /// Lives on the prefab spawned by PlayerInputManager.
    /// Receives input via Send Messages and passes it to StageSelectManager.
    ///
    /// Same pattern as CharacterSelectPlayer.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class StageSelectPlayer : MonoBehaviour {
        private StageSelectManager _manager;
        private int _playerIndex;

        /// <summary>
        /// Called by StageSelectManager.OnPlayerJoined to link this
        /// handler back to the manager.
        /// </summary>
        public void Initialize(StageSelectManager manager, int playerIndex) {
            _manager = manager;
            _playerIndex = playerIndex;
        }

        // ──────────────────────────────────────
        //  INPUT SYSTEM CALLBACKS (Send Messages)
        // ──────────────────────────────────────

        public void OnNavigate(InputValue value) {
            if (_manager == null) return;
            _manager.OnPlayerNavigate(_playerIndex, value.Get<Vector2>());
        }

        public void OnSubmit(InputValue value) {
            if (_manager == null) return;
            if (value.isPressed)
                _manager.OnPlayerConfirm(_playerIndex);
        }

        public void OnCancel(InputValue value) {
            if (_manager == null) return;
            if (value.isPressed)
                _manager.OnPlayerCancel(_playerIndex);
        }
    }
}