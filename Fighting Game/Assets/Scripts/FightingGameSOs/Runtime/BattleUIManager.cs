using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FightingGame.Runtime {
    /// <summary>
    /// Drives all in-match HUD elements: health bars, tension meter bars,
    /// round timer, round counters, and state banners (ROUND 1, FIGHT,
    /// KO, etc.).
    ///
    /// Reads directly from the two PlayerControllers exposed by
    /// MatchManager. Updates every frame in Update (visual-only,
    /// not gameplay-critical, so Update is fine).
    ///
    /// TENSION GAUGE DISPLAY:
    ///   The meter bar fills based on current tension / max tension.
    ///   During negative penalty, the bar color shifts to a warning color
    ///   and optionally flashes to signal the player.
    ///
    /// Setup:
    ///   - Place on a UI Canvas in the Battle scene.
    ///   - Drag the MatchManager reference into the inspector.
    ///   - Wire every UI element to its slot.
    ///   - Health bars and meter bars must have Image Type = Filled.
    /// </summary>
    public class BattleUIManager : MonoBehaviour {
        // ──────────────────────────────────────
        //  INSPECTOR
        // ──────────────────────────────────────

        [Header("Match Manager")]
        [Tooltip("Reference to the scene's MatchManager.")]
        public MatchManager Match;

        [Header("P1 HUD (Left)")]
        public Image P1HealthBar;
        public Image P1MeterBar;
        public TMP_Text P1NameLabel;
        public Image P1Portrait;
        [Tooltip("Small pips indicating round wins. Disable extras in inspector.")]
        public Image[] P1RoundWinIcons;

        [Header("P2 HUD (Right)")]
        public Image P2HealthBar;
        public Image P2MeterBar;
        public TMP_Text P2NameLabel;
        public Image P2Portrait;
        public Image[] P2RoundWinIcons;

        [Header("Timer")]
        public TMP_Text TimerLabel;

        [Header("State Banners")]
        [Tooltip("Parent object for round/fight/KO text. Activate children as needed.")]
        public GameObject BannerRoot;
        public TMP_Text BannerText;

        [Header("Colors")]
        public Color HealthFullColor = new Color(0.2f, 0.8f, 0.2f, 1f);
        public Color HealthLowColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        [Range(0f, 1f)] public float HealthLowThreshold = 0.25f;

        [Header("Tension Gauge Colors")]
        [Tooltip("Normal tension bar color.")]
        public Color MeterColor = new Color(0.3f, 0.5f, 1f, 1f);
        [Tooltip("Tension bar color during negative penalty.")]
        public Color MeterPenaltyColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        [Tooltip("Flash speed during negative penalty (cycles per second).")]
        public float PenaltyFlashSpeed = 4f;

        // ──────────────────────────────────────
        //  STATE
        // ──────────────────────────────────────

        private PlayerController _p1;
        private PlayerController _p2;
        private bool _initialized;

        // ──────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────

        private void Start() {
            if (BannerRoot != null)
                BannerRoot.SetActive(false);
        }

        private void Update() {
            // Wait for both players to be available
            if (!_initialized) {
                if (Match == null) return;
                _p1 = Match.GetPlayer(0);
                _p2 = Match.GetPlayer(1);

                if (_p1 == null || _p2 == null) return;

                InitializeHUD();
                _initialized = true;
            }

            UpdateHealthBar(P1HealthBar, _p1);
            UpdateHealthBar(P2HealthBar, _p2);
            UpdateMeterBar(P1MeterBar, _p1);
            UpdateMeterBar(P2MeterBar, _p2);
        }

        // ──────────────────────────────────────
        //  INITIALIZATION
        // ──────────────────────────────────────

        private void InitializeHUD() {
            // P1 identity
            if (P1NameLabel != null && _p1.Character != null)
                P1NameLabel.text = _p1.Character.CharacterName;
            if (P1Portrait != null && _p1.Character != null && _p1.Character.Portrait != null)
                P1Portrait.sprite = _p1.Character.Portrait;

            // P2 identity
            if (P2NameLabel != null && _p2.Character != null)
                P2NameLabel.text = _p2.Character.CharacterName;
            if (P2Portrait != null && _p2.Character != null && _p2.Character.Portrait != null)
                P2Portrait.sprite = _p2.Character.Portrait;

            // Meter bar initial color
            if (P1MeterBar != null) P1MeterBar.color = MeterColor;
            if (P2MeterBar != null) P2MeterBar.color = MeterColor;

            // Reset round win icons
            SetRoundWins(P1RoundWinIcons, 0);
            SetRoundWins(P2RoundWinIcons, 0);
        }

        // ──────────────────────────────────────
        //  PER-FRAME UPDATES
        // ──────────────────────────────────────

        private void UpdateHealthBar(Image bar, PlayerController player) {
            if (bar == null || player.Character == null) return;

            float ratio = (float)player.Health / player.Character.MaxHealth;
            ratio = Mathf.Clamp01(ratio);

            bar.fillAmount = ratio;
            bar.color = ratio <= HealthLowThreshold
                ? HealthLowColor
                : Color.Lerp(HealthLowColor, HealthFullColor, ratio);
        }

        private void UpdateMeterBar(Image bar, PlayerController player) {
            if (bar == null || player.Character == null) return;

            float ratio = (float)player.Meter / player.Character.GetTotalMeterCapacity();
            bar.fillAmount = Mathf.Clamp01(ratio);

            // Flash during negative penalty
            if (player.InNegativePenalty) {
                float flash = (Mathf.Sin(Time.time * PenaltyFlashSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                bar.color = Color.Lerp(MeterPenaltyColor, MeterColor, flash);
            }
            else {
                bar.color = MeterColor;
            }
        }

        // ──────────────────────────────────────
        //  TIMER
        // ──────────────────────────────────────

        /// <summary>
        /// Called from MatchManager each second (or whenever the displayed
        /// timer value changes).
        /// </summary>
        public void SetTimer(int secondsRemaining) {
            if (TimerLabel == null) return;
            TimerLabel.text = secondsRemaining.ToString();
        }

        // ──────────────────────────────────────
        //  ROUND WIN ICONS
        // ──────────────────────────────────────

        /// <summary>
        /// Lights up the first N round win icons for a player.
        /// </summary>
        public void SetRoundWins(Image[] icons, int wins) {
            if (icons == null) return;
            for (int i = 0; i < icons.Length; i++) {
                if (icons[i] != null)
                    icons[i].enabled = (i < wins);
            }
        }

        public void SetP1RoundWins(int wins) => SetRoundWins(P1RoundWinIcons, wins);
        public void SetP2RoundWins(int wins) => SetRoundWins(P2RoundWinIcons, wins);

        // ──────────────────────────────────────
        //  BANNERS (ROUND 1, FIGHT, KO, etc.)
        // ──────────────────────────────────────

        /// <summary>
        /// Shows a full-screen banner with the given text for a set duration.
        /// Called from MatchManager for round start/end sequences.
        /// </summary>
        public void ShowBanner(string text, float duration = 2f) {
            if (BannerRoot == null || BannerText == null) return;

            BannerText.text = text;
            BannerRoot.SetActive(true);

            CancelInvoke(nameof(HideBanner));
            Invoke(nameof(HideBanner), duration);
        }

        public void HideBanner() {
            if (BannerRoot != null)
                BannerRoot.SetActive(false);
        }
    }
}