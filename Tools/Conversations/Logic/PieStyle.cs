using System;

namespace Simitone.Conversations
{
    // Pure helpers for the redesigned pie menu (Game/PieOverlay.cs): which icon from the design's sheet fits a menu label,
    // and how wide a slot is. Names are entries of iconSheet.order in design-tokens.json.
    public static class PieStyle
    {
        static readonly string[][] Rules =
        {
            new[] { "study", "book" }, new[] { "practice", "book" }, new[] { "read", "book" },   // first: "Study Cooking" is studying, not food
            new[] { "kiss", "kiss" }, new[] { "romance", "heart" }, new[] { "flirt", "heart" }, new[] { "woo", "heart" }, new[] { "hug", "heart" }, new[] { "cheer", "smile" }, new[] { "comfort", "heart" },
            new[] { "talk", "chat" }, new[] { "chat", "chat" }, new[] { "joke", "smile" }, new[] { "gossip", "chat" }, new[] { "brag", "star" }, new[] { "compliment", "star" }, new[] { "apolog", "hand" },
            new[] { "dance", "note" }, new[] { "stereo", "note" }, new[] { "music", "note" }, new[] { "play", "play" },
            new[] { "sleep", "bed" }, new[] { "nap", "bed" }, new[] { "bed", "bed" },
            new[] { "eat", "food" }, new[] { "cook", "food" }, new[] { "serve", "food" }, new[] { "snack", "food" }, new[] { "drink", "drop" }, new[] { "fridge", "food" },
            new[] { "shower", "drop" }, new[] { "bath", "drop" }, new[] { "wash", "drop" }, new[] { "swim", "drop" }, new[] { "water", "drop" },
            new[] { "toilet", "wc" }, new[] { "clean", "brush" }, new[] { "mop", "brush" }, new[] { "sweep", "brush" }, new[] { "paint", "brush" },
            new[] { "read", "book" }, new[] { "book", "book" }, new[] { "study", "book" }, new[] { "practice", "book" }, new[] { "learn", "book" },
            new[] { "call", "mail" }, new[] { "phone", "mail" }, new[] { "mail", "mail" }, new[] { "gift", "gift" }, new[] { "give", "gift" },
            new[] { "watch", "eye" }, new[] { "look", "eye" }, new[] { "inspect", "eye" }, new[] { "examine", "eye" },
            new[] { "sit", "sofa" }, new[] { "couch", "sofa" }, new[] { "sofa", "sofa" }, new[] { "chair", "chair" },
            new[] { "work", "job" }, new[] { "job", "job" }, new[] { "career", "job" },
            new[] { "fight", "alert" }, new[] { "slap", "alert" }, new[] { "yell", "alert" }, new[] { "insult", "alert" }, new[] { "argue", "alert" }, new[] { "scare", "alert" },
            new[] { "trash", "trash" }, new[] { "throw", "trash" }, new[] { "delete", "trash" }, new[] { "sell", "coin" }, new[] { "buy", "coin" }, new[] { "pay", "coin" },
            new[] { "repair", "hammer" }, new[] { "fix", "hammer" }, new[] { "build", "hammer" }, new[] { "upgrade", "hammer" },
            new[] { "go here", "right" }, new[] { "move", "right" }, new[] { "follow", "right" }, new[] { "go ", "right" },
            new[] { "light", "bulb" }, new[] { "lamp", "bulb" }, new[] { "tv", "eye" }, new[] { "computer", "grid" }, new[] { "turn", "bolt" },
            new[] { "cancel", "close" }, new[] { "back", "left" },
        };

        // the icon for a menu label such as "Talk...", "Take a Shower" or "Romance/Kiss"; "hand" when nothing fits
        public static string IconFor(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "hand";
            var s = label.ToLowerInvariant();
            foreach (var r in Rules) if (s.Contains(r[0])) return r[1];
            return "hand";
        }

        public static bool IsSubmenu(string caption) { return caption != null && caption.TrimEnd().EndsWith("..."); }

        public static string CleanLabel(string caption) { return (caption ?? "").Trim().TrimEnd('.').Trim(); }

        // "Kiss - Polite" shows as "Polite" (the icon already says kiss): long submenu labels would overlap their neighbours otherwise
        public static string ShortLabel(string caption)
        {
            var s = CleanLabel(caption);
            int i = s.LastIndexOf(" - ", StringComparison.Ordinal);
            return i > 0 && i + 3 < s.Length ? s.Substring(i + 3).Trim() : s;
        }

        // The design's orbit: slots sit evenly spaced (by angle, clockwise from the top like the native pie) on an ellipse around the Sim's head,
        // 6 to 8 of them on rx 235 / ry 128. When they would touch (long labels, more than 8 entries) the ellipse grows until every slot
        // has `margin` px of air; the back button of a submenu sits in the middle and is kept clear as well. Returns slot centres.
        public static void Orbit(float[] widths, float slotH, bool hasBack, float backW, float rx, float ry, float margin, out float[] cx, out float[] cy)
        {
            int n = widths.Length;
            cx = new float[n]; cy = new float[n];
            for (float k = 1f; k < 4f; k += 0.04f)
            {
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    cx[i] = (float)(Math.Sin(a) * rx * k); cy[i] = (float)(-Math.Cos(a) * ry * k);
                }
                bool clear = true;
                for (int i = 0; i < n && clear; i++)
                {
                    if (hasBack && Overlap(cx[i], cy[i], widths[i], 0, 0, backW, slotH, slotH, margin)) clear = false;
                    for (int j = i + 1; j < n && clear; j++)
                        if (Overlap(cx[i], cy[i], widths[i], cx[j], cy[j], widths[j], slotH, slotH, margin)) clear = false;
                }
                if (clear) return;
            }
        }

        static bool Overlap(float x1, float y1, float w1, float x2, float y2, float w2, float h1, float h2, float margin)
        {
            return Math.Abs(x1 - x2) < (w1 + w2) / 2 + margin && Math.Abs(y1 - y2) < (h1 + h2) / 2 + margin;
        }

        // slot width: icon, gaps and text, never narrower than the native button it covers (so the native one is fully hidden)
        public static float SlotWidth(float textWidth, float iconSize, float nativeWidth, bool submenu)
        {
            float w = 12 + iconSize + 8 + textWidth + (submenu ? 22 : 0) + 12;
            return Math.Max(w, nativeWidth + 8);
        }
    }
}
