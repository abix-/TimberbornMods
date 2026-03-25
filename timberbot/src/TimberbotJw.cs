using System.Text;

namespace Timberbot
{
    // fluent zero-allocation JSON writer. allocate once as a field, Reset() per request.
    // auto-handles commas between array items and object keys. nesting-aware up to 16 levels.
    //
    // usage:
    //   _jw.Reset().OpenArr();
    //   _jw.OpenObj().Key("id").Int(1).Key("name").Str("Path").CloseObj();
    //   _jw.OpenObj().Key("id").Int(2).Key("name").Str("Farm").CloseObj();
    //   _jw.CloseArr();
    //   return _jw.ToString();
    public class TimberbotJw
    {
        private readonly StringBuilder _sb;
        private int _depth;
        private readonly bool[] _hasValue = new bool[16];

        public TimberbotJw(int capacity = 100000) { _sb = new StringBuilder(capacity); }

        public TimberbotJw Reset() { _sb.Clear(); _depth = 0; _hasValue[0] = false; return this; }

        public TimberbotJw OpenArr() { AutoSep(); _sb.Append('['); _hasValue[++_depth] = false; return this; }
        public TimberbotJw CloseArr() { _sb.Append(']'); _depth--; _hasValue[_depth] = true; return this; }
        public TimberbotJw OpenObj() { AutoSep(); _sb.Append('{'); _hasValue[++_depth] = false; return this; }
        public TimberbotJw CloseObj() { _sb.Append('}'); _depth--; _hasValue[_depth] = true; return this; }

        public TimberbotJw Key(string name) { AutoSep(); _sb.Append('"'); _sb.Append(name); _sb.Append("\":"); _hasValue[_depth] = false; return this; }
        public TimberbotJw Bool(bool v) { AutoSep(); _sb.Append(v ? "true" : "false"); _hasValue[_depth] = true; return this; }
        public TimberbotJw Int(int v) { AutoSep(); _sb.Append(v); _hasValue[_depth] = true; return this; }
        public TimberbotJw Long(long v) { AutoSep(); _sb.Append(v); _hasValue[_depth] = true; return this; }
        public TimberbotJw Float(float v, string fmt = "F2")
        {
            AutoSep();
            // zero-alloc: write digits directly instead of v.ToString(fmt) which allocates
            if (v < 0) { _sb.Append('-'); v = -v; }
            int whole = (int)v;
            _sb.Append(whole);
            _sb.Append('.');
            float frac = v - whole;
            if (fmt == "F1")
            {
                _sb.Append((int)(frac * 10 + 0.5f));
            }
            else // F2 default
            {
                int d = (int)(frac * 100 + 0.5f);
                if (d < 10) _sb.Append('0');
                _sb.Append(d);
            }
            _hasValue[_depth] = true;
            return this;
        }
        public TimberbotJw Str(string v) { AutoSep(); _sb.Append('"'); _sb.Append(v ?? ""); _sb.Append('"'); _hasValue[_depth] = true; return this; }
        public TimberbotJw Null() { AutoSep(); _sb.Append("null"); _hasValue[_depth] = true; return this; }
        public TimberbotJw Raw(string json) { AutoSep(); _sb.Append(json); _hasValue[_depth] = true; return this; }

        public override string ToString() => _sb.ToString();

        private void AutoSep()
        {
            if (_hasValue[_depth]) _sb.Append(',');
        }
    }
}
