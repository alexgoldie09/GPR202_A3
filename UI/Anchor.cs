namespace GPR202_A3.UI
{
    /// <summary>
    /// The point of the screen a HUD box is attached to. The same point of the box sits on it,
    /// so a TopRight box hangs down and left from the top-right corner, and a Centre box is centred on screen.
    /// </summary>
    internal enum Anchor
    {
        TopLeft,
        TopCentre,
        TopRight,
        MiddleLeft,
        Centre,
        MiddleRight,
        BottomLeft,
        BottomCentre,
        BottomRight
    }
}
