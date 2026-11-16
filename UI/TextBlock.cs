using OpenTK.Mathematics;

namespace GPR202_A3.UI
{
    /// <summary>
    /// A box of text on the HUD: its lines and their colours, its size, alignment and optional background,
    /// all placed by its RectTransform. Changing the text only flags the HUD to rebuild if the text is different.
    /// </summary>
    internal class TextBlock
    {
        #region Constants
        // Where each alignment puts a line in the spare width: 0 = left edge, 0.5 = middle, 1 = right edge
        private const float LEFTFACTOR = 0f;
        private const float CENTREFACTOR = 0.5f;
        private const float RIGHTFACTOR = 1f;

        // Splits SetText's text into lines
        private const char NEWLINE = '\n';
        #endregion

        #region Public Fields
        /// <summary>
        /// Where the box sits on screen, and its size.
        /// </summary>
        public RectTransform Transform { get; }

        /// <summary>
        /// Text size as a whole-number multiple of the font's 16px (whole numbers keep the pixels even).
        /// </summary>
        public int Scale { get; }

        /// <summary>
        /// Whether the box sizes itself to its text (true when it was made without a size).
        /// </summary>
        public bool FitContent { get; }

        /// <summary>
        /// How each line lines up inside the box. Starts matching the anchor (right anchors align right, and so on).
        /// </summary>
        public TextAlignment Alignment
        {
            get => _alignment;
            set
            {
                if (_alignment == value) return;
                _alignment = value;
                IsDirty = true;
            }
        }

        /// <summary>
        /// Whether the box is drawn.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                IsDirty = true;
            }
        }

        /// <summary>
        /// Whether a coloured box is drawn behind the text.
        /// </summary>
        public bool HasBackground { get; private set; }

        /// <summary>
        /// The background's colour (use alpha below 1 to see the game through it).
        /// </summary>
        public Color4 BackgroundColor { get; private set; }

        /// <summary>
        /// Space between the background's edge and the text, in pixels. 0 without a background.
        /// </summary>
        public float Padding { get; private set; }

        /// <summary>
        /// The lines of text and their colours.
        /// </summary>
        public IReadOnlyList<(string Text, Color4 Color)> Lines => _lines;

        /// <summary>
        /// Whether something changed since the HUD last built its vertices.
        /// </summary>
        public bool IsDirty { get; private set; } = true;
        #endregion

        #region Private Fields
        private readonly List<(string Text, Color4 Color)> _lines = new();
        private TextAlignment _alignment;
        private bool _visible = true;

        // Seconds left before ShowFor hides the box. 0 = no timer.
        private float _timeRemaining;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor for a box of text without a background.
        /// </summary>
        /// <param name="transform">Where the box sits. Leave its size as zero for the box to fit its text.</param>
        /// <param name="scale">Text size as a whole-number multiple of the font's 16px.</param>
        public TextBlock(RectTransform transform, int scale)
        {
            Transform = transform;
            Scale = scale;
            FitContent = transform.Size == Vector2.Zero;

            // Line the text up the same way as the box: a right-anchored box reads best right-aligned
            float pivotX = RectTransform.GetPivot(transform.Anchor).X;
            if (pivotX == CENTREFACTOR)
                _alignment = TextAlignment.Centre;
            else if (pivotX == RIGHTFACTOR)
                _alignment = TextAlignment.Right;
            else
                _alignment = TextAlignment.Left;
        }

        /// <summary>
        /// Constructor for a box of text with a background behind it.
        /// </summary>
        /// <param name="transform">Where the box sits. Leave its size as zero for the box to fit its text.</param>
        /// <param name="scale">Text size as a whole-number multiple of the font's 16px.</param>
        /// <param name="backgroundColor">The background's colour.</param>
        /// <param name="padding">Space between the background's edge and the text, in pixels.</param>
        public TextBlock(RectTransform transform, int scale, Color4 backgroundColor, float padding)
            : this(transform, scale)
        {
            SetBackground(backgroundColor, padding);
        }
        #endregion

        #region Public API
        /// <summary>
        /// Replaces all the text with one colour. '\n' starts a new line.
        /// </summary>
        public void SetText(string text, Color4 color)
        {
            string[] newLines = text.Split(NEWLINE);

            // Nothing to rebuild if it's the same text
            if (newLines.Length == _lines.Count)
            {
                bool same = true;
                for (int i = 0; i < newLines.Length; i++)
                {
                    if (_lines[i].Text != newLines[i] || _lines[i].Color != color)
                    {
                        same = false;
                        break;
                    }
                }
                if (same) return;
            }

            _lines.Clear();
            foreach (string line in newLines)
                _lines.Add((line, color));
            IsDirty = true;
        }

        /// <summary>
        /// Sets one line, so lines can have different colours. Adds empty lines if the index is past the end.
        /// </summary>
        public void SetLine(int index, string text, Color4 color)
        {
            while (_lines.Count <= index)
            {
                _lines.Add((string.Empty, color));
                IsDirty = true;
            }

            if (_lines[index].Text == text && _lines[index].Color == color) return;

            _lines[index] = (text, color);
            IsDirty = true;
        }

        /// <summary>
        /// Shows text for a number of seconds, then hides the box (e.g. "Balloon collected!").
        /// </summary>
        public void ShowFor(string text, Color4 color, float seconds)
        {
            SetText(text, color);
            Visible = true;
            _timeRemaining = seconds;
        }

        /// <summary>
        /// Counts down a ShowFor timer. Called by the HUD every update.
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_timeRemaining <= 0f) return;

            _timeRemaining -= deltaTime;
            if (_timeRemaining <= 0f)
                Visible = false;
        }

        /// <summary>
        /// Adds a coloured box behind the text.
        /// </summary>
        /// <param name="color">The background's colour (alpha below 1 lets the game show through).</param>
        /// <param name="padding">Space between the background's edge and the text, in pixels.</param>
        public void SetBackground(Color4 color, float padding)
        {
            HasBackground = true;
            BackgroundColor = color;
            Padding = padding;
            IsDirty = true;
        }

        /// <summary>
        /// Takes the background away.
        /// </summary>
        public void RemoveBackground()
        {
            HasBackground = false;
            Padding = 0f;
            IsDirty = true;
        }

        /// <summary>
        /// Sizes the box to its text (plus padding), if it fits its content.
        /// </summary>
        public void FitToText(Font font)
        {
            if (!FitContent) return;

            float width = 0f;
            foreach (var line in _lines)
                width = MathF.Max(width, font.MeasureWidth(line.Text, Scale));

            // Every line is a line-height apart, and the last one is a cell tall
            float height = _lines.Count == 0 ? 0f : (_lines.Count - 1) * font.GetLineHeight(Scale) + font.GetCellSize(Scale);

            Transform.Size = new Vector2(width, height) + new Vector2(Padding * 2f);
        }

        /// <summary>
        /// Where a line goes in the spare width: 0 = left edge, 0.5 = middle, 1 = right edge.
        /// </summary>
        public float GetAlignmentFactor()
        {
            return _alignment switch
            {
                TextAlignment.Centre => CENTREFACTOR,
                TextAlignment.Right => RIGHTFACTOR,
                _ => LEFTFACTOR
            };
        }

        /// <summary>
        /// Called by the HUD once it has rebuilt, so unchanged text isn't rebuilt again.
        /// </summary>
        public void ClearDirty() => IsDirty = false;
        #endregion
    }
}
