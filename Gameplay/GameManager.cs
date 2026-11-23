using GPR202_A3.Collision;
using GPR202_A3.Scene;
using OpenTK.Mathematics;

namespace GPR202_A3.Gameplay
{
    /// <summary>
    /// The rules of the game: the balloons, the game state (playing, crashed, won), crashing,
    /// the play area countdown, and starting a new game.
    /// Game owns the window and the drawing; this owns what's happening in the game.
    /// It tells Game when something happens (the events below) instead of reaching into the HUD or effects itself.
    /// </summary>
    internal class GameManager
    {
        #region Constants
        // How many balloons each game starts with
        private const int BALLOONSTOTAL = 8;

        // Where the plane starts: above the oasis, facing +Z
        private const float STARTHEIGHT = 60f;
        private const float STARTHEADING = 0f;

        // Seconds outside the play area before the plane blows up
        private const float OUTOFBOUNDSTIME = 5f;

        // Seconds after crashing or winning before a new game starts
        private const float RESETDELAY = 5f;
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
        /// Where the game is up to.
        /// </summary>
        public GameState State { get; private set; }

        /// <summary>
        /// Whether the plane has crashed this game (it can still crash after winning, on its victory lap).
        /// </summary>
        public bool PlaneDestroyed { get; private set; }

        /// <summary>
        /// Whether the plane is outside the play area (and the countdown is running).
        /// </summary>
        public bool OutOfBounds { get; private set; }

        /// <summary>
        /// Seconds left to get back inside the play area.
        /// </summary>
        public float OutOfBoundsTimeLeft { get; private set; }

        /// <summary>
        /// Seconds until the next game, after crashing or winning.
        /// </summary>
        public float ResetTimeLeft { get; private set; }

        /// <summary>
        /// Raised when the plane collects a balloon (Game shows the message and the confetti).
        /// </summary>
        public event Action<BalloonState>? BalloonCollected;

        /// <summary>
        /// Raised when the plane crashes: what it hit and where (Game hides the plane, and adds the fire and crater).
        /// </summary>
        public event Action<HitSurface, Vector3>? PlaneCrashed;

        /// <summary>
        /// Raised when the last balloon is collected.
        /// </summary>
        public event Action? GameWon;

        /// <summary>
        /// Raised when a new game starts, including the first (Game resets the HUD, effects and camera).
        /// </summary>
        public event Action? GameStarted;
        #endregion

        #region Private Fields
        private readonly List<BalloonState> _balloons = new();
        private readonly PlaneCollider _collider = new();
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
        /// Sets the props the plane can crash into. Call once the props are placed.
        /// </summary>
        public void SetObstacles(IReadOnlyList<PropCollider> obstacles) => _collider.SetObstacles(obstacles);

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
            _collider.Reset(plane);

            State = GameState.Playing;
            PlaneDestroyed = false;
            OutOfBounds = false;
            OutOfBoundsTimeLeft = OUTOFBOUNDSTIME;
            ResetTimeLeft = RESETDELAY;

            GameStarted?.Invoke();
        }

        /// <summary>
        /// Runs the game for one update. Call after the plane has moved.
        /// </summary>
        public void Update(Plane plane, float deltaTime)
        {
            // Still flying (after winning too, so it can't fly through the ground on its victory lap)
            if (!PlaneDestroyed)
            {
                plane.ClampHeight(PlayArea.Ceiling);

                if (State == GameState.Playing)
                    CheckPlayArea(plane, deltaTime);

                // Checked again, since leaving the play area for too long blows the plane up
                if (!PlaneDestroyed)
                    CheckCollisions(plane);
            }

            UpdateBalloons(plane, deltaTime);

            // Crashed or won: count down to the next game
            if (State != GameState.Playing)
            {
                ResetTimeLeft -= deltaTime;
                if (ResetTimeLeft <= 0f)
                    StartNewGame(plane);
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Counts down while the plane is outside the play area, and blows it up if the time runs out.
        /// </summary>
        private void CheckPlayArea(Plane plane, float deltaTime)
        {
            OutOfBounds = !PlayArea.Contains(plane.Position);
            if (!OutOfBounds)
            {
                OutOfBoundsTimeLeft = OUTOFBOUNDSTIME;
                return;
            }

            OutOfBoundsTimeLeft -= deltaTime;
            if (OutOfBoundsTimeLeft <= 0f)
                Crash(HitSurface.OutOfBounds, plane.Position);
        }

        /// <summary>
        /// Crashes the plane if it hit the ground, the water or a prop.
        /// </summary>
        private void CheckCollisions(Plane plane)
        {
            var (hit, surface, point) = _collider.Check(plane);
            if (hit)
                Crash(surface, point);
        }

        /// <summary>
        /// Destroys the plane. That ends the game, unless it was already won.
        /// </summary>
        private void Crash(HitSurface surface, Vector3 point)
        {
            PlaneDestroyed = true;
            OutOfBounds = false;
            if (State == GameState.Playing)
            {
                State = GameState.Crashed;
                ResetTimeLeft = RESETDELAY;
            }
            PlaneCrashed?.Invoke(surface, point);
        }

        /// <summary>
        /// Updates the balloons, counts new collections, and removes balloons that have finished animating.
        /// </summary>
        private void UpdateBalloons(Plane plane, float deltaTime)
        {
            // A crashed plane can't collect anything
            bool canCollect = !PlaneDestroyed;

            // Backwards so removing doesn't skip entries
            for (int i = _balloons.Count - 1; i >= 0; i--)
            {
                BalloonState balloon = _balloons[i];
                bool wasCollected = balloon.Collected;

                balloon.Update(plane.Position, canCollect, deltaTime);

                if (!wasCollected && balloon.Collected)
                {
                    BalloonsLeft--;
                    BalloonCollected?.Invoke(balloon);

                    if (BalloonsLeft == 0 && State == GameState.Playing)
                    {
                        State = GameState.Won;
                        ResetTimeLeft = RESETDELAY;
                        GameWon?.Invoke();
                    }
                }

                // The original never removed them, and kept drawing them at scale 0
                if (balloon.Finished)
                    _balloons.RemoveAt(i);
            }
        }
        #endregion
    }
}
