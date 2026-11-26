using FSO.Client.UI.Framework;

namespace Simitone.Conversations
{
    // Putting our pages on the game's screen and taking them off again.
    // The game's UIContainer.Remove does NOT clear the child's Parent (that line is commented out in FSO), so "Parent != null"
    // stays true after a page is closed. Every page that used it as "is open" or "is already on this screen" opened once, was
    // closed, and then never came back: Open() skipped the re-add and the Options button skipped Open altogether. This is the
    // only place that decides whether an element is really on a screen: it asks the screen's own child list.
    public static class ModalHost
    {
        public static bool IsOn(UIContainer screen, UIElement e)
        {
            return screen != null && e != null && screen.GetChildren().Contains(e);
        }

        // makes sure `e` is on `screen` (re-adding it after a Close or a screen change); returns false when there is no screen
        public static bool Show(UIElement e)
        {
            var screen = UIScreen.Current;
            if (screen == null || e == null) return false;
            if (e.Parent != null && !ReferenceEquals(e.Parent, screen)) Detach(e);   // left over from another screen
            if (!IsOn(screen, e)) screen.Add(e);
            return true;
        }

        // is `e` on the screen being drawn now
        public static bool IsShown(UIElement e)
        {
            return IsOn(UIScreen.Current, e);
        }

        public static void Detach(UIElement e)
        {
            if (e == null) return;
            var p = e.Parent;
            if (p != null) p.Remove(e);
            e.Parent = null;
        }
    }
}
