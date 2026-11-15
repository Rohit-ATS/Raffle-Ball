using UnityEngine;

namespace RiftRally.Core
{
    /// <summary>
    /// Central tuning surface for the whole game. Values live here so the feel of the
    /// game can be adjusted in one place instead of hunting through prefabs.
    /// </summary>
    public static class GameConfig
    {
        // ---- Rink dimensions (metres). Sized for a seated player at a real table. ----
        public const float RinkLength = 1.60f;
        public const float RinkWidth = 0.70f;
        public const float RinkHeight = 0.74f;   // table-top height above the floor
        public const float WallHeight = 0.035f;

        /// <summary>Distance from the far end at which the scoring target sits.</summary>
        public const float TargetFromFarEnd = 0.34f;

        /// <summary>Radii of the scoring rings, outermost first.</summary>
        public static readonly float[] RingRadii = { 0.17f, 0.11f, 0.055f };

        /// <summary>Launch line: stones are released from behind this near-end offset.</summary>
        public const float LaunchLineFromNearEnd = 0.20f;

        // ---- Stones ----
        public const int StonesPerPlayerPerRound = 3;
        public const float StoneRadius = 0.032f;
        public const float StoneHeight = 0.026f;
        public const float StoneMass = 0.42f;

        /// <summary>Linear damping applied manually to emulate ice friction.</summary>
        public const float IceFriction = 0.62f;
        public const float StoneAngularDamping = 1.4f;

        /// <summary>Speed below which a stone counts as stopped.</summary>
        public const float SettleSpeed = 0.045f;
        public const float SettleHoldTime = 0.35f;

        // ---- Throwing ----
        public const float GrabRadius = 0.11f;
        public const float MaxPullDistance = 0.34f;
        public const float MinLaunchSpeed = 0.55f;
        public const float MaxLaunchSpeed = 3.35f;

        /// <summary>Pinch gap (m) that begins a grab, and the wider gap that releases it.</summary>
        public const float PinchEnterDistance = 0.022f;
        public const float PinchExitDistance = 0.038f;

        // ---- Match ----
        public const int RoundsToWinMatch = 2;   // best of three
        public const int MaxRounds = 3;

        // ---- Timings ----
        public const float ModifierRevealTime = 2.6f;
        public const float RoundScoreRevealTime = 3.2f;
        public const float TurnHandoffTime = 1.1f;

        // ---- Presentation ----
        public static readonly Color PlayerOneColor = new Color(0.16f, 0.78f, 1.00f);
        public static readonly Color PlayerTwoColor = new Color(1.00f, 0.42f, 0.22f);
        public static readonly Color NeutralColor = new Color(0.72f, 0.78f, 0.86f);
        public static readonly Color TargetColor = new Color(0.55f, 0.95f, 0.70f);
    }

    public enum PlayerSide
    {
        One = 0,
        Two = 1
    }

    public static class PlayerSideExtensions
    {
        public static PlayerSide Other(this PlayerSide side)
            => side == PlayerSide.One ? PlayerSide.Two : PlayerSide.One;

        public static Color ToColor(this PlayerSide side)
            => side == PlayerSide.One ? GameConfig.PlayerOneColor : GameConfig.PlayerTwoColor;

        public static string Label(this PlayerSide side)
            => side == PlayerSide.One ? "BLUE" : "ORANGE";
    }
}
