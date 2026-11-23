namespace GPR202_A3.Gameplay
{
    /// <summary>
    /// Where the game is up to.
    /// </summary>
    internal enum GameState
    {
        /// <summary>Flying and collecting balloons.</summary>
        Playing,

        /// <summary>The plane crashed; a new game starts after a countdown.</summary>
        Crashed,

        /// <summary>Every balloon collected; a new game starts after a countdown.</summary>
        Won
    }
}
