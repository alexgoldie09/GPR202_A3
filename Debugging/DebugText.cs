using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using GPR202_A3.Graphics;

namespace GPR202_A3.Debugging
{
    /// <summary>
    /// On-screen debug messages drawn with a bitmap font, similar to Unreal's AddOnScreenDebugMessage.
    /// Call Log() from anywhere; messages stack top-down, expire after their duration,
    /// and the oldest lines are dropped when the screen is full.
    /// </summary>
    internal static class DebugText
    {
        #region Font Atlas Layout
        // These must match BitPapAtlas.png.
        private static readonly int _cellSize = 16; // each glyph cell is 16x16 pixels
        private static readonly int _columns = 16; // glyphs per row in the atlas
        private static readonly int _rows = 6; // rows in the atlas
        private static readonly int _firstChar = 32; // ' '
        private static readonly int _lastChar = 126; // '~'
        private static readonly int _glyphPadding = 1; // glyphs are drawn 1px in from the cell's left edge

        // How far to move right after each character (pixels at scale 1), for characters 32..126.
        private static readonly int[] _advances =
        [
            5, 4, 6, 8, 8, 14, 11, 3, 5, 5, 7, 10, 4, 6, 4, 6,   //  !"#$%&'()*+,-./
            7, 6, 8, 7, 8, 8, 8, 8, 8, 8, 4, 4, 10, 9, 10, 8,    // 0123456789:;<=>?
            14, 6, 6, 6, 6, 6, 6, 7, 6, 3, 6, 6, 6, 8, 8, 7,     // @ABCDEFGHIJKLMNO
            6, 8, 6, 6, 7, 6, 7, 8, 6, 7, 7, 4, 6, 4, 8, 10,     // PQRSTUVWXYZ[\]^_
            5, 6, 6, 6, 6, 6, 6, 7, 6, 3, 3, 6, 3, 9, 6, 7,      // `abcdefghijklmno
            6, 6, 6, 7, 5, 6, 8, 8, 8, 7, 7, 8, 4, 8, 9          // pqrstuvwxyz{|}~
        ];
        #endregion

        #region Settings
        /// <summary>
        /// Text size multiplier. 1 = the font's native 16px. Whole numbers keep pixels crisp.
        /// </summary>
        public static float TextScale { get; set; } = 1f;

        private static readonly float _margin = 8f; // gap from the screen edges, in pixels
        private static readonly float _lineSpacing = 2f; // extra gap between lines, in pixels

        private static float LineHeight => (_cellSize + _lineSpacing) * TextScale;
        private static int MaxLines => Math.Max(1, (int)((_screenHeight - 2 * _margin) / LineHeight));
        #endregion

        #region Private Fields
        private static readonly List<DebugMessage> _messages = new();

        // GPU resources
        private static ShaderProgram? _shader;
        private static Texture? _font;
        private static int _vao;
        private static int _vbo;

        // Vertex layout: position (x, y), uv (u, v), colour (r, g, b, a)
        private static int _floatsPerVertex = 8;
        private static float[] _vertexData = new float[1024]; // reused every frame, grows if needed
        private static int _floatCount;

        // Window size in screen coordinates (not framebuffer pixels)
        private static float _screenWidth = 1280f;
        private static float _screenHeight = 720f;
        #endregion

        #region Public API
        /// <summary>
        /// Creates the GPU resources. Call once in OnLoad, after the OpenGL context exists.
        /// </summary>
        public static void Initialize(float screenWidth, float screenHeight)
        {
            _shader = new ShaderProgram("Text.vert", "Text.frag");
            _font = new Texture("UI/BitPapAtlas.png", pixelPerfect: true); // nearest filtering keeps the pixel font crisp

            // One interleaved, dynamic buffer: the text changes every frame, so it's rebuilt every frame.
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            int stride = _floatsPerVertex * sizeof(float);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);                 // position
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float)); // uv
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float)); // colour
            GL.EnableVertexAttribArray(2);

            GL.BindVertexArray(0);

            SetScreenSize(screenWidth, screenHeight);
        }

        /// <summary>
        /// Shows a debug message on screen.
        /// </summary>
        /// <param name="message">The text to show. Characters outside printable ASCII are drawn as '?'.</param>
        /// <param name="color">The text colour.</param>
        /// <param name="duration">Seconds to show it for. 0 = a single frame.</param>
        /// <param name="key">-1 adds a new line. Any other value replaces the existing line with that key.</param>
        public static void Log(string message, Color4 color, float duration = 2f, int key = -1)
        {
            // Keyed: update the existing line in place, if there is one
            if (key != -1)
            {
                foreach (var existing in _messages)
                {
                    if (existing.Key != key) continue;

                    existing.Text = message;
                    existing.Color = color;
                    existing.TimeRemaining = duration;
                    return;
                }
            }

            _messages.Add(new DebugMessage { Key = key, Text = message, Color = color, TimeRemaining = duration });
            TrimToScreen();
        }

        /// <summary>
        /// Counts down every message and removes expired ones. Call at the start of OnUpdateFrame.
        /// </summary>
        public static void Update(float deltaTime)
        {
            // Backwards so removing doesn't skip entries
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                _messages[i].TimeRemaining -= deltaTime;
                if (_messages[i].TimeRemaining <= 0f)
                    _messages.RemoveAt(i);
            }
        }

        /// <summary>
        /// Updates the screen size used for layout. Call from OnResize with the window (not framebuffer) size.
        /// </summary>
        public static void SetScreenSize(float width, float height)
        {
            if (width <= 0 || height <= 0) return; // minimised

            _screenWidth = width;
            _screenHeight = height;
            TrimToScreen();
        }

        /// <summary>
        /// Draws all messages on top of the scene. Call at the end of OnRenderFrame, before SwapBuffers.
        /// </summary>
        public static void Render()
        {
            if (_shader == null || _font == null || _messages.Count == 0) return;

            BuildVertices();
            if (_floatCount == 0) return;

            // Remember the 3D render state so it can be restored afterwards
            bool depthWasOn = GL.IsEnabled(EnableCap.DepthTest);
            bool cullWasOn = GL.IsEnabled(EnableCap.CullFace);
            bool blendWasOn = GL.IsEnabled(EnableCap.Blend);

            // Text is flat and always on top: no depth test, no culling, alpha blending on
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Orthographic projection in screen pixels, with (0, 0) at the top-left corner
            _shader.Bind();
            _shader.SetMatrix4("projection",
                Matrix4.CreateOrthographicOffCenter(0f, _screenWidth, _screenHeight, 0f, -1f, 1f));
            // Font atlas on texture unit 0, and point the sampler at that unit
            _font.Bind();
            _shader.SetInt("FontAtlas", 0);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _floatCount * sizeof(float), _vertexData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _floatCount / _floatsPerVertex);
            GL.BindVertexArray(0);

            // Restore the 3D render state
            if (depthWasOn) GL.Enable(EnableCap.DepthTest);
            if (cullWasOn) GL.Enable(EnableCap.CullFace);
            if (!blendWasOn) GL.Disable(EnableCap.Blend);
        }
        
        /// <summary>
        /// Logs any pending OpenGL errors. Call after setup steps, not every frame (it forces the CPU to wait for the GPU).
        /// </summary>
        public static void CheckGLError(string context)
        {
            ErrorCode error;
            while ((error = GL.GetError()) != ErrorCode.NoError)
                Log($"GL error after {context}: {error}", Color4.Red, 10f);
        }

        /// <summary>
        /// Frees the GPU resources. Call in OnUnload.
        /// </summary>
        public static void Shutdown()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _shader?.Delete();
            _font?.Delete();
            _messages.Clear();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Removes the oldest lines until everything fits on screen.
        /// </summary>
        private static void TrimToScreen()
        {
            int excess = _messages.Count - MaxLines;
            if (excess > 0)
                _messages.RemoveRange(0, excess);
        }

        /// <summary>
        /// Turns every message into quads (two triangles per character), ready to upload to the GPU.
        /// Each vertex has position, uv and colour.
        /// </summary>
        private static void BuildVertices()
        {
            _floatCount = 0;

            float cellSize = _cellSize * TextScale;
            float maxX = _screenWidth - _margin;

            for (int line = 0; line < _messages.Count; line++)
            {
                var message = _messages[line];
                float penX = _margin;
                float y = _margin + line * LineHeight;

                foreach (char raw in message.Text)
                {
                    char c = (raw < _firstChar || raw > _lastChar) ? '?' : raw;
                    int index = c - _firstChar;
                    float advance = _advances[index] * TextScale;

                    // Cut the line off at the right edge instead of wrapping
                    if (penX + advance > maxX) break;

                    if (c != ' ')
                        AddGlyph(index, penX - _glyphPadding * TextScale, y, cellSize, message.Color);

                    penX += advance;
                }
            }
        }

        /// <summary>
        /// Adds one character's quad: its screen rectangle plus the matching cell of the atlas.
        /// </summary>
        private static void AddGlyph(int index, float x, float y, float size, Color4 color)
        {
            EnsureCapacity(_floatCount + 6 * _floatsPerVertex);

            int column = index % _columns;
            int row = index / _columns;

            float u0 = (float)column / _columns;
            float u1 = (float)(column + 1) / _columns;

            // Texture.cs flips images on load, so the atlas's top row sits at v = 1
            float vTop = 1f - (float)row / _rows;
            float vBottom = 1f - (float)(row + 1) / _rows;

            float x0 = x, x1 = x + size;
            float y0 = y, y1 = y + size;

            // Two triangles: top-left, top-right, bottom-right / bottom-right, bottom-left, top-left
            AddVertex(x0, y0, u0, vTop, color);
            AddVertex(x1, y0, u1, vTop, color);
            AddVertex(x1, y1, u1, vBottom, color);
            AddVertex(x1, y1, u1, vBottom, color);
            AddVertex(x0, y1, u0, vBottom, color);
            AddVertex(x0, y0, u0, vTop, color);
        }

        /// <summary>
        /// Adds one vertex to the vertex array, growing it if needed.
        /// </summary>
        /// <param name="x">The x-coordinate of the vertex in screen space.</param>
        /// <param name="y">The y-coordinate of the vertex in screen space.</param>
        /// <param name="u">The u-coordinate of the vertex in texture space.</param>
        /// <param name="v">The v-coordinate of the vertex in texture space.</param>
        /// <param name="color">The color of the vertex.</param>
        private static void AddVertex(float x, float y, float u, float v, Color4 color)
        {
            _vertexData[_floatCount++] = x;
            _vertexData[_floatCount++] = y;
            _vertexData[_floatCount++] = u;
            _vertexData[_floatCount++] = v;
            _vertexData[_floatCount++] = color.R;
            _vertexData[_floatCount++] = color.G;
            _vertexData[_floatCount++] = color.B;
            _vertexData[_floatCount++] = color.A;
        }

        /// <summary>
        /// Grows the vertex array (doubling) when more space is needed. Never shrinks, so no per-frame allocations.
        /// </summary>
        private static void EnsureCapacity(int needed)
        {
            if (_vertexData.Length < needed)
                Array.Resize(ref _vertexData, Math.Max(needed, _vertexData.Length * 2));
        }
        #endregion
    }
}