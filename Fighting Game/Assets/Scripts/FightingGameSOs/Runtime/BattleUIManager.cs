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

        [Header("Ghost Health Bar (trailing red)")]
        [Tooltip("Trailing health bar that shows recent damage. Sits behind the main bar.")]
        public Image P1GhostHealthBar;
        public Image P2GhostHealthBar;
        [Tooltip("Speed at which the ghost bar catches up (units per second).")]
        public float GhostDrainSpeed = 0.3f;
        public Color GhostHealthColor = new Color(0.85f, 0.15f, 0.15f, 0.8f);

        [Header("Stun Meter")]
        [Tooltip("Stun bar for each player. Depletes when hit, recovers after delay.")]
        public Image P1StunBar;
        public Image P2StunBar;
        public Color StunBarColor = new Color(1f, 0.85f, 0.1f, 1f);
        public Color StunBarDangerColor = new Color(1f, 0.3f, 0f, 1f);
        [Range(0f, 1f)] public float StunDangerThreshold = 0.3f;

        [Header("Combo Counter")]
        [Tooltip("Combo hit count text. Shown near the defender during combos.")]
        public TMP_Text P1ComboText;
        public TMP_Text P2ComboText;
        public Color ComboTextColor = Color.yellow;

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

        // Ghost health bar state (tracks the trailing fill amount)
        private float _p1GhostFill = 1f;
        private float _p2GhostFill = 1f;

        // Combo counter state (auto-hide after combo drops)
        private int _p1LastCombo;
        private int _p2LastCombo;
        private float _p1ComboHideTimer;
        private float _p2ComboHideTimer;
        private const float COMBO_DISPLAY_LINGER = 1.5f; // seconds to show after combo ends

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
            UpdateGhostHealthBar(P1GhostHealthBar, _p1, ref _p1GhostFill);
            UpdateGhostHealthBar(P2GhostHealthBar, _p2, ref _p2GhostFill);
            UpdateMeterBar(P1MeterBar, _p1);
            UpdateMeterBar(P2MeterBar, _p2);
            UpdateStunBar(P1StunBar, _p1);
            UpdateStunBar(P2StunBar, _p2);
            // Combo counter shows on the ATTACKER's side — P1's counter reads
            // P2's ComboHitCount (P2 is the one being comboed by P1), and vice versa.
            UpdateComboDisplay(P1ComboText, _p2, ref _p1LastCombo, ref _p1ComboHideTimer);
            UpdateComboDisplay(P2ComboText, _p1, ref _p2LastCombo, ref _p2ComboHideTimer);
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

            // Ghost health bars
            if (P1GhostHealthBar != null) { P1GhostHealthBar.fillAmount = 1f; P1GhostHealthBar.color = GhostHealthColor; }
            if (P2GhostHealthBar != null) { P2GhostHealthBar.fillAmount = 1f; P2GhostHealthBar.color = GhostHealthColor; }
            _p1GhostFill = 1f;
            _p2GhostFill = 1f;

            // Stun bars
            if (P1StunBar != null) { P1StunBar.fillAmount = 1f; P1StunBar.color = StunBarColor; }
            if (P2StunBar != null) { P2StunBar.fillAmount = 1f; P2StunBar.color = StunBarColor; }

            // Combo text (hidden initially)
            if (P1ComboText != null) P1ComboText.gameObject.SetActive(false);
            if (P2ComboText != null) P2ComboText.gameObject.SetActive(false);

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
        //  GHOST HEALTH BAR
        // ──────────────────────────────────────

        private void UpdateGhostHealthBar(Image ghostBar, PlayerController player, ref float ghostFill) {
            if (ghostBar == null || player.Character == null) return;

            float actualRatio = Mathf.Clamp01((float)player.Health / player.Character.MaxHealth);

            // Ghost only drains DOWN — it never goes up faster than actual health.
            // Snap up instantly if health somehow increases (healing).
            if (actualRatio > ghostFill)
                ghostFill = actualRatio;
            else
                ghostFill = Mathf.MoveTowards(ghostFill, actualRatio, GhostDrainSpeed * Time.deltaTime);

            ghostBar.fillAmount = ghostFill;
        }

        // ──────────────────────────────────────
        //  STUN METER
        // ──────────────────────────────────────

        private void UpdateStunBar(Image stunBar, PlayerController player) {
            if (stunBar == null || player.Character == null) return;

            float ratio = Mathf.Clamp01((float)player.StunMeter / player.Character.MaxStun);
            stunBar.fillAmount = ratio;

            stunBar.color = ratio <= StunDangerThreshold ? StunBarDangerColor : StunBarColor;
        }

        // ──────────────────────────────────────
        //  COMBO COUNTER
        // ──────────────────────────────────────

        /// <summary>
        /// Called by MatchManager each time a hit lands in a combo.
        /// playerIndex is the ATTACKER (the one performing the combo).
        /// The counter displays on the attacker's side of the screen.
        /// </summary>
        public void SetComboCount(int playerIndex, int hits) {
            TMP_Text label = (playerIndex == 0) ? P1ComboText : P2ComboText;
            if (label == null) return;

            if (hits >= 2) {
                label.gameObject.SetActive(true);
                label.text = $"{hits} HITS";
                label.color = ComboTextColor;
            }

            // Reset hide timer references via the Update loop
            if (playerIndex == 0) { _p1LastCombo = hits; _p1ComboHideTimer = COMBO_DISPLAY_LINGER; }
            else { _p2LastCombo = hits; _p2ComboHideTimer = COMBO_DISPLAY_LINGER; }
        }

        private void UpdateComboDisplay(TMP_Text label, PlayerController player,
            ref int lastCombo, ref float hideTimer) {
            if (label == null) return;

            int current = player.ComboHitCount;

            if (current >= 2) {
                // Active combo — keep visible, reset timer
                label.gameObject.SetActive(true);
                label.text = $"{current} HITS";
                label.color = ComboTextColor;
                hideTimer = COMBO_DISPLAY_LINGER;
                lastCombo = current;
            }
            else if (lastCombo >= 2) {
                // Combo just ended — linger with final count, then hide
                // Use unscaledDeltaTime so time-scale changes don't affect display
                hideTimer -= Time.unscaledDeltaTime;
                if (hideTimer <= 0f) {
                    label.gameObject.SetActive(false);
                    lastCombo = 0;
                }
            }
            else {
                // No active combo, no linger — make sure it's hidden
                if (label.gameObject.activeSelf)
                    label.gameObject.SetActive(false);
                lastCombo = 0;
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