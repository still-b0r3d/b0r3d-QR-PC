using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Qr
{
    // A colour font's pictures, as OpenType's COLR table (version 0) and its CPAL palette keep them: an emoji's glyph is
    // drawn as layers, each another glyph of the font in a colour of the palette or in the text's own colour (palette
    // entry 0xFFFF). Chromium draws Segoe UI Emoji that way on the website's page; WPF on .NET Framework draws every glyph
    // in the text's one colour. So a caption is laid out and drawn by WPF as before, and then each glyph of a colour font
    // is painted again as its layers, in the same place: lines and widths stay exactly WPF's. (COLR version 1's gradients
    // are left out: Segoe UI Emoji keeps the flat version 0 layers of every emoji too.) The tables are read from the
    // font's own bytes as WPF has them, as anything from outside is read: a table that doesn't add up leaves its font in
    // one colour. Only the two tables' records and the first palette are kept (about half a megabyte for Segoe UI Emoji),
    // once for each font. An SVG picture's text, which is drawn as shapes, takes its emoji's layers from here too (Split).
    sealed class ColourGlyphs
    {
        readonly ushort[] bases, firsts, counts;   // the glyphs drawn as layers, in order; each one's first layer and how many
        readonly ushort[] layers, colours;          // each layer's glyph and palette entry
        readonly uint[] palette;                    // the first palette's colours, as ARGB

        const ushort TextColour = 0xFFFF;

        // (Read makes them; the tests make up tables that don't add up, which Read never gives)
        internal ColourGlyphs(ushort[] bases, ushort[] firsts, ushort[] counts, ushort[] layers, ushort[] colours, uint[] palette)
        {
            this.bases = bases;
            this.firsts = firsts;
            this.counts = counts;
            this.layers = layers;
            this.colours = colours;
            this.palette = palette;
        }

        // ---------- reading ----------

        // The tables in a font file's bytes, or null when it has none, or any count or place in them goes past its
        // table, a layer past the font's glyphs or a colour past the palette: then the font draws in one colour. A font
        // collection (.ttc) stays in one colour too; none of Windows' colour fonts is one.
        internal static ColourGlyphs Read(Stream font, int glyphCount)
        {
            byte[] head = Bytes(font, 0, 12);
            if (head == null || (U32(head, 0) != 0x00010000 && U32(head, 0) != 0x4F54544F && U32(head, 0) != 0x74727565)) return null;   // TrueType, 'OTTO', 'true'
            int tables = U16(head, 4);
            byte[] directory = Bytes(font, 12, 16 * tables);
            if (directory == null) return null;
            long colrAt = -1, colrLength = 0, cpalAt = -1, cpalLength = 0;
            for (int t = 0; t < tables; t++)
            {
                string tag = Encoding.ASCII.GetString(directory, 16 * t, 4);
                if (tag == "COLR") (colrAt, colrLength) = (U32(directory, 16 * t + 8), U32(directory, 16 * t + 12));
                else if (tag == "CPAL") (cpalAt, cpalLength) = (U32(directory, 16 * t + 8), U32(directory, 16 * t + 12));
            }
            if (colrAt < 0) return null;

            // COLR: its version (0, or 1, which starts as 0 does), then the base glyphs' and the layers' records
            byte[] colr = Bytes(font, colrAt, 14, colrLength);
            if (colr == null || U16(colr, 0) > 1) return null;
            int baseCount = U16(colr, 2), layerCount = U16(colr, 12);
            byte[] baseRecords = Bytes(font, colrAt + U32(colr, 4), 6 * baseCount, colrLength - U32(colr, 4));
            byte[] layerRecords = Bytes(font, colrAt + U32(colr, 8), 4 * layerCount, colrLength - U32(colr, 8));
            if (baseCount == 0 || baseRecords == null || layerRecords == null) return null;

            // CPAL: the first palette's colours, as blue, green, red and alpha (a font without one can only use the text's)
            var palette = new uint[0];
            if (cpalAt >= 0)
            {
                byte[] cpal = Bytes(font, cpalAt, 14, cpalLength);
                if (cpal == null || U16(cpal, 0) > 1 || U16(cpal, 4) == 0) return null;
                int entries = U16(cpal, 2), records = U16(cpal, 6), first = U16(cpal, 12);
                if (first + entries > records) return null;
                byte[] colours = Bytes(font, cpalAt + U32(cpal, 8) + 4L * first, 4 * entries, cpalLength - U32(cpal, 8) - 4L * first);
                if (colours == null) return null;
                palette = new uint[entries];
                for (int i = 0; i < entries; i++)
                    palette[i] = (uint)colours[4 * i + 3] << 24 | (uint)colours[4 * i + 2] << 16 | (uint)colours[4 * i + 1] << 8 | colours[4 * i];
            }

            var bases = new ushort[baseCount];
            var firsts = new ushort[baseCount];
            var counts = new ushort[baseCount];
            for (int i = 0; i < baseCount; i++)
            {
                bases[i] = U16(baseRecords, 6 * i);
                firsts[i] = U16(baseRecords, 6 * i + 2);
                counts[i] = U16(baseRecords, 6 * i + 4);
                if ((i > 0 && bases[i] <= bases[i - 1]) || firsts[i] + counts[i] > layerCount) return null;   // in order, as they're looked up
            }
            var layers = new ushort[layerCount];
            var colourOf = new ushort[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = U16(layerRecords, 4 * i);
                colourOf[i] = U16(layerRecords, 4 * i + 2);
                if (layers[i] >= glyphCount || (colourOf[i] >= palette.Length && colourOf[i] != TextColour)) return null;
            }
            return new ColourGlyphs(bases, firsts, counts, layers, colourOf, palette);
        }

        // `count` bytes at `at` in the font, or null when they aren't all there (or go past `room`, what's left of their
        // table)
        static byte[] Bytes(Stream font, long at, int count, long room = long.MaxValue)
        {
            if (at < 0 || count < 0 || count > room || !font.CanSeek || at + count > font.Length) return null;
            font.Position = at;
            var bytes = new byte[count];
            for (int got = 0; got < count; )
            {
                int read = font.Read(bytes, got, count - got);
                if (read <= 0) return null;
                got += read;
            }
            return bytes;
        }

        static ushort U16(byte[] b, int at) => (ushort)(b[at] << 8 | b[at + 1]);
        static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

        // The fonts met so far, by their file (and face, for one of a collection: the address's #1...), each with its
        // tables or null: a font is read once, however WPF hands it out (now and then another GlyphTypeface for the same
        // font, and one for each style it makes bold or italic, which share the file's tables), and the batch and print
        // threads draw captions too. The tests can give a font made-up tables.
        static readonly Dictionary<string, ColourGlyphs> fonts = new Dictionary<string, ColourGlyphs>();
        internal static Func<GlyphTypeface, ColourGlyphs> Of = Kept;

        // how many fonts are kept (for the tests)
        internal static int Met
        {
            get
            {
                lock (fonts) return fonts.Count;
            }
        }

        static ColourGlyphs Kept(GlyphTypeface font)
        {
            string file = font.FontUri.AbsoluteUri;
            lock (fonts)
            {
                if (!fonts.TryGetValue(file, out ColourGlyphs found))
                {
                    try
                    {
                        using (Stream bytes = font.GetFontStream()) found = Read(bytes, font.GlyphCount);
                    }
                    catch (Exception)   // a font whose bytes can't be read, for whatever reason (gone since, not allowed), draws in one colour
                    {
                        found = null;
                    }
                    fonts[file] = found;
                }
                return found;
            }
        }

        // the base glyph's record, or -1
        int Find(ushort glyph)
        {
            int at = Array.BinarySearch(bases, glyph);
            return at >= 0 && counts[at] > 0 ? at : -1;
        }

        // the colours of a glyph's layers in the order they're drawn (null: the text's colour); none for a glyph without
        internal List<Color?> ColoursOf(ushort glyph)
        {
            var found = new List<Color?>();
            int at = Find(glyph);
            if (at >= 0)
                for (int layer = firsts[at]; layer < firsts[at] + counts[at]; layer++)
                    found.Add(colours[layer] == TextColour ? (Color?)null : Colour(palette[colours[layer]]));
            return found;
        }

        // ---------- drawing ----------

        // Draws the text as DrawText does, but a colour font's glyphs as their layers, in their colours
        public static void DrawText(DrawingContext dc, FormattedText text, Point at)
        {
            var drawing = new DrawingGroup();
            using (DrawingContext inner = drawing.Open()) inner.DrawText(text, at);
            Paint(drawing);
            dc.DrawDrawing(drawing);
        }

        // each run of glyphs in a drawing of text that has a colour font's glyphs, put in colour
        static void Paint(DrawingGroup group)
        {
            DrawingCollection children = group.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is DrawingGroup inner) Paint(inner);
                else if (children[i] is GlyphRunDrawing glyphs && Layered(glyphs) is Drawing painted) children[i] = painted;
            }
        }

        // A run of glyphs as its colour font's layers, or null to leave WPF's own run, in one colour: for a font without
        // colour glyphs in the run, and for anything that goes wrong on the way (a font's tables come from outside the
        // program, and nothing in them may stop a drawing)
        static Drawing Layered(GlyphRunDrawing glyphs)
        {
            try
            {
                return glyphs.GlyphRun.IsSideways ? null : Of(glyphs.GlyphRun.GlyphTypeface)?.Layered(glyphs.GlyphRun, glyphs.ForegroundBrush);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A run's glyphs one at a time, each of this font's colour glyphs as its layers, one glyph each in its colour (the
        // text's brush for the rest); null when none of its glyphs has layers. (Glyphs rather than their outlines as
        // shapes, which Chromium draws: the same picture, which WPF draws and prints several times faster, an emoji of 120
        // layers being no rarity.)
        Drawing Layered(GlyphRun run, Brush text)
        {
            if (LayersIn(run) == 0) return null;
            var painted = new DrawingGroup();
            Each(run, (glyph, origin, found) =>
            {
                if (found < 0) painted.Children.Add(new GlyphRunDrawing(text, One(run, glyph, origin)));
                else
                    for (int layer = firsts[found]; layer < firsts[found] + counts[found]; layer++)
                        painted.Children.Add(new GlyphRunDrawing(colours[layer] == TextColour ? text : Brush(palette[colours[layer]]), One(run, layers[layer], origin)));
            });
            painted.Freeze();
            return painted;
        }

        // the layers of a run's glyphs, in all
        int LayersIn(GlyphRun run)
        {
            int count = 0;
            foreach (ushort glyph in run.GlyphIndices)
            {
                int found = Find(glyph);
                if (found >= 0) count += counts[found];
            }
            return count;
        }

        // Each glyph of a run where WPF draws it, with its record of layers (-1 for a glyph without): from the run's origin
        // by the advances before it and its offset, or right to left, with its own advance back from there
        void Each(GlyphRun run, Action<ushort, Point, int> glyph)
        {
            IList<ushort> glyphs = run.GlyphIndices;
            bool backwards = (run.BidiLevel & 1) != 0;
            IDictionary<ushort, double> advances = run.GlyphTypeface.AdvanceWidths;
            double pen = 0;
            for (int i = 0; i < glyphs.Count; i++)
            {
                Point offset = run.GlyphOffsets != null && i < run.GlyphOffsets.Count ? run.GlyphOffsets[i] : new Point();
                double x = run.BaselineOrigin.X + pen + offset.X;
                if (backwards) x = run.BaselineOrigin.X - pen - (advances.TryGetValue(glyphs[i], out double own) ? own * run.FontRenderingEmSize : 0) - offset.X;
                pen += run.AdvanceWidths[i];
                glyph(glyphs[i], new Point(x, run.BaselineOrigin.Y - offset.Y), Find(glyphs[i]));
            }
        }

        // a run of one glyph of the run's font, at `origin`
        static GlyphRun One(GlyphRun run, ushort glyph, Point origin) =>
            new GlyphRun(run.GlyphTypeface, 0, false, run.FontRenderingEmSize, (float)run.PixelsPerDip, new[] { glyph }, origin, new[] { 0.0 }, null, null, null, null, null, null);

        // ---------- an SVG picture's text ----------

        // The layers in a run of glyphs, in all: 0 when its font has none for them (or its tables can't be had), as
        // Layered has it
        internal static int Layers(GlyphRun run)
        {
            try
            {
                return run.IsSideways ? 0 : Of(run.GlyphTypeface)?.LayersIn(run) ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // A run of glyphs for an SVG picture's text, which is drawn as shapes: the glyphs without layers as their
        // outlines, each where WPF draws it (into `plain`), and each layer of a colour glyph as a run of that one glyph,
        // with its colour (null for palette entry 0xFFFF: the text's own paint). False, adding nothing, when none of the
        // glyphs has layers or anything goes wrong on the way, as Layered: then WPF's run is drawn in the text's paint.
        internal static bool Split(GlyphRun run, GeometryGroup plain, List<(GlyphRun Glyph, Color? Colour)> layered)
        {
            int had = plain.Children.Count, hadLayers = layered.Count;
            try
            {
                ColourGlyphs tables = run.IsSideways ? null : Of(run.GlyphTypeface);
                if (tables == null || tables.LayersIn(run) == 0) return false;
                tables.Each(run, (glyph, origin, found) =>
                {
                    if (found < 0)
                    {
                        Geometry outline = One(run, glyph, origin).BuildGeometry();
                        if (!outline.IsEmpty()) plain.Children.Add(outline);
                    }
                    else
                        for (int layer = tables.firsts[found]; layer < tables.firsts[found] + tables.counts[found]; layer++)
                            layered.Add((One(run, tables.layers[layer], origin), tables.colours[layer] == TextColour ? (Color?)null : Colour(tables.palette[tables.colours[layer]])));
                });
                return true;
            }
            catch (Exception)
            {
                while (plain.Children.Count > had) plain.Children.RemoveAt(plain.Children.Count - 1);
                layered.RemoveRange(hadLayers, layered.Count - hadLayers);
                return false;
            }
        }

        static Color Colour(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        static Brush Brush(uint argb)
        {
            var brush = new SolidColorBrush(Colour(argb));
            brush.Freeze();
            return brush;
        }
    }
}
