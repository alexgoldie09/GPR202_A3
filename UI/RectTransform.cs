using OpenTK.Mathematics;

namespace GPR202_A3.UI
{
    /// <summary>
    /// Where a HUD box sits on screen: which point it's anchored to, how far in from that point, and how big it is.
    /// A cut-down version of Unity's RectTransform (anchor and pivot are one setting, and there's no stretching).
    /// Screen positions are in window pixels with (0, 0) at the top-left, matching the HUD's projection.
    /// </summary>
    internal class RectTransform
    {
        #region Constants
        // How far across the screen (and the box) an anchor sits, on each axis: 0 = left/top, 0.5 = middle, 1 = right/bottom
        private const float START = 0f;
        private const float MIDDLE = 0.5f;
        private const float END = 1f;

        // Which way the offset pushes the box: in from the left/top edges, or in from the right/bottom edges
        private const float INWARDFROMSTART = 1f;
        private const float INWARDFROMEND = -1f;
        #endregion

        #region Public Fields
        /// <summary>
        /// The point of the screen (and of the box) the box is attached to.
        /// </summary>
        public Anchor Anchor { get; set; }

        /// <summary>
        /// Pixels in from the anchored edges. On a middle axis, positive moves right or down.
        /// </summary>
        public Vector2 Offset { get; set; }

        /// <summary>
        /// The box's width and height in pixels. TextBlocks that fit their content set this themselves.
        /// </summary>
        public Vector2 Size { get; set; }
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that anchors a box to a point of the screen.
        /// </summary>
        /// <param name="anchor">The point of the screen it's attached to.</param>
        /// <param name="offset">Pixels in from the anchored edges.</param>
        /// <param name="size">Its size in pixels (leave as zero if the text sets it).</param>
        public RectTransform(Anchor anchor, Vector2 offset, Vector2 size = default)
        {
            Anchor = anchor;
            Offset = offset;
            Size = size;
        }
        #endregion

        #region Public API
        /// <summary>
        /// The box's rectangle on a screen of the given size.
        /// </summary>
        /// <param name="screenSize">The window size in pixels.</param>
        public Box2 GetScreenRect(Vector2 screenSize)
        {
            Vector2 pivot = GetPivot(Anchor);

            // The anchor point on screen, then line the same point of the box up with it
            Vector2 anchorPoint = screenSize * pivot;
            Vector2 min = anchorPoint - Size * pivot;

            // Push the box in from whichever edges it's anchored to
            var inward = new Vector2(pivot.X == END ? INWARDFROMEND : INWARDFROMSTART,
                                     pivot.Y == END ? INWARDFROMEND : INWARDFROMSTART);
            min += Offset * inward;

            // Whole pixels keep the pixel font crisp (centring can land on half a pixel)
            min = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
            return new Box2(min, min + Size);
        }

        /// <summary>
        /// How far across the screen an anchor sits on each axis (0 = left/top, 0.5 = middle, 1 = right/bottom).
        /// </summary>
        public static Vector2 GetPivot(Anchor anchor)
        {
            return anchor switch
            {
                Anchor.TopLeft => new Vector2(START, START),
                Anchor.TopCentre => new Vector2(MIDDLE, START),
                Anchor.TopRight => new Vector2(END, START),
                Anchor.MiddleLeft => new Vector2(START, MIDDLE),
                Anchor.Centre => new Vector2(MIDDLE, MIDDLE),
                Anchor.MiddleRight => new Vector2(END, MIDDLE),
                Anchor.BottomLeft => new Vector2(START, END),
                Anchor.BottomCentre => new Vector2(MIDDLE, END),
                _ => new Vector2(END, END) // BottomRight
            };
        }
        #endregion
    }
}
