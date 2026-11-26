using System;

namespace Simitone.Conversations
{
    // The Memory tab badge counts the News stories that the News tab has not shown yet (Game/TabBadge.cs draws it).
    public static class TabBadgeLogic
    {
        public static int Unread(int total, int shown) { return Math.Max(0, total - shown); }

        // "7", or "99+" so the badge keeps its size
        public static string Label(int unread) { return unread > 99 ? "99+" : unread.ToString(); }
    }
}
