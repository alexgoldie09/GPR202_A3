namespace GPR202_A3.Collision
{
    /// <summary>
    /// What the plane hit, so the crash knows whether to leave a crater.
    /// </summary>
    internal enum HitSurface
    {
        /// <summary>The sand (leaves a crater).</summary>
        Ground,

        /// <summary>The oasis.</summary>
        Water,

        /// <summary>A palm tree or a rock.</summary>
        Prop,

        /// <summary>Stayed outside the play area too long (blows up in the air).</summary>
        OutOfBounds
    }
}
