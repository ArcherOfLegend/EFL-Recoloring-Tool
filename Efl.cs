// Efl.cs - parsing, smear detection and the recolour transform.
// Straight port of efl_recolor.py, same numbers out.
//
// by ArcherOfLegend

namespace EflRecolor;

/// Two ways to recolour, because smears are not all the same.
///
///   Ryu     40 sites, grey 100%
///   Chun    24 sites, grey 100%
///   Spencer 76 sites, grey 89%, green 10%
///   Hulk   200 sites, green 78%, blue 16%, cyan 4%, grey 2%
///
/// Tint sets one hue on everything - right for a plain grey smear where there
/// is no relationship to lose. Shift rotates every hue by the same amount and
/// leaves greys alone - right for one that already carries colour, because
/// Hulk's green, blue and cyan stay three distinct things instead of
/// collapsing into one.
public enum Mode { Tint, Shift }

public enum SiteKind { Primary, Primary2, Secondary, Secondary2, TrackKey }

public readonly struct Site
{
    public readonly int Offset;
    public readonly SiteKind Kind;
    public Site(int offset, SiteKind kind) { Offset = offset; Kind = kind; }

    public string KindLabel => Kind switch
    {
        SiteKind.Primary    => "primary",
        SiteKind.Primary2   => "primary 2",
        SiteKind.Secondary  => "secondary",
        SiteKind.Secondary2 => "secondary 2",
        _                   => "track key",
    };
}

public sealed class Check
{
    public string Name = "";
    public string Detail = "";
    public bool Passed;
    /// notes don't count toward the score, they just tell you about the file
    public bool Info;
}

public sealed class Efl
{
    public const int DataBase = 0x30;

    // fixed struct size per render tag. anything past this is tracks.
    static readonly Dictionary<int, int> Fixed = new()
    {
        [0] = 0x1A0, [1] = 0x200, [2] = 0x1F0, [4] = 0x060,
        [5] = 0x1B0, [6] = 0x260, [10] = 0x080, [20] = 0x1C0,
    };

    // where the second colour pair lives, per tag. tags 0 and 2 don't have one.
    static readonly Dictionary<int, int> SecondColour = new() { [6] = 0x178, [4] = 0x58 };

    public byte[] Data;
    public int NodeCount, GroupCount;
    readonly List<(int tag, int off)[]> _nodes = new();
    readonly List<int> _groupPtrs = new();

    public Efl(byte[] bytes)
    {
        Data = bytes;
        if (Data.Length < 0x30 || Data[0] != 'E' || Data[1] != 'F' || Data[2] != 'L' || Data[3] != 0)
            throw new InvalidDataException("Not an EFL file.");

        int total = U32(8);
        if (total + DataBase != Data.Length)
            throw new InvalidDataException(
                $"Size mismatch. The header says 0x{total + DataBase:X}, the file is 0x{Data.Length:X}.");

        NodeCount = BitConverter.ToUInt16(Data, 0x10);
        GroupCount = BitConverter.ToUInt16(Data, 0x12);

        for (int i = 0; i < NodeCount; i++)
        {
            var row = new (int, int)[4];
            for (int j = 0; j < 4; j++)
            {
                int p = DataBase + i * 16 + j * 4;
                // packed [u8 tag][u24 offset], and the offset is relative to 0x30.
                // an offset of 0 means null
                int off = Data[p + 1] | (Data[p + 2] << 8) | (Data[p + 3] << 16);
                row[j] = (Data[p], off == 0 ? -1 : DataBase + off);
            }
            _nodes.Add(row);
        }

        int b = DataBase + NodeCount * 16;
        for (int i = 0; i < GroupCount; i++)
            _groupPtrs.Add(U32(b + 4 * i) + DataBase);
    }

    public int U32(int off) => BitConverter.ToInt32(Data, off);
    public void SetU32(int off, uint v) => BitConverter.GetBytes(v).CopyTo(Data, off);
    public uint Colour(int off) => BitConverter.ToUInt32(Data, off);

    List<int> Pool(int col)
    {
        var set = new SortedSet<int>();
        foreach (var n in _nodes) if (n[col].off >= 0) set.Add(n[col].off);
        return set.ToList();
    }


    public List<(int off, int size, int tag)> RenderBlocks()
    {
        var b = Pool(1);
        var c = Pool(2);
        int end = c.Count > 0 ? c[0] : _groupPtrs[0];

        var tagOf = new Dictionary<int, int>();
        foreach (var n in _nodes) if (n[1].off >= 0) tagOf[n[1].off] = n[1].tag;

        var outp = new List<(int, int, int)>();
        for (int i = 0; i < b.Count; i++)
        {
            int next = i + 1 < b.Count ? b[i + 1] : end;
            outp.Add((b[i], next - b[i], tagOf[b[i]]));
        }
        return outp;
    }


    bool TrackOk(int p, int count, int end)
    {
        if (count < 1 || count > 32) return false;
        if (p + ((4 + 12 * count + 0xF) & ~0xF) > end) return false;
        int prev = 0;
        for (int k = 0; k < count; k++)
        {
            int frame = U32(p + 4 + k * 12);
            if (frame < 0 || frame > 4096) return false;
            if (k > 0 && frame <= prev) return false;
            prev = frame;
        }
        return true;
    }

    static bool LooksLikeFloat(uint v)
    {
        if (v == 0) return true;
        float f = BitConverter.ToSingle(BitConverter.GetBytes(v));
        float a = Math.Abs(f);
        return a > 1e-4f && a < 1e5f;
    }


    public List<Site> ColourSites()
    {
        var sites = new List<Site>();
        foreach (var (off, size, tag) in RenderBlocks())
        {
            sites.Add(new Site(off + 0x48, SiteKind.Primary));
            sites.Add(new Site(off + 0x4C, SiteKind.Primary2));

            if (SecondColour.TryGetValue(tag, out int sec) && size >= sec + 8)
            {
                sites.Add(new Site(off + sec, SiteKind.Secondary));
                sites.Add(new Site(off + sec + 4, SiteKind.Secondary2));
            }

            if (!Fixed.TryGetValue(tag, out int fixedSize)) continue;
            int p = off + fixedSize, end = off + size;
            while (p + 4 <= end)
            {
                int count = (int)(Colour(p) & 0xFF);
                if (TrackOk(p, count, end))
                {
                    int live = 0, colourish = 0;
                    for (int k = 0; k < count; k++)
                    {
                        uint v0 = Colour(p + 8 + k * 12), v1 = Colour(p + 12 + k * 12);
                        if (v0 == 0 && v1 == 0) continue;
                        live++;
                        if (!LooksLikeFloat(v0)) colourish++;
                    }
                    // all keys in a track are the same type
                    if (live > 0 && colourish * 2 > live)
                        for (int k = 0; k < count; k++)
                        {
                            sites.Add(new Site(p + 8 + k * 12, SiteKind.TrackKey));
                            sites.Add(new Site(p + 12 + k * 12, SiteKind.TrackKey));
                        }
                    p += (4 + 12 * count + 0xF) & ~0xF;
                }
                else p += 0x10;
            }
        }
        return sites;
    }

    public List<string> Textures()
    {
        var outp = new List<string>();
        foreach (var (off, size, tag) in RenderBlocks())
        {
            if (tag == 5 || size < 0xB0) continue;
            int end = off + 0x70;
            while (end < off + 0xB0 && Data[end] != 0) end++;
            if (end == off + 0x70) continue;
            string s = System.Text.Encoding.ASCII.GetString(Data, off + 0x70, end - off - 0x70);
            int slash = s.LastIndexOf('\\');
            outp.Add(slash >= 0 ? s[(slash + 1)..] : s);
        }
        return outp;
    }

    static bool IsNeutral(uint v)
    {
        int r = (int)((v >> 16) & 255), g = (int)((v >> 8) & 255), b = (int)(v & 255);
        return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 24;
    }

    public List<Check> Detect()
    {
        var sites = ColourSites();
        var live = new List<uint>();
        foreach (var s in sites)
        {
            uint c = Colour(s.Offset);
            if ((c & 0xFFFFFF) != 0) live.Add(c);
        }
        double neutral = live.Count == 0 ? 0 : live.Count(IsNeutral) / (double)live.Count;

        var uniq = Textures().Distinct().OrderBy(x => x).ToList();
        bool trail = uniq.Any(t => t.ToLower().Contains("kiseki"));

        var spawn = new List<int>();
        foreach (int off in Pool(0))
        {
            uint v = Colour(off + 0x48);
            if ((v & 0xFFFF) != 0) spawn.Add((int)(v & 0xFFFF));
            if (((v >> 16) & 0xFFFF) != 0) spawn.Add((int)((v >> 16) & 0xFFFF));
        }
        double oneRatio = spawn.Count == 0 ? 0 : spawn.Count(x => x == 1) / (double)spawn.Count;

        var motions = Pool(3);
        int degen = 0;
        for (int i = 0; i < motions.Count; i++)
        {
            int o = motions[i];
            int next = i + 1 < motions.Count ? motions[i + 1] : _groupPtrs[0];
            if (next - o < 0x50) { degen++; continue; }
            bool allZero = true;
            for (int k = 0; k < 6; k++)
                if (BitConverter.ToSingle(Data, o + 0x10 + k * 4) != 0f) { allZero = false; break; }
            if (allZero) degen++;
        }

        string texList = uniq.Count == 0 ? "none"
            : uniq.Count <= 3 ? string.Join(", ", uniq)
            : $"{uniq.Count}: " + string.Join(", ", uniq.Take(2)) + ", ...";

        return new List<Check>
        {
            new() { Name = "Uses a trail sheet", Passed = trail,
                    Detail = trail ? "found a kiseki texture" : "no kiseki texture" },
            new() { Name = "Few textures", Passed = uniq.Count <= 6, Detail = texList },
            new() { Name = "Particle percent per emitter", Passed = oneRatio >= 0.80,
                    Detail = $"{oneRatio * 100:0}% of emitters" },
            new() { Name = "Low Motion Count", Passed = degen == motions.Count,
                    Detail = $"{degen} of {motions.Count} motion blocks" },
            new() { Info = true, Name = neutral >= 0.80 ? "Neutral" : "Already coloured",
                    Detail = neutral >= 0.80
                        ? "recolouring tints it"
                        : $"only {neutral * 100:0}% grey." },
        };
    }

    /// nothing should have moved. same size, header still adds up, and every
    /// render block still walks end to end.
    public string Verify(Efl before)
    {
        if (Data.Length != before.Data.Length) return "the file changed size";
        if (U32(8) + DataBase != Data.Length) return "the header no longer matches the file size";
        foreach (var (off, size, tag) in RenderBlocks())
        {
            if (!Fixed.TryGetValue(tag, out int fixedSize)) continue;
            int p = off + fixedSize, end = off + size;
            while (p + 4 <= end)
            {
                int count = (int)(Colour(p) & 0xFF);
                p += TrackOk(p, count, end) ? ((4 + 12 * count + 0xF) & ~0xF) : 0x10;
            }
            if (p != end) return $"render block 0x{off:X4} no longer reads correctly";
        }
        if (ColourSites().Count != before.ColourSites().Count)
            return "the list of colour sites changed";
        return null;
    }

    // ---- colour maths -------------------------------------------------------
    // System.Drawing gives HSL, not HSV, and we want value so the trail keeps
    // its falloff. So do it by hand.

    public static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        v = max;
        double d = max - min;
        s = max == 0 ? 0 : d / max;
        if (d == 0) { h = 0; return; }
        if (max == rf) h = 60 * (((gf - bf) / d) % 6);
        else if (max == gf) h = 60 * (((bf - rf) / d) + 2);
        else h = 60 * (((rf - gf) / d) + 4);
        if (h < 0) h += 360;
    }

    public static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60 % 2) - 1)), m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb((int)Math.Round((r + m) * 255),
                              (int)Math.Round((g + m) * 255),
                              (int)Math.Round((b + m) * 255));
    }

    /// how grey the file is overall. drives which mode gets suggested.
    public double GreyFraction()
    {
        var live = ColourSites().Select(s => Colour(s.Offset))
                                .Where(c => (c & 0xFFFFFF) != 0).ToList();
        return live.Count == 0 ? 1 : live.Count(IsNeutral) / (double)live.Count;
    }

    /// circular mean hue of the coloured sites, ignoring greys. Shift rotates
    /// relative to this so the file's own palette moves as a unit.
    public double DominantHue()
    {
        double x = 0, y = 0;
        foreach (var s in ColourSites())
        {
            uint c = Colour(s.Offset);
            if ((c & 0xFFFFFF) == 0) continue;
            RgbToHsv((int)((c >> 16) & 255), (int)((c >> 8) & 255), (int)(c & 255),
                     out double h, out double sat, out _);
            if (sat < 0.10) continue;
            x += Math.Cos(h * Math.PI / 180);
            y += Math.Sin(h * Math.PI / 180);
        }
        if (x == 0 && y == 0) return 0;
        double d = Math.Atan2(y, x) * 180 / Math.PI;
        return d < 0 ? d + 360 : d;
    }
    public static uint Retint(uint argb, double hue, double sat, double valueScale = 1.0)
    {
        uint a = (argb >> 24) & 255;
        RgbToHsv((int)((argb >> 16) & 255), (int)((argb >> 8) & 255), (int)(argb & 255),
                 out _, out _, out double v);
        Color c = HsvToColor(hue, sat, Math.Clamp(v * valueScale, 0, 1));
        return (a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    public sealed class Recolour
    {
        public double Hue;
        public double Sat = 0.85;
        public double Val = 1.0;
        public Mode Mode = Mode.Tint;
        public bool KeepWhite;
        public double DominantHue;

        /// a site counts as grey below this saturation
        const double GreyCut = 0.06;

        public uint Apply(uint argb)
        {
            uint a = (argb >> 24) & 255;
            RgbToHsv((int)((argb >> 16) & 255), (int)((argb >> 8) & 255), (int)(argb & 255),
                     out double h, out double s, out double v);

            if (KeepWhite && s <= GreyCut && v >= 0.98) return argb;

            double nh, ns;
            if (Mode == Mode.Tint) { nh = Hue; ns = Sat; }
            else if (s <= GreyCut) { nh = h; ns = s; }        // grey stays grey
            else { nh = h + (Hue - DominantHue); ns = s; }    // rotate as a unit

            Color c = HsvToColor(nh, ns, Math.Clamp(v * Val, 0, 1));
            return (a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        }
    }

    public static Color ToColor(uint argb) =>
        Color.FromArgb(255, (int)((argb >> 16) & 255), (int)((argb >> 8) & 255), (int)(argb & 255));
}