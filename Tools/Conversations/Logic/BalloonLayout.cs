using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    // Layout rules for the speech balloons over the Sims. Pure geometry and text, so it is unit tested; the overlay
    // only supplies the measuring function and draws the result.
    public static class BalloonLayout
    {
        // Breaks text into lines no wider than maxWidth (words are kept whole; a single over-long word gets its own line).
        public static List<string> Wrap(string text, float maxWidth, Func<string, float> measure)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            var cur = "";
            foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var test = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && measure(test) > maxWidth) { lines.Add(cur); cur = word; }
                else cur = test;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        public struct Box { public float X, Y, W, H; }

        // Puts a balloon of the given size above an anchor point (the Sim's head), centred, kept inside the screen.
        public static Box PlaceAbove(float anchorX, float anchorY, float w, float h, float screenW, float screenH, float gap = 14)
        {
            float x = anchorX - w / 2, y = anchorY - h - gap;
            x = Math.Max(4, Math.Min(x, screenW - w - 4));
            y = Math.Max(4, Math.Min(y, screenH - h - 4));
            return new Box { X = x, Y = y, W = w, H = h };
        }

        public static bool Overlaps(Box a, Box b)
        {
            return a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
        }

        // Moves a balloon up until it no longer covers the one already placed (two Sims standing close together).
        public static Box AvoidOverlap(Box placed, Box b, float gap = 6)
        {
            if (!Overlaps(placed, b)) return b;
            b.Y = placed.Y - b.H - gap;
            if (b.Y < 4) b.Y = placed.Y + placed.H + gap;   // no room above: go below it instead
            return b;
        }
    }
}
