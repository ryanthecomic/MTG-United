using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MTGUnited
{
    /// <summary>
    /// Small self-contained JSON reader/writer so the project needs no extra packages.
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            return v;
        }

        public static Dictionary<string, object> ParseObject(string text) => Parse(text) as Dictionary<string, object>;

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObj(s, ref i);
                case '[': return ParseArr(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static Dictionary<string, object> ParseObj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                i++; // :
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("Expected , or } at " + i);
            }
        }

        static List<object> ParseArr(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("Expected , or ] at " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                }
            }
            throw new FormatException("Unterminated string");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- writing

        public static string Write(object o)
        {
            var sb = new StringBuilder();
            WriteValue(sb, o);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString(CultureInfo.InvariantCulture)); break;
                case byte by: sb.Append(by.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool f2 = true;
                    foreach (var item in list)
                    {
                        if (!f2) sb.Append(',');
                        f2 = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default: WriteString(sb, o.ToString()); break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- typed accessors

        public static string Str(this Dictionary<string, object> d, string k, string def = "")
            => d != null && d.TryGetValue(k, out var v) && v != null ? v.ToString() : def;

        public static int Int(this Dictionary<string, object> d, string k, int def = 0)
            => d != null && d.TryGetValue(k, out var v) && v is double n ? (int)n : def;

        public static bool Bool(this Dictionary<string, object> d, string k, bool def = false)
            => d != null && d.TryGetValue(k, out var v) && v is bool b ? b : def;

        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string k)
            => d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(this Dictionary<string, object> d, string k)
            => d != null && d.TryGetValue(k, out var v) && v is List<object> l ? l : new List<object>();

        public static List<int> IntList(this Dictionary<string, object> d, string k)
        {
            var r = new List<int>();
            foreach (var o in d.Arr(k)) if (o is double n) r.Add((int)n);
            return r;
        }
    }
}
