using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Debugging
{
    /// <summary>
    /// Prints messages to the console, with warnings and errors coloured so they stand out from the load messages.
    /// </summary>
    internal static class Log
    {
        #region Constants
        // Colours for each kind of message (info uses the console's normal colour)
        private const ConsoleColor WARNINGCOLOR = ConsoleColor.Yellow;
        private const ConsoleColor ERRORCOLOR = ConsoleColor.Red;
        #endregion

        #region Public API
        /// <summary>
        /// Prints general information (files loaded, mesh sizes, timings).
        /// </summary>
        public static void Info(string message) => Console.WriteLine($"[INFO] {message}");

        /// <summary>
        /// Prints something that's wrong but the game can carry on (e.g. a missing texture uses a plain colour).
        /// </summary>
        public static void Warning(string message) => Write($"[WARNING] {message}", WARNINGCOLOR);

        /// <summary>
        /// Prints something that failed (e.g. a shader that didn't compile).
        /// </summary>
        public static void Error(string message) => Write($"[ERROR] {message}", ERRORCOLOR);

        /// <summary>
        /// Prints any pending OpenGL errors. Call after setup steps, not every frame (it makes the CPU wait for the GPU).
        /// </summary>
        /// <param name="context">What just happened, to show where the error came from.</param>
        public static void CheckGLError(string context)
        {
            ErrorCode error;
            while ((error = GL.GetError()) != ErrorCode.NoError)
                Error($"GL error after {context}: {error}");
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Prints a line in a colour, then puts the console's colour back.
        /// </summary>
        private static void Write(string message, ConsoleColor color)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = previous;
        }
        #endregion
    }
}
