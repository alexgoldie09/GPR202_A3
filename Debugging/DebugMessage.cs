using OpenTK.Mathematics;

namespace GPR202_A3.Debugging
{
    /// <summary>
    /// A single on-screen debug line: what it says, what colour it is, and how long it has left.
    /// </summary>
    internal class DebugMessage
    {
        #region Public Fields
        /// <summary>
        /// Key used to replace this message instead of adding a new line. -1 = no key.
        /// </summary>
        public int Key { get; set; } = -1;

        /// <summary>
        /// The text to display.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The colour of the text.
        /// </summary>
        public Color4 Color { get; set; } = Color4.White;

        /// <summary>
        /// Seconds left before the message disappears.
        /// </summary>
        public float TimeRemaining { get; set; }
        #endregion
    }
}