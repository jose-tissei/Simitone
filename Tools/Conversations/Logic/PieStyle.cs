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

        // slot width: icon, gaps and text, never narrower than the native button it covers (so the native one is fully hidden)
        public static float SlotWidth(float textWidth, float iconSize, float nativeWidth, bool submenu)
        {
            float w = 12 + iconSize + 8 + textWidth + (submenu ? 22 : 0) + 12;
            return Math.Max(w, nativeWidth + 8);
        }
    }
}
