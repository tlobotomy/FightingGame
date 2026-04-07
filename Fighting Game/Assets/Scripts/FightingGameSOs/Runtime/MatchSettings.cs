using UnityEngine.InputSystem;
using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Static data carrier that persists between scenes.
    /// Character select and stage select write to this; MatchManager reads from it.
    ///
    /// Flow: CharacterSelect → StageSelect → Battle
    ///
    /// Using a static class rather than DontDestroyOnLoad because
    /// there's no MonoBehaviour lifecycle to manage — it's just data.
    ///
    /// DEVICE TRACKING:
    ///   PlayerDeviceIds stores the deviceId of the input device each player
    ///   used to join in character select. When the battle scene loads,
    ///   MatchManager uses this to ensure the same physical device maps
    ///   to the same player index — regardless of join order.
    /// </summary>
    public static class MatchSettings {
        /// <summary>
        /// The CharacterData each player selected.
        /// Index 0 = P1, Index 1 = P2.
        /// </summary>
        public static CharacterData[] SelectedCharacters = new CharacterData[2];

        /// <summary>
        /// Which color palette each player chose (index into CharacterData.ColorPalettes).
        /// Automatically offset if both players pick the same character
        /// (P2 gets palette 1 if P1 has palette 0).
        /// </summary>
        public static int[] SelectedPalettes = new int[2];

        /// <summary>
        /// The InputDevice.deviceId for each player's controller.
        /// Set during character select, read by stage select and battle
        /// to maintain consistent P1/P2 assignment across scenes.
        /// </summary>
        public static int[] PlayerDeviceIds = new int[2];

        /// <summary>
        /// The stage selected on the stage select screen.
        /// Read by MatchManager to set bounds, spawn background, and play BGM.
        /// </summary>
        public static StageData SelectedStage;

        /// <summary>
        /// Returns the player index (0 or 1) that originally used this device.
        /// Returns -1 if the device wasn't tracked.
        /// </summary>
        public static int GetPlayerIndexForDevice(InputDevice device) {
            if (device == null) return -1;
            if (device.deviceId == PlayerDeviceIds[0]) return 0;
            if (device.deviceId == PlayerDeviceIds[1]) return 1;
            return -1;
        }

        /// <summary>
        /// Reset everything (e.g. returning to main menu).
        /// </summary>
        public static void Clear() {
            SelectedCharacters[0] = null;
            SelectedCharacters[1] = null;
            SelectedPalettes[0] = 0;
            SelectedPalettes[1] = 0;
            PlayerDeviceIds[0] = 0;
            PlayerDeviceIds[1] = 0;
            SelectedStage = null;
        }
    }
}