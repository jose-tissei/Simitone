using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Simitone.HtmlUi
{
    // Tiny logic-less template: the HTML page stays a design file, the game only supplies data.
    //   {{name}} {{card.title}}              value, HTML-escaped (looked up in the innermost scope first)
    //   <!--#each list--> ... <!--/each-->   repeat for every item of a list; {{.}} is the item itself, {{#}} its index
    //   <!--#if name--> ... <!--#else--> ... <!--/if-->   shown when the value is present, true, non-zero or a non-empty list/string
    // Data is Dictionary<string, object>, List<object>, string, bool or numbers.
    public static class Template
    {
        static readonly Regex Tag = new Regex(@"<!--(#each|#if|#else|/each|/if)(?:\s+([\w.]+))?\s*-->|\{\{\s*([\w.#]+)\s*\}\}", RegexOptions.Compiled);

        class Node
        {
            public string Kind;                 // text | var | each | if
            public string Text;                 // text, or the path of var / each / if
            public List<Node> Body = new List<Node>(), Else = new List<Node>();
        }

        public static string Render(string template, IDictionary<string, object> data)
        {
            int pos = 0;
            var nodes = Parse(template, ref pos, null);
            var sb = new StringBuilder();
            Emit(nodes, new List<object> { data }, sb);
            return sb.ToString();
        }

        // parses up to the closing tag named end ("/each" or "/if"); an #else switches the target list of an open #if
        static List<Node> Parse(string t, ref int pos, string end)
        {
            var list = new List<Node>();
            while (true)
            {
                var m = Tag.Match(t, pos);
                if (!m.Success)
                {
                    if (end != null) throw new InvalidOperationException("template: missing <!--" + end + "-->");
                    if (pos < t.Length) list.Add(new Node { Kind = "text", Text = t.Substring(pos) });
                    pos = t.Length;
                    return list;
                }
                if (m.Index > pos) list.Add(new Node { Kind = "text", Text = t.Substring(pos, m.Index - pos) });
                pos = m.Index + m.Length;
                if (m.Groups[3].Success) { list.Add(new Node { Kind = "var", Text = m.Groups[3].Value }); continue; }
                string kind = m.Groups[1].Value;
                if (kind == "/each" || kind == "/if")
                {
                    if (kind != end) throw new InvalidOperationException("template: unexpected <!--" + kind + "-->");
                    return list;
                }
                if (kind == "#else")
                {
                    if (end != "/if") throw new InvalidOperationException("template: <!--#else--> outside an #if");
                    // everything after the else belongs to the else branch of the enclosing if: signalled by a marker node
                    list.Add(new Node { Kind = "else" });
                    continue;
                }
                var n = new Node { Kind = kind == "#each" ? "each" : "if", Text = m.Groups[2].Value };
                var inner = Parse(t, ref pos, kind == "#each" ? "/each" : "/if");
                int split = inner.FindIndex(x => x.Kind == "else");
                if (split >= 0) { n.Body = inner.GetRange(0, split); n.Else = inner.GetRange(split + 1, inner.Count - split - 1); }
                else n.Body = inner;
                list.Add(n);
            }
        }

        static void Emit(List<Node> nodes, List<object> scope, StringBuilder sb)
        {
            foreach (var n in nodes)
            {
                switch (n.Kind)
                {
                    case "text": sb.Append(n.Text); break;
                    case "var": sb.Append(Escape(Lookup(scope, n.Text))); break;
                    case "if": Emit(Truthy(Lookup(scope, n.Text)) ? n.Body : n.Else, scope, sb); break;
                    case "each":
                        var list = Lookup(scope, n.Text) as System.Collections.IEnumerable;
                        if (list == null || list is string) break;
                        int i = 0;
                        foreach (var item in list)
                        {
                            var inner = new List<object>(scope) { new Item { Value = item, Index = i++ } };
                            Emit(n.Body, inner, sb);
                        }
                        break;
                }
            }
        }

        class Item { public object Value; public int Index; }

        static object Lookup(List<object> scope, string path)
        {
            if (path == "." || path == "#")
            {
                var item = scope[scope.Count - 1] as Item;
                return item == null ? null : (path == "." ? item.Value : (object)item.Index);
            }
            var parts = path.Split('.');
            for (int s = scope.Count - 1; s >= 0; s--)
            {
                object cur = scope[s] is Item ? ((Item)scope[s]).Value : scope[s];
                bool ok = true;
                foreach (var part in parts)
                {
                    var d = cur as IDictionary<string, object>;
                    if (d == null || !d.TryGetValue(part, out cur)) { ok = false; break; }
                }
                if (ok) return cur;
            }
            return null;
        }

        static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is string) return ((string)v).Length > 0;
            if (v is int) return (int)v != 0;
            var e = v as System.Collections.IEnumerable;
            if (e != null) return e.GetEnumerator().MoveNext();
            return true;
        }

        static string Escape(object v)
        {
            if (v == null) return "";
            string s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
