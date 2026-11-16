using GPR202_A3.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_A3.UI
{
    /// <summary>
    /// Draws every TextBlock on top of the game, in one draw call.
    /// The vertices are only rebuilt and uploaded when a block's text, visibility or the window size changes,
    /// so most frames just draw what's already on the GPU.
    /// </summary>
    internal class Hud
    {
        #region Constants
        // Vertex layout: position (x, y), uv (u, v), colour (r, g, b, a)
        private const int POSITIONLOCATION = 0;
        private const int UVLOCATION = 1;
        private const int COLORLOCATION = 2;
        private const int POSITIONSIZE = 2;
        private const int UVSIZE = 2;
        private const int COLORSIZE = 4;
        private const int FLOATSPERVERTEX = POSITIONSIZE + UVSIZE + COLORSIZE;
        private const int VERTICESPERQUAD = 6; // two triangles per character or background

        // Starting size of the vertex array (it doubles when more is needed)
        private const int STARTFLOATS = 1024;

        // Background quads use this as their texture coordinate, which tells Text.frag to fill them with plain colour
        private static readonly Vector2 SolidUV = new(-1f, -1f);

        // Orthographic projection depth range (the HUD is flat, so anything around 0 works)
        private const float NEARDEPTH = -1f;
        private const float FARDEPTH = 1f;
        #endregion

        #region Public Fields
        /// <summary>
        /// How many times the vertices have been rebuilt (for measuring how often the HUD changes).
        /// </summary>
        public int RebuildCount { get; private set; }
        #endregion

        #region Private Fields
        private readonly Font _font;
        private readonly ShaderProgram _shader;
        private readonly List<TextBlock> _blocks = new();

        // GPU resources
        private readonly int _vao;
        private readonly int _vbo;
        private int _vertexCount;

        // Rebuilt into this array (reused, and only grows, so there are no per-frame allocations)
        private float[] _vertexData = new float[STARTFLOATS];
        private int _floatCount;

        // Window size in screen coordinates (not framebuffer pixels)
        private Vector2 _screenSize;

        // Set when blocks are added or removed, or the window resizes
        private bool _layoutChanged = true;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that creates the HUD's shader and vertex buffer.
        /// </summary>
        /// <param name="font">The font every block is drawn with.</param>
        /// <param name="screenWidth">The window width in screen coordinates.</param>
        /// <param name="screenHeight">The window height in screen coordinates.</param>
        public Hud(Font font, float screenWidth, float screenHeight)
        {
            _font = font;
            _shader = new ShaderProgram("Text.vert", "Text.frag");

            // One interleaved buffer for every block
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            int stride = FLOATSPERVERTEX * sizeof(float);
            // Each attribute starts where the one before it ends
            int uvOffset = POSITIONSIZE * sizeof(float);
            int colorOffset = (POSITIONSIZE + UVSIZE) * sizeof(float);
            GL.VertexAttribPointer(POSITIONLOCATION, POSITIONSIZE, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(POSITIONLOCATION);
            GL.VertexAttribPointer(UVLOCATION, UVSIZE, VertexAttribPointerType.Float, false, stride, uvOffset);
            GL.EnableVertexAttribArray(UVLOCATION);
            GL.VertexAttribPointer(COLORLOCATION, COLORSIZE, VertexAttribPointerType.Float, false, stride, colorOffset);
            GL.EnableVertexAttribArray(COLORLOCATION);

            GL.BindVertexArray(0);

            // Point the shader's sampler at the font's texture unit
            _shader.Bind();
            _shader.SetInt("FontAtlas", _font.Unit);

            SetScreenSize(screenWidth, screenHeight);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Adds a block to the HUD. Returns it, so it can be made and kept in one line.
        /// </summary>
        public TextBlock Add(TextBlock block)
        {
            _blocks.Add(block);
            _layoutChanged = true;
            return block;
        }

        /// <summary>
        /// Takes a block off the HUD.
        /// </summary>
        public void Remove(TextBlock block)
        {
            if (_blocks.Remove(block))
                _layoutChanged = true;
        }

        /// <summary>
        /// Updates the projection and layout for a new window size. Call from OnResize with the window (not framebuffer) size.
        /// </summary>
        public void SetScreenSize(float width, float height)
        {
            if (width <= 0 || height <= 0) return; // minimised

            _screenSize = new Vector2(width, height);

            // Screen pixels with (0, 0) at the top-left. Only changes on resize, so it's uploaded here.
            _shader.Bind();
            _shader.SetMatrix4("projection", Matrix4.CreateOrthographicOffCenter(0f, width, height, 0f, NEARDEPTH, FARDEPTH));
            _layoutChanged = true;
        }

        /// <summary>
        /// Counts down timed blocks (ShowFor). Call every update.
        /// </summary>
        public void Update(float deltaTime)
        {
            foreach (TextBlock block in _blocks)
                block.Update(deltaTime);
        }

        /// <summary>
        /// Draws the HUD on top of the scene. Call at the end of OnRenderFrame, before SwapBuffers.
        /// </summary>
        public void Render()
        {
            if (NeedsRebuild())
                Rebuild();

            if (_vertexCount == 0) return;

            // Remember the 3D render state so it can be restored afterwards
            bool depthWasOn = GL.IsEnabled(EnableCap.DepthTest);
            bool cullWasOn = GL.IsEnabled(EnableCap.CullFace);
            bool blendWasOn = GL.IsEnabled(EnableCap.Blend);

            // The HUD is flat and always on top: no depth test, no culling, alpha blending on
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _shader.Bind();
            _font.Bind();

            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
            GL.BindVertexArray(0);

            // Restore the 3D render state
            if (depthWasOn) GL.Enable(EnableCap.DepthTest);
            if (cullWasOn) GL.Enable(EnableCap.CullFace);
            if (!blendWasOn) GL.Disable(EnableCap.Blend);
        }

        /// <summary>
        /// Frees the shader and vertex buffer (the font belongs to whoever made it).
        /// </summary>
        public void Delete()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _shader.Delete();
            _blocks.Clear();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Whether anything changed since the last rebuild.
        /// </summary>
        private bool NeedsRebuild()
        {
            if (_layoutChanged) return true;

            foreach (TextBlock block in _blocks)
            {
                if (block.IsDirty) return true;
            }
            return false;
        }

        /// <summary>
        /// Turns every visible block into quads and uploads them.
        /// </summary>
        private void Rebuild()
        {
            _floatCount = 0;

            foreach (TextBlock block in _blocks)
            {
                if (block.Visible && block.Lines.Count > 0)
                    AddBlock(block);
                block.ClearDirty();
            }

            // Only uploaded here, not every frame
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _floatCount * sizeof(float), _vertexData, BufferUsageHint.DynamicDraw);

            _vertexCount = _floatCount / FLOATSPERVERTEX;
            _layoutChanged = false;
            RebuildCount++;
        }

        /// <summary>
        /// Adds one block's background (if it has one) and text.
        /// </summary>
        private void AddBlock(TextBlock block)
        {
            block.FitToText(_font);
            Box2 rect = block.Transform.GetScreenRect(_screenSize);

            // Background first, so the text draws over it
            if (block.HasBackground)
                AddQuad(rect.Min, rect.Max, SolidUV, SolidUV, block.BackgroundColor);

            // The text sits inside the padding
            Vector2 contentMin = rect.Min + new Vector2(block.Padding);
            Vector2 contentMax = rect.Max - new Vector2(block.Padding);
            float contentWidth = contentMax.X - contentMin.X;

            int scale = block.Scale;
            float cellSize = _font.GetCellSize(scale);
            float lineHeight = _font.GetLineHeight(scale);
            float alignment = block.GetAlignmentFactor();

            for (int line = 0; line < block.Lines.Count; line++)
            {
                var (text, color) = block.Lines[line];
                float y = contentMin.Y + line * lineHeight;

                // A fixed-size box drops lines that don't fit
                if (!block.FitContent && y + cellSize > contentMax.Y) break;

                // Spread the spare width by the alignment, on whole pixels so the font stays crisp
                float lineWidth = _font.MeasureWidth(text, scale);
                float penX = MathF.Floor(contentMin.X + (contentWidth - lineWidth) * alignment);

                foreach (char c in text)
                {
                    int glyph = _font.GetGlyphIndex(c);
                    float advance = _font.GetAdvance(glyph, scale);

                    // A fixed-size box cuts the line off at its right edge instead of wrapping
                    if (!block.FitContent && penX + advance > contentMax.X) break;

                    if (c != ' ')
                    {
                        float x = penX - _font.GetGlyphPadding(scale);
                        var (uvTopLeft, uvBottomRight) = _font.GetGlyphUVs(glyph);
                        AddQuad(new Vector2(x, y), new Vector2(x + cellSize, y + cellSize), uvTopLeft, uvBottomRight, color);
                    }

                    penX += advance;
                }
            }
        }

        /// <summary>
        /// Adds a rectangle as two triangles: top-left, top-right, bottom-right / bottom-right, bottom-left, top-left.
        /// </summary>
        private void AddQuad(Vector2 min, Vector2 max, Vector2 uvTopLeft, Vector2 uvBottomRight, Color4 color)
        {
            EnsureCapacity(_floatCount + VERTICESPERQUAD * FLOATSPERVERTEX);

            AddVertex(min.X, min.Y, uvTopLeft.X, uvTopLeft.Y, color);
            AddVertex(max.X, min.Y, uvBottomRight.X, uvTopLeft.Y, color);
            AddVertex(max.X, max.Y, uvBottomRight.X, uvBottomRight.Y, color);
            AddVertex(max.X, max.Y, uvBottomRight.X, uvBottomRight.Y, color);
            AddVertex(min.X, max.Y, uvTopLeft.X, uvBottomRight.Y, color);
            AddVertex(min.X, min.Y, uvTopLeft.X, uvTopLeft.Y, color);
        }

        /// <summary>
        /// Adds one vertex: screen position, atlas position and colour.
        /// </summary>
        private void AddVertex(float x, float y, float u, float v, Color4 color)
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
        /// Grows the vertex array (doubling) when more space is needed. Never shrinks.
        /// </summary>
        private void EnsureCapacity(int needed)
        {
            if (_vertexData.Length < needed)
                Array.Resize(ref _vertexData, Math.Max(needed, _vertexData.Length * 2));
        }
        #endregion
    }
}
