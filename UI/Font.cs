using GPR202_A3.Graphics;
using OpenTK.Mathematics;

namespace GPR202_A3.UI
{
    /// <summary>
    /// A bitmap font: the atlas texture (a grid of character cells) and how wide each character is.
    /// It measures text and finds each character's cell, so the HUD can turn text into quads.
    /// </summary>
    internal class Font
    {
        #region Font Atlas Layout
        // These must match BitPapAtlas.png
        private const int CELLSIZE = 16; // each character cell is 16x16 pixels
        private const int COLUMNS = 16; // cells per row in the atlas
        private const int ROWS = 6; // rows in the atlas
        private const int FIRSTCHAR = 32; // ' '
        private const int LASTCHAR = 126; // '~'
        private const char MISSINGCHAR = '?'; // drawn instead of characters the atlas doesn't have
        private const int GLYPHPADDING = 1; // characters are drawn 1px in from the cell's left edge
        private const int LINESPACING = 2; // extra gap between lines, in pixels at scale 1

        // How far to move right after each character (pixels at scale 1), for characters 32..126
        private static readonly int[] Advances =
        [
            5, 4, 6, 8, 8, 14, 11, 3, 5, 5, 7, 10, 4, 6, 4, 6,   //  !"#$%&'()*+,-./
            7, 6, 8, 7, 8, 8, 8, 8, 8, 8, 4, 4, 10, 9, 10, 8,    // 0123456789:;<=>?
            14, 6, 6, 6, 6, 6, 6, 7, 6, 3, 6, 6, 6, 8, 8, 7,     // @ABCDEFGHIJKLMNO
            6, 8, 6, 6, 7, 6, 7, 8, 6, 7, 7, 4, 6, 4, 8, 10,     // PQRSTUVWXYZ[\]^_
            5, 6, 6, 6, 6, 6, 6, 7, 6, 3, 3, 6, 3, 9, 6, 7,      // `abcdefghijklmno
            6, 6, 6, 7, 5, 6, 8, 8, 8, 7, 7, 8, 4, 8, 9          // pqrstuvwxyz{|}~
        ];
        #endregion

        #region Public Fields
        /// <summary>
        /// The texture unit the atlas binds to.
        /// </summary>
        public int Unit => _atlas.Unit;
        #endregion

        #region Private Fields
        private readonly Texture _atlas;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor that loads the font atlas.
        /// </summary>
        /// <param name="atlasFile">The atlas image, relative to the Resource folder.</param>
        /// <param name="unit">The texture unit to bind it to.</param>
        public Font(string atlasFile, int unit)
        {
            // Nearest filtering keeps the pixel font crisp
            _atlas = new Texture(atlasFile, unit, pixelPerfect: true);
        }
        #endregion

        #region Public API
        /// <summary>
        /// The size of a character cell on screen, in pixels.
        /// </summary>
        public float GetCellSize(int scale) => CELLSIZE * scale;

        /// <summary>
        /// The distance from one line to the next, in pixels.
        /// </summary>
        public float GetLineHeight(int scale) => (CELLSIZE + LINESPACING) * scale;

        /// <summary>
        /// The gap between the last line and the bottom of a box, so text doesn't sit on the edge.
        /// </summary>
        public float GetLineSpacing(int scale) => LINESPACING * scale;

        /// <summary>
        /// How far the drawn character sits left of the pen position (the cell has a blank column on its left).
        /// </summary>
        public float GetGlyphPadding(int scale) => GLYPHPADDING * scale;

        /// <summary>
        /// The atlas cell for a character. Characters the atlas doesn't have use '?'.
        /// </summary>
        public int GetGlyphIndex(char c)
        {
            if (c < FIRSTCHAR || c > LASTCHAR)
                c = MISSINGCHAR;
            return c - FIRSTCHAR;
        }

        /// <summary>
        /// How far to move right after drawing a character, in pixels.
        /// </summary>
        public float GetAdvance(int glyphIndex, int scale) => Advances[glyphIndex] * scale;

        /// <summary>
        /// The width of a line of text, in pixels.
        /// </summary>
        public float MeasureWidth(string text, int scale)
        {
            float width = 0f;
            foreach (char c in text)
                width += GetAdvance(GetGlyphIndex(c), scale);
            return width;
        }

        /// <summary>
        /// A character's cell in the atlas, as texture coordinates of its top-left and bottom-right corners.
        /// </summary>
        public (Vector2 TopLeft, Vector2 BottomRight) GetGlyphUVs(int glyphIndex)
        {
            int column = glyphIndex % COLUMNS;
            int row = glyphIndex / COLUMNS;

            // Texture.cs flips images on load, so the atlas's top row sits at v = 1
            var topLeft = new Vector2((float)column / COLUMNS, 1f - (float)row / ROWS);
            var bottomRight = new Vector2((float)(column + 1) / COLUMNS, 1f - (float)(row + 1) / ROWS);
            return (topLeft, bottomRight);
        }

        /// <summary>
        /// Binds the atlas to its texture unit.
        /// </summary>
        public void Bind() => _atlas.Bind();

        /// <summary>
        /// Frees the atlas texture.
        /// </summary>
        public void Delete() => _atlas.Delete();
        #endregion
    }
}
