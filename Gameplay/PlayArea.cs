using OpenTK.Mathematics;

namespace GPR202_A3.Gameplay
{
    /// <summary>
    /// The part of the world the game happens in: a square centred on the oasis.
    /// The terrain is bigger than this, so the desert carries on past it (and fog hides its far edge).
    /// Balloons spawn inside it, and the collision step will keep the plane inside it.
    /// </summary>
    internal static class PlayArea
    {
        #region Constants
        // The play area runs from -HALFSIZE to +HALFSIZE on x and z (the terrain is twice as wide)
        private const float HALFSIZE = 1024f;
        #endregion

        #region Public Fields
        /// <summary>
        /// Half the play area's width: it runs from -HalfSize to +HalfSize on x and z.
        /// </summary>
        public static float HalfSize => HALFSIZE;
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
