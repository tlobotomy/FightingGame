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
    /// The six-button layout, matching Third Strike.
    /// </summary>
    public enum ButtonInput {
        None,
        LightPunch,
        MediumPunch,
        HeavyPunch,
        LightKick,
        MediumKick,
        HeavyKick
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
        High,       // must block standing
        Mid,        // can block either way
        Low,        // must block crouching
        Overhead,   // hits crouching block
        Unblockable
    }

    /// <summary>
    /// How the opponent reacts on hit.
    /// </summary>
    public enum HitEffect {
        None,
        Stagger,
        Knockdown,
        Launch,
        WallBounce,
        GroundBounce,
        Crumple,     // slow collapse (like stun in 3S)
        SpinOut
    }

    /// <summary>
    /// The motion pattern type, used by the parser to know
    /// which matching algorithm to run.
    /// </summary>
    public enum MotionType {
        None,               // just a button press (normals)
        DirectionPlusButton,// command normals (e.g. f+HP)
        QuarterCircle,      // 236 or 214
        DragonPunch,        // 623
        HalfCircle,         // 41236 or 63214
        FullCircle,         // 360
        ChargeBack,         // [4]6 + button
        ChargeDown,         // [2]8 + button
        DoubleQuarterCircle,// 236236 (supers)
        Custom              // arbitrary sequence defined in MotionSequence
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
}