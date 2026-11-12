namespace GPR202_A3.Generation
{
    /// <summary>
    /// The rules for where one kind of prop can go, and how big and rotated it can be (used by PropScatter).
    /// </summary>
    internal class ScatterRules
    {
        #region Public Fields
        /// <summary>
        /// How many to place (fewer if the rules can't fit them all).
        /// </summary>
        public int Count { get; init; }

        /// <summary>
        /// Closest a prop can be to the centre of the map (the oasis).
        /// </summary>
        public float MinDistance { get; init; }

        /// <summary>
        /// Furthest a prop can be from the centre of the map.
        /// </summary>
        public float MaxDistance { get; init; }

        /// <summary>
        /// Space to keep around the prop (it and each neighbour keep half the gap between them).
        /// </summary>
        public float Spacing { get; init; }

        /// <summary>
        /// Steepest ground it can stand on, in degrees.
        /// </summary>
        public float MaxSlope { get; init; }

        /// <summary>
        /// How far above the water the ground must be.
        /// </summary>
        public float MinHeightAboveWater { get; init; }

        /// <summary>
        /// Smallest size (an even scale on all axes).
        /// </summary>
        public float MinScale { get; init; } = 1f;

        /// <summary>
        /// Largest size.
        /// </summary>
        public float MaxScale { get; init; } = 1f;

        /// <summary>
        /// Largest lean away from upright, in degrees.
        /// </summary>
        public float MaxTilt { get; init; }

        /// <summary>
        /// How far the model's origin goes below the ground, per unit of scale (buries the base on slopes).
        /// </summary>
        public float SinkDepth { get; init; }
        #endregion
    }
}
