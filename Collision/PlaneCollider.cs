using GPR202_A3.Scene;
using OpenTK.Mathematics;

namespace GPR202_A3.Collision
{
    /// <summary>
    /// Checks whether the plane has hit the ground, the water or a prop.
    /// The plane is a simple wire frame on its model: points at its extremes, joined by lines along the wings,
    /// body and fin. Points check the ground; the lines check the props' real triangles.
    /// </summary>
    internal class PlaneCollider
    {
        #region Constants
        // Which point is which in ProbePoints (so the lines below can join them by name)
        private const int NOSE = 0;
        private const int LEFTWINGTIP = 1;
        private const int RIGHTWINGTIP = 2;
        private const int TAIL = 3;
        private const int FINTOP = 4;
        private const int LEFTWHEEL = 5;
        private const int RIGHTWHEEL = 6;
        private const int BELLY = 7;
        private const int LEFTRACK = 8;
        private const int RIGHTRACK = 9;

        // The points on the plane model (its own units, as in Blender; the nose points along +Z).
        // Picked from the model's furthest vertices in each direction, plus a few underneath.
        private static readonly Vector3[] ProbePoints =
        [
            new(0f, 1.2f, 3.9f), // nose (front of the propeller)
            new(-6.3f, 0.5f, 0.2f), // left wingtip
            new(6.3f, 0.5f, 0f), // right wingtip
            new(0f, 1.4f, -7f), // tail
            new(0f, 3f, -6.1f), // top of the fin
            new(-2.4f, -1.4f, 0.6f), // left wheel
            new(2.4f, -1.4f, 0.6f), // right wheel
            new(0f, -0.1f, -2.5f), // belly (the air scoop, the lowest part of the body)
            new(-3.2f, -0.2f, -1.2f), // under the left wing (the rocket rack)
            new(3.2f, -0.2f, -1.2f) // under the right wing
        ];

        // The wire frame: lines between points, so a thin palm trunk can't slip between two points
        private static readonly (int From, int To)[] FrameLines =
        [
            (LEFTWINGTIP, LEFTRACK), (LEFTRACK, RIGHTRACK), (RIGHTRACK, RIGHTWINGTIP), // across the wings
            (NOSE, BELLY), (BELLY, TAIL), // along the body
            (TAIL, FINTOP), // up the fin
            (LEFTWHEEL, LEFTRACK), (RIGHTWHEEL, RIGHTRACK) // down to the wheels
        ];

        // Added to the plane's reach for the quick "is this prop close?" check, so it never misses an edge
        private const float NEARBYMARGIN = 2f;

        // On a slow frame the plane can move further than a trunk is wide, so the frame is tested in steps
        // no longer than this (about half the thinnest trunk). At 60 FPS that's a single step.
        private const float MAXSTEPLENGTH = 0.5f;
        #endregion

        #region Private Fields
        // The props to check against
        private IReadOnlyList<PropCollider> _obstacles = Array.Empty<PropCollider>();

        // Where each probe point was last frame, and is this frame (world space)
        private readonly Vector3[] _previous = new Vector3[ProbePoints.Length];
        private readonly Vector3[] _current = new Vector3[ProbePoints.Length];

        // The same points moved into one prop's model units (reused for each prop, so nothing is allocated per frame)
        private readonly Vector3[] _modelPrevious = new Vector3[ProbePoints.Length];
        private readonly Vector3[] _modelCurrent = new Vector3[ProbePoints.Length];
        private readonly Vector3[] _stepStart = new Vector3[ProbePoints.Length];
        private readonly Vector3[] _stepEnd = new Vector3[ProbePoints.Length];

        // How far the furthest probe point is from the plane's origin
        private readonly float _reach;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that works out how far the probe points reach from the plane's origin.
        /// </summary>
        public PlaneCollider()
        {
            foreach (Vector3 point in ProbePoints)
                _reach = MathF.Max(_reach, point.Length);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Sets the props to check against.
        /// </summary>
        public void SetObstacles(IReadOnlyList<PropCollider> obstacles) => _obstacles = obstacles;

        /// <summary>
        /// Puts every probe point where the plane is now. Call after the plane is reset,
        /// so the first check doesn't draw a line from where it crashed to the start.
        /// </summary>
        public void Reset(Plane plane)
        {
            for (int i = 0; i < ProbePoints.Length; i++)
            {
                _current[i] = plane.GetWorldPoint(ProbePoints[i]);
                _previous[i] = _current[i];
            }
        }

        /// <summary>
        /// Moves the probe points to where the plane is now, and checks them against the world.
        /// Call once per update, after the plane has moved.
        /// </summary>
        /// <returns>Whether the plane hit something, what it hit, and where.</returns>
        public (bool Hit, HitSurface Surface, Vector3 Point) Check(Plane plane)
        {
            float moved = 0f;
            for (int i = 0; i < ProbePoints.Length; i++)
            {
                _previous[i] = _current[i];
                _current[i] = plane.GetWorldPoint(ProbePoints[i]);
                moved = MathF.Max(moved, (_current[i] - _previous[i]).Length);
            }

            // Ground and water first: one height lookup per point
            foreach (Vector3 point in _current)
            {
                float ground = Terrain.GetHeightAt(point.X, point.Z);
                if (point.Y < ground)
                    return (true, HitSurface.Ground, new Vector3(point.X, ground, point.Z));

                if (point.Y < Terrain.WaterLevel)
                    return (true, HitSurface.Water, new Vector3(point.X, Terrain.WaterLevel, point.Z));
            }

            // Props: the box skips everything far away, then the wire frame is tested against the real triangles
            float nearby = _reach + moved + NEARBYMARGIN;
            int steps = Math.Max(1, (int)MathF.Ceiling(moved / MAXSTEPLENGTH));

            foreach (PropCollider obstacle in _obstacles)
            {
                if (!obstacle.Bounds.IntersectsSphere(plane.Position, nearby))
                    continue;

                if (HitsProp(obstacle, steps, out Vector3 hitPoint))
                    return (true, HitSurface.Prop, hitPoint);
            }

            return (false, HitSurface.Ground, Vector3.Zero);
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Tests the plane against one prop's triangles, in steps from last frame's position to this frame's.
        /// Each step tests the line each point moved, and the wire frame where the step ends.
        /// </summary>
        private bool HitsProp(PropCollider obstacle, int steps, out Vector3 hitPoint)
        {
            hitPoint = Vector3.Zero;

            // Into the prop's model units once, then every line below is tested there
            for (int i = 0; i < ProbePoints.Length; i++)
            {
                _modelPrevious[i] = obstacle.ToModel(_previous[i]);
                _modelCurrent[i] = obstacle.ToModel(_current[i]);
            }

            for (int step = 1; step <= steps; step++)
            {
                float startFraction = (step - 1) / (float)steps;
                float endFraction = step / (float)steps;
                for (int i = 0; i < ProbePoints.Length; i++)
                {
                    _stepStart[i] = Vector3.Lerp(_modelPrevious[i], _modelCurrent[i], startFraction);
                    _stepEnd[i] = Vector3.Lerp(_modelPrevious[i], _modelCurrent[i], endFraction);
                }

                // The line each point moved this step
                for (int i = 0; i < ProbePoints.Length; i++)
                {
                    if (obstacle.SegmentIntersects(_stepStart[i], _stepEnd[i], out hitPoint))
                        return true;
                }

                // The wire frame where this step ends
                foreach (var (from, to) in FrameLines)
                {
                    if (obstacle.SegmentIntersects(_stepEnd[from], _stepEnd[to], out hitPoint))
                        return true;
                }
            }

            return false;
        }
        #endregion
    }
}
