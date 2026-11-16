using GPR202_A3.Scene;
using OpenTK.Mathematics;

namespace GPR202_A3.Gameplay
{
    /// <summary>
    /// The rules of the game: the balloons, how many are left, and starting a new game.
    /// Game owns the window and the drawing; this owns what's happening in the game.
    /// It tells Game when something happens (BalloonCollected) instead of reaching into the HUD or effects itself.
    /// Later steps add the game states here (playing, won, crashed) and the reset countdown.
    /// </summary>
    internal class GameManager
    {
        #region Constants
        // How many balloons each game starts with
        private const int BALLOONSTOTAL = 8;

        // Where the plane starts: above the oasis, facing +Z
        private const float STARTHEIGHT = 60f;
        private const float STARTHEADING = 0f;
        #endregion

        #region Public Fields
        /// <summary>
        /// The balloons still in the game (collected ones are removed once their pop animation finishes).
        /// </summary>
        public IReadOnlyList<BalloonState> Balloons => _balloons;

        /// <summary>
        /// Balloons not collected yet.
        /// </summary>
        public int BalloonsLeft { get; private set; }

        /// <summary>
        /// Balloons each game starts with.
        /// </summary>
        public int BalloonsTotal => BALLOONSTOTAL;

        /// <summary>
        /// Raised when the plane collects a balloon (Game shows the message and the confetti).
        /// </summary>
        public event Action<BalloonState>? BalloonCollected;
        #endregion

        #region Private Fields
        private readonly List<BalloonState> _balloons = new();
        #endregion

        #region Public API
        /// <summary>
        /// The plane's starting point, above the oasis (or above the water, if that's higher).
        /// </summary>
        public static Vector3 GetStartPosition()
        {
            float ground = MathF.Max(Terrain.GetHeightAt(0f, 0f), Terrain.WaterLevel);
            return new Vector3(0f, ground + STARTHEIGHT, 0f);
        }

        /// <summary>
        /// New balloons, and the plane back at the start.
        /// </summary>
        public void StartNewGame(Plane plane)
        {
            _balloons.Clear();
            for (int i = 0; i < BALLOONSTOTAL; i++)
            {
                var balloon = new BalloonState();
                balloon.Spawn();
                _balloons.Add(balloon);
            }
            BalloonsLeft = BALLOONSTOTAL;

            plane.Reset(GetStartPosition(), STARTHEADING);
        }

        /// <summary>
        /// Updates the balloons, counts new collections, and removes balloons that have finished animating.
        /// </summary>
        public void Update(Plane plane, float deltaTime)
        {
            // Backwards so removing doesn't skip entries
            for (int i = _balloons.Count - 1; i >= 0; i--)
            {
                BalloonState balloon = _balloons[i];
                bool wasCollected = balloon.Collected;

                balloon.Update(plane.Position, deltaTime);

                if (!wasCollected && balloon.Collected)
                {
                    BalloonsLeft--;
                    BalloonCollected?.Invoke(balloon);
                }

                // The original never removed them, and kept drawing them at scale 0
                if (balloon.Finished)
                    _balloons.RemoveAt(i);
            }
        }
        #endregion
    }
}
