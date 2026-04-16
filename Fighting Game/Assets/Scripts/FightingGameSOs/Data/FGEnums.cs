using System;

namespace FightingGame.Data {
    /// <summary>
    /// Numpad notation directions (standard FGC convention).
    /// 7=UB  8=U  9=UF
    /// 4=B   5=N  6=F
    /// 1=DB  2=D  3=DF
    /// </summary>
    public enum NumpadDirection {
        Neutral = 5,
        Forward = 6,
        Back = 4,
        Up = 8,
        Down = 2,
        DownForward = 3,
        DownBack = 1,
        UpForward = 9,
        UpBack = 7
    }

    [Flags]
    public enum DirectionInput {
        None = 0,
        Up = 1 << 0,
        Down = 1 << 1,
        Back = 1 << 2,   // "left" relative to facing
        Forward = 1 << 3  // "right" relative to facing
    }

    /// <summary>
    /// Guilty Gear XX 4+1 button layout.
    /// P  = Punch
    /// K  = Kick
    /// S  = Slash
    /// HS = Heavy Slash
    /// D  = Dust (universal overhead / launcher)
    /// </summary>
    public enum ButtonInput {
        None,
        Punch,
        Kick,
        Slash,
        HeavySlash,
        Dust
    }

    /// <summary>
    /// Flags-based button state for tracking multiple simultaneous presses.
    /// Used in InputFrame so that P+K on the same frame both register.
    /// </summary>
    [Flags]
    public enum ButtonFlags {
        None = 0,
        Punch = 1 << 0,
        Kick = 1 << 1,
        Slash = 1 << 2,
        HeavySlash = 1 << 3,
        Dust = 1 << 4
    }

    /// <summary>
    /// Utility for converting between ButtonInput (single) and ButtonFlags (multi).
    /// </summary>
    public static class ButtonFlagsUtil {
        public static ButtonFlags FromSingle(ButtonInput btn) {
            switch (btn) {
                case ButtonInput.Punch: return ButtonFlags.Punch;
                case ButtonInput.Kick: return ButtonFlags.Kick;
                case ButtonInput.Slash: return ButtonFlags.Slash;
                case ButtonInput.HeavySlash: return ButtonFlags.HeavySlash;
                case ButtonInput.Dust: return ButtonFlags.Dust;
                default: return ButtonFlags.None;
            }
        }

        public static ButtonInput ToSingle(ButtonFlags flags) {
            // Returns highest priority held button
            if (flags.HasFlag(ButtonFlags.Dust)) return ButtonInput.Dust;
            if (flags.HasFlag(ButtonFlags.HeavySlash)) return ButtonInput.HeavySlash;
            if (flags.HasFlag(ButtonFlags.Slash)) return ButtonInput.Slash;
            if (flags.HasFlag(ButtonFlags.Kick)) return ButtonInput.Kick;
            if (flags.HasFlag(ButtonFlags.Punch)) return ButtonInput.Punch;
            return ButtonInput.None;
        }
    }

    /// <summary>
    /// Broad move categories — determines priority, cancel rules,
    /// and how the move interacts with the combo system.
    /// </summary>
    public enum MoveType {
        Normal,
        CommandNormal,  // direction + button, no motion
        Special,
        Super,
        Throw,
        Universal       // dash, parry, taunt, etc.
    }

    /// <summary>
    /// What state the player is in when this move can be used.
    /// </summary>
    [Flags]
    public enum MoveUsableState {
        Standing = 1 << 0,
        Crouching = 1 << 1,
        Airborne = 1 << 2
    }

    /// <summary>
    /// What kind of attack this is — affects hitstun, blockstun,
    /// and whether the opponent must block high or low.
    /// </summary>
    public enum AttackHeight {
        Mid,        // can block either way (default — most normals are mid)
        High,       // must block standing (functionally same as Overhead)
        Low,        // must block crouching
        Overhead,   // hits crouching block (alias for High, kept for clarity)
        Unblockable
    }

    /// <summary>
    /// How the opponent reacts on hit.
    /// </summary>
    public enum HitEffect {
        None,
        Stagger,
        Knockdown,      // hard knockdown — fixed wakeup timing
        SoftKnockdown,  // soft knockdown — can quick rise
        Launch,         // pops opponent into the air for juggle
        WallBounce,
        GroundBounce,
        Crumple,        // slow collapse (like stun in 3S)
        SpinOut
    }

    /// <summary>
    /// The motion pattern type, used by the parser to know
    /// which matching algorithm to run.
    /// </summary>
    public enum MotionType {
        None,                // just a button press (normals)
        DirectionPlusButton, // command normals (e.g. f+HP)
        QuarterCircleForward,// 236
        QuarterCircleBack,   // 214
        DragonPunch,         // 623
        HalfCircleForward,   // 41236
        HalfCircleBack,      // 63214
        FullCircle,          // 360
        ChargeBack,          // [4]6 + button
        ChargeDown,          // [2]8 + button
        DoubleQuarterCircle, // 236236 (supers)
        Custom               // arbitrary sequence defined in MotionSequence
    }

    /// <summary>
    /// Cancel levels — a move can only cancel into something
    /// of equal or higher level.
    /// Normal → Command Normal → Special → Super
    /// </summary>
    public enum CancelLevel {
        None = 0,
        Normal = 1,
        Command = 2,
        Special = 3,
        Super = 4
    }

    /// <summary>
    /// Block type modifier — determines which blockstun table to use.
    /// </summary>
    public enum BlockType {
        Normal,           // standard block
        FaultlessDefense, // FD — costs meter, more pushback, uses FD blockstun
        InstantBlock      // IB — tight timing window, reduced blockstun
    }

    /// <summary>
    /// Air tech direction — chosen by the defender when untechable time expires.
    /// </summary>
    public enum AirTechDirection {
        Neutral,  // tech in place (slight upward float)
        Forward,  // tech toward opponent
        Back      // tech away from opponent
    }

    /// <summary>
    /// Ground recovery options after knockdown.
    /// </summary>
    public enum GroundTechType {
        None,       // no tech (stay down full duration)
        QuickRise,  // button press — stand up faster in place
        BackRoll    // back + button — roll backward before standing
    }
}