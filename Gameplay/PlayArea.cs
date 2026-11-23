using OpenTK.Mathematics;

namespace GPR202_A3.Gameplay
{
    /// <summary>
    /// The part of the world the game happens in: a square centred on the oasis.
    /// The terrain is bigger than this, so the desert carries on past it (and fog hides its far edge).
    /// Balloons spawn inside it. Flying out starts a countdown, and the plane can't climb above the ceiling.
    /// </summary>
    internal static class PlayArea
    {
        #region Constants
        // The play area runs from -HALFSIZE to +HALFSIZE on x and z (the terrain is twice as wide)
        private const float HALFSIZE = 1024f;

        // The highest the plane can fly (world y). Above this, most of the view is fog.
        private const float CEILING = 160f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Half the play area's width: it runs from -HalfSize to +HalfSize on x and z.
        /// </summary>
        public static float HalfSize => HALFSIZE;

        /// <summary>
        /// The highest the plane can fly (world y).
        /// </summary>
        public static float Ceiling => CEILING;
        #endregion

        #region Public API
        /// <summary>
        /// Whether a position is inside the play area (only x and z are checked).
        /// </summary>
        public static bool Contains(Vector3 position) =>
            MathF.Abs(position.X) <= HALFSIZE && MathF.Abs(position.Z) <= HALFSIZE;
        #endregion
    }
}
