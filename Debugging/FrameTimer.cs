using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;

namespace GPR202_A3.Debugging
{
    /// <summary>
    /// Tracks the average frame time, CPU render time and GPU render time over a short sample of frames.
    ///
    /// Frame time is the whole gap between frames, including the VSync wait, so it follows the monitor's refresh rate.
    /// CPU render time is only how long this program spends issuing a frame's work.
    /// GPU render time is how long the graphics card spends actually drawing it, measured with OpenGL timer queries.
    /// The two can be very different: issuing one huge draw call is quick on the CPU but slow on the GPU,
    /// so both are needed to see where the time goes and what an optimisation changed.
    /// </summary>
    internal class FrameTimer
    {
        #region Constants
        // How long each average covers, in seconds
        private const double SAMPLESECONDS = 0.5;

        // Timer query results arrive a few frames late (the GPU runs behind the CPU), so several queries take turns.
        // A query is only reused once its result has been read, so the CPU never waits for the GPU.
        private const int QUERYCOUNT = 4;
        #endregion

        #region Public Fields
        /// <summary>
        /// Average time between frames over the last sample, in milliseconds.
        /// </summary>
        public double AverageFrameMs { get; private set; }

        /// <summary>
        /// Average CPU time spent issuing each frame over the last sample, in milliseconds.
        /// </summary>
        public double AverageRenderMs { get; private set; }

        /// <summary>
        /// Average GPU time spent drawing each frame over the last sample, in milliseconds.
        /// </summary>
        public double AverageGpuMs { get; private set; }

        /// <summary>
        /// Average frames per second over the last sample.
        /// </summary>
        public double AverageFps => 1000.0 / Math.Max(AverageFrameMs, 0.001); // the minimum avoids dividing by zero
        #endregion

        #region Private Fields
        // Times the CPU work of one frame
        private readonly Stopwatch _renderStopwatch = new();

        // GPU timer queries, used in turn, and whether each one is waiting for a result
        private readonly int[] _queries = new int[QUERYCOUNT];
        private readonly bool[] _queryPending = new bool[QUERYCOUNT];
        private int _queryIndex;
        private bool _queryRunning;

        // Running totals for the current sample, turned into averages when it ends
        private double _sampleElapsed;
        private double _sampleFrameTime;
        private double _sampleRenderTime;
        private double _sampleGpuTime;
        private int _sampleFrames;
        private int _sampleGpuFrames;
        #endregion

        #region Public API
        /// <summary>
        /// Creates the GPU timer queries. Call once in OnLoad, after the OpenGL context exists.
        /// </summary>
        public void Initialize() => GL.GenQueries(QUERYCOUNT, _queries);

        /// <summary>
        /// Starts timing a frame's work on the CPU and GPU. Call at the start of OnRenderFrame.
        /// </summary>
        public void BeginFrame()
        {
            _renderStopwatch.Restart();

            // Only start a GPU query if this slot's previous result has been collected
            _queryRunning = !_queryPending[_queryIndex];
            if (_queryRunning)
                GL.BeginQuery(QueryTarget.TimeElapsed, _queries[_queryIndex]);
        }

        /// <summary>
        /// Stops timing the frame's work and adds this frame to the averages.
        /// Call before SwapBuffers so the wait isn't counted as render time.
        /// </summary>
        /// <param name="frameSeconds">Time since the previous frame, in seconds.</param>
        public void EndFrame(double frameSeconds)
        {
            _renderStopwatch.Stop();

            if (_queryRunning)
            {
                GL.EndQuery(QueryTarget.TimeElapsed);
                _queryPending[_queryIndex] = true;
            }
            _queryIndex = (_queryIndex + 1) % QUERYCOUNT;

            CollectGpuResults();

            _sampleElapsed += frameSeconds;
            _sampleFrameTime += frameSeconds * 1000.0;
            _sampleRenderTime += _renderStopwatch.Elapsed.TotalMilliseconds;
            _sampleFrames++;

            if (_sampleElapsed < SAMPLESECONDS)
                return;

            AverageFrameMs = _sampleFrameTime / _sampleFrames;
            AverageRenderMs = _sampleRenderTime / _sampleFrames;
            if (_sampleGpuFrames > 0)
                AverageGpuMs = _sampleGpuTime / _sampleGpuFrames;

            _sampleElapsed = 0;
            _sampleFrameTime = 0;
            _sampleRenderTime = 0;
            _sampleGpuTime = 0;
            _sampleFrames = 0;
            _sampleGpuFrames = 0;
        }

        /// <summary>
        /// Frees the GPU timer queries. Call in OnUnload.
        /// </summary>
        public void Delete() => GL.DeleteQueries(QUERYCOUNT, _queries);
        #endregion

        #region Helper Methods
        /// <summary>
        /// Reads every finished GPU query (without waiting for unfinished ones) and adds its time to the sample.
        /// </summary>
        private void CollectGpuResults()
        {
            for (int i = 0; i < QUERYCOUNT; i++)
            {
                if (!_queryPending[i])
                    continue;

                GL.GetQueryObject(_queries[i], GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available == 0)
                    continue;

                // The result is in nanoseconds
                GL.GetQueryObject(_queries[i], GetQueryObjectParam.QueryResult, out long nanoseconds);
                _sampleGpuTime += nanoseconds / 1_000_000.0;
                _sampleGpuFrames++;
                _queryPending[i] = false;
            }
        }
        #endregion
    }
}
