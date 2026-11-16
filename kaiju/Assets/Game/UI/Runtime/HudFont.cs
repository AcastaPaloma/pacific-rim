using System;
using System.Collections.Generic;

namespace Game.UI
{
    /// <summary>
    /// 5x7 bitmap pixel font (R-122 4.1/4.4): upper-case letters, digits and HUD punctuation. Each glyph is 7 rows of 5 bits (bit 4 = leftmost).
    /// Lower-case text is drawn with the upper-case glyphs. The 7x10 medium font does not exist yet: <see cref="HudTextLadder"/> flags that step as placeholder.
    /// </summary>
    public static class HudFont
    {
        public const int GlyphW = 5, GlyphH = 7, Advance = 6, LineAdvance = 9; // 1 px spacing, 2 px leading

        static readonly Dictionary<char, byte[]> Glyphs = Build();

        static void Add(Dictionary<char, byte[]> d, char c, string rows)
        {
            var parts = rows.Split(' ');
            if (parts.Length != GlyphH) throw new InvalidOperationException("glyph " + c);
            var b = new byte[GlyphH];
            for (int r = 0; r < GlyphH; r++)
            {
                if (parts[r].Length != GlyphW) throw new InvalidOperationException("glyph " + c + " row " + r);
                int v = 0;
                for (int x = 0; x < GlyphW; x++) v = (v << 1) | (parts[r][x] == '#' ? 1 : 0);
                b[r] = (byte)v;
            }
            d[c] = b;
        }

        static Dictionary<char, byte[]> Build()
        {
            var d = new Dictionary<char, byte[]>();
            Add(d, ' ', "..... ..... ..... ..... ..... ..... .....");
            Add(d, '0', ".###. #...# #..## #.#.# ##..# #...# .###.");
            Add(d, '1', "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.");
            Add(d, '2', ".###. #...# ....# ...#. ..#.. .#... #####");
            Add(d, '3', ".###. #...# ....# ..##. ....# #...# .###.");
            Add(d, '4', "...#. ..##. .#.#. #..#. ##### ...#. ...#.");
            Add(d, '5', "##### #.... ####. ....# ....# #...# .###.");
            Add(d, '6', "..##. .#... #.... ####. #...# #...# .###.");
            Add(d, '7', "##### ....# ...#. ..#.. .#... .#... .#...");
            Add(d, '8', ".###. #...# #...# .###. #...# #...# .###.");
            Add(d, '9', ".###. #...# #...# .#### ....# ...#. .##..");
            Add(d, 'A', ".###. #...# #...# ##### #...# #...# #...#");
            Add(d, 'B', "####. #...# #...# ####. #...# #...# ####.");
            Add(d, 'C', ".###. #...# #.... #.... #.... #...# .###.");
            Add(d, 'D', "####. #...# #...# #...# #...# #...# ####.");
            Add(d, 'E', "##### #.... #.... ####. #.... #.... #####");
            Add(d, 'F', "##### #.... #.... ####. #.... #.... #....");
            Add(d, 'G', ".###. #...# #.... #.### #...# #...# .###.");
            Add(d, 'H', "#...# #...# #...# ##### #...# #...# #...#");
            Add(d, 'I', ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.");
            Add(d, 'J', "..### ...#. ...#. ...#. ...#. #..#. .##..");
            Add(d, 'K', "#...# #..#. #.#.. ##... #.#.. #..#. #...#");
            Add(d, 'L', "#.... #.... #.... #.... #.... #.... #####");
            Add(d, 'M', "#...# ##.## #.#.# #.#.# #...# #...# #...#");
            Add(d, 'N', "#...# ##..# #.#.# #..## #...# #...# #...#");
            Add(d, 'O', ".###. #...# #...# #...# #...# #...# .###.");
            Add(d, 'P', "####. #...# #...# ####. #.... #.... #....");
            Add(d, 'Q', ".###. #...# #...# #...# #.#.# #..#. .##.#");
            Add(d, 'R', "####. #...# #...# ####. #.#.. #..#. #...#");
            Add(d, 'S', ".#### #.... #.... .###. ....# ....# ####.");
            Add(d, 'T', "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..");
            Add(d, 'U', "#...# #...# #...# #...# #...# #...# .###.");
            Add(d, 'V', "#...# #...# #...# #...# #...# .#.#. ..#..");
            Add(d, 'W', "#...# #...# #...# #.#.# #.#.# ##.## #...#");
            Add(d, 'X', "#...# #...# .#.#. ..#.. .#.#. #...# #...#");
            Add(d, 'Y', "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..");
            Add(d, 'Z', "##### ....# ...#. ..#.. .#... #.... #####");
            Add(d, '/', "....# ....# ...#. ..#.. .#... #.... #....");
            Add(d, ':', "..... ..#.. ..#.. ..... ..#.. ..#.. .....");
            Add(d, '%', "##..# ##..# ...#. ..#.. .#... #..## #..##");
            Add(d, '.', "..... ..... ..... ..... ..... .##.. .##..");
            Add(d, '-', "..... ..... ..... ##### ..... ..... .....");
            Add(d, '!', "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..");
            Add(d, '+', "..... ..#.. ..#.. ##### ..#.. ..#.. .....");
            Add(d, '?', ".###. #...# ....# ...#. ..#.. ..... ..#..");
            Add(d, '*', "..... #.#.# .###. ##### .###. #.#.# .....");
            return d;
        }

        public static bool HasGlyph(char c) { return Glyphs.ContainsKey(char.ToUpperInvariant(c)); }

        /// <summary>Rows (7 bytes, bit 4 = leftmost). Unknown characters map to '?'.</summary>
        public static byte[] Rows(char c)
        {
            byte[] r;
            return Glyphs.TryGetValue(char.ToUpperInvariant(c), out r) ? r : Glyphs['?'];
        }

        /// <summary>True when pixel (x, y) of the glyph is set.</summary>
        public static bool Pixel(char c, int x, int y)
        {
            if (x < 0 || x >= GlyphW || y < 0 || y >= GlyphH) return false;
            return ((Rows(c)[y] >> (GlyphW - 1 - x)) & 1) != 0;
        }

        /// <summary>Width of a single-line string in font pixels (1 px between glyphs, none after the last), times <paramref name="scale"/>.</summary>
        public static int MeasureWidth(string s, int scale)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return (s.Length * Advance - 1) * scale;
        }

        public static int HeightPx(int scale) { return GlyphH * scale; }

        /// <summary>All characters that have a glyph, ordinal order (for tests and atlas baking).</summary>
        public static string Charset()
        {
            var keys = new List<char>(Glyphs.Keys);
            keys.Sort();
            return new string(keys.ToArray());
        }

        /// <summary>
        /// Greedy word wrap into lines no wider than <paramref name="maxWidthPx"/> at <paramref name="scale"/>; a word longer than a line is hard-split.
        /// Never returns lines wider than the limit (except when a single glyph is wider than the limit).
        /// </summary>
        public static List<string> Wrap(string s, int maxWidthPx, int scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(s)) return lines;
            int maxChars = Math.Max(1, (maxWidthPx / scale + 1) / Advance);
            var cur = new System.Text.StringBuilder();
            foreach (var wordRaw in s.Split(' '))
            {
                string word = wordRaw;
                while (word.Length > maxChars)
                {
                    if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                    lines.Add(word.Substring(0, maxChars)); word = word.Substring(maxChars);
                }
                if (cur.Length == 0) cur.Append(word);
                else if (cur.Length + 1 + word.Length <= maxChars) cur.Append(' ').Append(word);
                else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            return lines;
        }
    }

    /// <summary>Text-size ladder (R-122 4.4): S = 5x7 at k, M = 7x10 font at k (NOT YET DRAWN: placeholder uses the 5x7 glyphs at k), L = 5x7 at 2k. Integer scales only.</summary>
    public static class HudTextLadder
    {
        public static int PixelScale(TextScaleStep step, int k)
        {
            if (k < 1) k = 1;
            return step == TextScaleStep.Large ? 2 * k : k;
        }

        /// <summary>Body height in output pixels (smallest string): S at k=4 is 28 px, above the 18 px XAG 101 minimum.</summary>
        public static int BodyHeightPx(TextScaleStep step, int k)
        {
            int h = step == TextScaleStep.Medium ? 10 : HudFont.GlyphH; // M is specified as 7x10 (height 10)
            return h * PixelScale(step, k);
        }

        /// <summary>True while the step does not have its own font (Medium only).</summary>
        public static bool IsPlaceholder(TextScaleStep step) { return step == TextScaleStep.Medium; }
    }
}
