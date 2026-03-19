using FightingGame.ScriptableObjects;

namespace FightingGame.Runtime {
    /// <summary>
    /// Static data carrier that persists between scenes.
    /// Character select writes to this, MatchManager reads from it.
    ///
    /// Using a static class rather than DontDestroyOnLoad because
    /// there's no MonoBehaviour lifecycle to manage — it's just data.
    /// </summary>
    public static class MatchSettings {
        /// <summary>
        /// The CharacterData each player selected.
        /// Index 0 = P1, Index 1 = P2.
        /// </summary>
        public static CharacterData[] SelectedCharacters = new CharacterData[2];

        /// <summary>
        /// Which super art each player chose (index into CharacterData.Moveset.SuperArts).
        /// </summary>
        public static int[] SelectedSuperArts = new int[2];

        /// <summary>
        /// Which color palette each player chose (index into CharacterData.ColorPalettes).
        /// Automatically offset if both players pick the same character
        /// (P2 gets palette 1 if P1 has palette 0).
        /// </summary>
        public static int[] SelectedPalettes = new int[2];

        /// <summary>
        /// Reset everything (e.g. returning to main menu).
        /// </summary>
        public static void Clear() {
            SelectedCharacters[0] = null;
            SelectedCharacters[1] = null;
            SelectedSuperArts[0] = 0;
            SelectedSuperArts[1] = 0;
            SelectedPalettes[0] = 0;
            SelectedPalettes[1] = 0;
        }
    }
}