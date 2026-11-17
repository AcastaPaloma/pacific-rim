using System;
using System.Collections.Generic;

namespace Game.UI
{
    public readonly struct HudRect
    {
        public readonly int X, Y, W, H;
        public HudRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public int Right { get { return X + W; } }
        public int Bottom { get { return Y + H; } }
        public bool Intersects(HudRect o) { return X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom; }
        public bool Inside(HudRect o) { return X >= o.X && Y >= o.Y && Right <= o.Right && Bottom <= o.Bottom; }
        public HudRect Scaled(int k) { return new HudRect(X * k, Y * k, W * k, H * k); }
        public override string ToString() { return X + "," + Y + " " + W + "x" + H; }
    }

    public sealed class HudElement
    {
        public string Id, Kind, Text;
        public HudRect Rect;
        public int Segments;
    }

    public sealed class HudLayoutException : Exception
    {
        public HudLayoutException(string m) : base(m) { }
    }

    /// <summary>
    /// HUD layout descriptor in integer reference coordinates (480x270, k = 4 at 1080p; R-122 4.1). Data lives in
    /// Assets/Game/Config/UI/hud_layout.json. Pure integer maths: no floats, so the layout is identical on every machine.
    /// </summary>
    public sealed class HudLayout
    {
        public const int RefW = 480, RefH = 270;
        /// <summary>No element may touch the central 40 % x 40 % of the frame (kaiju fights live there; P1 weight).</summary>
        public static readonly HudRect CentralZone = new HudRect(RefW * 30 / 100, RefH * 30 / 100, RefW * 40 / 100, RefH * 40 / 100);

        public readonly int RefWidth, RefHeight;
        public readonly IReadOnlyList<HudElement> Elements;

        HudLayout(int w, int h, List<HudElement> e) { RefWidth = w; RefHeight = h; Elements = e; }

        public static HudLayout Parse(string json)
        {
            object root;
            if (!MiniJson.TryParse(json, out root)) throw new HudLayoutException("not valid json");
            var o = MiniJson.Obj(root);
            if (o == null || MiniJson.Int(o, "schema", 0) != 1) throw new HudLayoutException("unsupported schema");
            int w = MiniJson.Int(o, "ref_w", 0), h = MiniJson.Int(o, "ref_h", 0);
            if (w != RefW || h != RefH) throw new HudLayoutException("reference size must be " + RefW + "x" + RefH);
            var arr = MiniJson.Arr(MiniJson.Get(o, "elements"));
            if (arr == null) throw new HudLayoutException("no elements");
            var list = new List<HudElement>();
            var ids = new HashSet<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                var e = MiniJson.Obj(arr[i]);
                if (e == null) throw new HudLayoutException("element " + i + " is not an object");
                string id = MiniJson.Str(e, "id", null);
                if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new HudLayoutException("missing or duplicate id at " + i);
                list.Add(new HudElement
                {
                    Id = id, Kind = MiniJson.Str(e, "kind", ""), Text = MiniJson.Str(e, "text", null), Segments = MiniJson.Int(e, "segments", 0),
                    Rect = new HudRect(MiniJson.Int(e, "x", 0), MiniJson.Int(e, "y", 0), MiniJson.Int(e, "w", 0), MiniJson.Int(e, "h", 0)),
                });
            }
            return new HudLayout(w, h, list);
        }

        public HudElement Find(string id)
        {
            for (int i = 0; i < Elements.Count; i++) if (Elements[i].Id == id) return Elements[i];
            return null;
        }

        /// <summary>Screen rect of an element at integer scale k (1080p: 4, 4K: 6).</summary>
        public HudRect Place(HudElement e, int k) { return e.Rect.Scaled(k < 1 ? 1 : k); }

        /// <summary>Problems with the geometry: out of frame, empty, inside the central zone, overlapping another element. Empty list = valid.</summary>
        public List<string> Validate()
        {
            var errs = new List<string>();
            var frame = new HudRect(0, 0, RefWidth, RefHeight);
            for (int i = 0; i < Elements.Count; i++)
            {
                var a = Elements[i];
                if (a.Rect.W <= 0 || a.Rect.H <= 0) errs.Add(a.Id + ": empty rect");
                if (!a.Rect.Inside(frame)) errs.Add(a.Id + ": outside the frame");
                if (a.Rect.Intersects(CentralZone)) errs.Add(a.Id + ": inside the central 40 % zone");
                for (int j = i + 1; j < Elements.Count; j++)
                    if (a.Rect.Intersects(Elements[j].Rect)) errs.Add(a.Id + ": overlaps " + Elements[j].Id);
            }
            return errs;
        }

        /// <summary>Ids of text elements whose string does not fit its rect at the given text step and base scale k (M-NEW-c: expect none at S, M, L).</summary>
        public List<string> ClippedText(TextScaleStep step, int k)
        {
            var bad = new List<string>();
            int ts = HudTextLadder.PixelScale(step, k);
            int rk = k < 1 ? 1 : k;
            for (int i = 0; i < Elements.Count; i++)
            {
                var e = Elements[i];
                if (e.Kind != "text" || e.Text == null) continue;
                if (HudFont.MeasureWidth(e.Text, ts, step) > e.Rect.W * rk || HudFont.HeightPx(ts, step) > e.Rect.H * rk) bad.Add(e.Id);
            }
            return bad;
        }
    }
}
