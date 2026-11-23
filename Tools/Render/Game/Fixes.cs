using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Render
{
    // Small robustness fixes for the engine, called from patched code in the game's own assemblies.
    public static class Fixes
    {
        public static int TexturesReplaced;

        // texture references that got a placeholder, with that placeholder, so they can try loading again a little later
        static readonly System.Collections.Generic.List<KeyValuePair<object, Texture2D>> pending = new System.Collections.Generic.List<KeyValuePair<object, Texture2D>>();
        static System.Reflection.FieldInfo instanceField;
        static int frames;

        // AbstractTextureRef.Get loads a texture and then writes to its Tag; when the file could not be decoded the loader returns null and the
        // whole game died with "NullReferenceException ... AbstractTextureRef.Get" (seen when drawing an avatar while a lot was being switched,
        // after the texture cache was cleared). The patched Get passes what it loaded through here: a good texture is returned unchanged, a
        // missing one becomes a small magenta placeholder, so one bad texture shows up as a coloured patch (and a line in render.log) instead of
        // ending the game, and Tick makes the reference try loading the real texture again soon. Every call gets its own placeholder, because
        // the engine ties the texture's life to the reference that loaded it.
        public static Texture2D OrPlaceholder(Texture2D loaded, GraphicsDevice device, object owner)
        {
            if (loaded != null) return loaded;
            try
            {
                TexturesReplaced++;
                if (TexturesReplaced <= 20 || TexturesReplaced % 500 == 0)
                    PostAA.Log("a texture could not be loaded and was replaced by a placeholder (" + TexturesReplaced + " so far)");
                var t = new Texture2D(device, 2, 2);
                t.SetData(new[] { Color.Magenta, Color.Magenta, Color.Magenta, Color.Magenta });
                lock (pending) { if (pending.Count < 512) pending.Add(new KeyValuePair<object, Texture2D>(owner, t)); }
                return t;
            }
            catch (Exception e)
            {
                PostAA.Log("could not make a placeholder texture: " + e.Message);
                return loaded;
            }
        }

        // called once per frame (from the world blit hook): every ~1.5 seconds the references still holding a placeholder are told to load again
        public static void Tick()
        {
            if (++frames % 90 != 0) return;
            try
            {
                lock (pending)
                {
                    if (pending.Count == 0) return;
                    foreach (var p in pending)
                    {
                        // _Instance is a private field of the base class AbstractTextureRef; the owner may be a subclass of it
                        for (var t = p.Key.GetType(); instanceField == null && t != null; t = t.BaseType)
                            instanceField = t.GetField("_Instance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                        if (instanceField != null && ReferenceEquals(instanceField.GetValue(p.Key), p.Value)) instanceField.SetValue(p.Key, null);   // next Get loads it again
                    }
                    pending.Clear();
                }
            }
            catch (Exception e) { PostAA.Log("placeholder retry failed: " + e.Message); pending.Clear(); }
        }
    }
}
