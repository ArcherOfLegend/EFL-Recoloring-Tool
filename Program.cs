// Program.cs
// by ArcherOfLegend

using System.Drawing.Drawing2D;

namespace EflRecolor;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

static class Ink
{
    public static readonly Color Bg   = Color.FromArgb(0x0F, 0x0F, 0x11);
    public static readonly Color Card = Color.FromArgb(0x18, 0x18, 0x1B);
    public static readonly Color Line = Color.FromArgb(0x27, 0x27, 0x2B);
    public static readonly Color Text = Color.FromArgb(0xEC, 0xEC, 0xEE);
    public static readonly Color Dim  = Color.FromArgb(0x7C, 0x7C, 0x85);
    public static readonly Color Good = Color.FromArgb(0x5F, 0xD3, 0x8D);
    public static readonly Color Bad  = Color.FromArgb(0xE5, 0x6B, 0x6B);

    public static Font Ui(float s = 9.5f) => new("Segoe UI", s);
    public static Font Mono(float s = 9f) => new("Consolas", s);
    public static Font Small() => new("Segoe UI", 8f, FontStyle.Bold);

    public static Label Eyebrow(string t) => new()
    {
        Text = t.ToUpperInvariant(), AutoSize = true, ForeColor = Dim,
        Font = Small(), Margin = new Padding(0, 0, 0, 10),
    };

    public static Button Btn(string text)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat, Font = Ui(),
            Padding = new Padding(16, 9, 16, 9), Margin = new Padding(0),
            BackColor = Card, ForeColor = Text, Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x2C, 0x2C, 0x31);
        return b;
    }
}

class Card : Panel
{
    public Card()
    {
        BackColor = Ink.Card;
        Padding = new Padding(20, 16, 20, 18);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Ink.Line);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

class Slider : Control
{
    public double Value;
    public Func<double, Color> Ramp;
    public event EventHandler Changed;
    public event EventHandler Committed;
    bool _drag;

    public Slider()
    {
        Height = 22;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Ramp == null || Width < 6) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int h = 6, y = (Height - h) / 2, w = Width - 2;
        for (int x = 0; x < w; x++)
        {
            using var p = new Pen(Ramp((double)x / Math.Max(w - 1, 1)));
            g.DrawLine(p, x + 1, y, x + 1, y + h);
        }

        int kx = 1 + (int)(Value * (w - 1));
        g.FillEllipse(Brushes.White, kx - 7, Height / 2 - 7, 14, 14);
        using var fill = new SolidBrush(Ramp(Value));
        g.FillEllipse(fill, kx - 5, Height / 2 - 5, 10, 10);
    }

    void Set(int x)
    {
        Value = Math.Clamp((x - 1) / (double)Math.Max(Width - 3, 1), 0, 1);
        Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    // capture, or the drag stops the moment you slip off the control
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _drag = true; Capture = true; Set(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag) Set(e.X);
    }

    // Changed fires the whole way through the drag, Committed only on release.
    // the form uses that to skip the expensive redraw until you let go.
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_drag) return;
        _drag = false; Capture = false;
        Committed?.Invoke(this, EventArgs.Empty);
    }
}

// top strip is the file as loaded and never changes. bottom is the working copy
// plus whatever's pending. selection is keyed to the original colours, not the
// working ones, so it can't go stale after an apply.
class RampStrip : Control
{
    public List<uint> Colours = new();
    public HashSet<uint> Selected = new();
    public Func<uint, uint> Preview;
    public event EventHandler SelectionChanged;
    public event EventHandler SelectionCommitted;

    const int Lab = 44, RowH = 40, Gap = 12, PadTop = 4;
    int _anchor = -1;
    bool _drag;

    public RampStrip()
    {
        Height = PadTop + RowH * 2 + Gap + 14;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    int Span => Math.Max(Width - Lab, 1);

    int IndexAt(int x)
    {
        if (Colours.Count == 0) return -1;
        int i = (int)((x - Lab) / (double)Span * Colours.Count);
        return Math.Clamp(i, 0, Colours.Count - 1);
    }

    int XOf(int i) => Lab + (int)(i / (double)Colours.Count * Span);

    void SelectRange(int a, int b)
    {
        Selected.Clear();
        if (a >= 0 && b >= 0)
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) Selected.Add(Colours[i]);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(IEnumerable<uint> cols)
    {
        Selected = new HashSet<uint>(cols);
        Invalidate();
    }

    public bool Selectable = true;

    int AfterTop => PadTop + RowH + Gap;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Selectable || Colours.Count == 0) return;
        // clicking the source strip does nothing rather than clearing, a stray
        // click up there used to wipe the selection and the next apply went global
        if (e.Y < AfterTop) return;
        if (e.X < Lab || e.Y > AfterTop + RowH) { SelectRange(-1, -1); return; }
        _drag = true; Capture = true; _anchor = IndexAt(e.X); SelectRange(_anchor, _anchor);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag && Selectable) SelectRange(_anchor, IndexAt(e.X));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_drag) return;
        _drag = false; Capture = false;
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    static void Strip(Graphics g, Rectangle r, List<uint> cols)
    {
        if (cols.Count == 0 || r.Width < 2 || r.Height < 2) return;
        if (cols.Count == 1)
        {
            using var solid = new SolidBrush(Efl.ToColor(cols[0]));
            g.FillRectangle(solid, r);
            return;
        }
        var blend = new ColorBlend(cols.Count)
        {
            Colors = cols.Select(Efl.ToColor).ToArray(),
            Positions = Enumerable.Range(0, cols.Count)
                                  .Select(i => i / (float)(cols.Count - 1)).ToArray(),
        };
        using var br = new LinearGradientBrush(r, Color.Black, Color.White, 0f)
        { InterpolationColors = blend };
        g.FillRectangle(br, r);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Ink.Card)) g.FillRectangle(bg, ClientRectangle);

        if (Colours.Count == 0)
        {
            TextRenderer.DrawText(g, "Open a file to see its colours.", Ink.Ui(),
                                  new Point(2, 8), Ink.Dim);
            return;
        }

        bool narrowed = Selected.Count > 0;
        var after = Preview == null ? Colours : Colours.Select(c =>
            (!narrowed || Selected.Contains(c)) ? Preview(c) : c).ToList();

        var top = new Rectangle(Lab, PadTop, Span, RowH);
        var bot = new Rectangle(Lab, PadTop + RowH + Gap, Span, RowH);
        TextRenderer.DrawText(g, "now", Ink.Mono(8.5f), new Point(0, PadTop + RowH / 2 - 8), Ink.Dim);
        TextRenderer.DrawText(g, "after", Ink.Mono(8.5f), new Point(0, bot.Top + RowH / 2 - 8), Ink.Dim);
        Strip(g, top, Colours);
        Strip(g, bot, after);

        if (!narrowed) return;

        int lo = int.MaxValue, hi = -1;
        for (int i = 0; i < Colours.Count; i++)
            if (Selected.Contains(Colours[i])) { lo = Math.Min(lo, i); hi = Math.Max(hi, i); }
        if (hi < 0) return;

        int x1 = XOf(lo), x2 = XOf(hi + 1);
        using (var veil = new SolidBrush(Color.FromArgb(150, Ink.Card)))
        {
            g.FillRectangle(veil, Lab, bot.Top, x1 - Lab, RowH);
            g.FillRectangle(veil, x2, bot.Top, Lab + Span - x2, RowH);
        }
        using (var pen = new Pen(Ink.Text, 2f))
        {
            g.DrawLine(pen, x1, PadTop - 2, x2, PadTop - 2);
            g.DrawLine(pen, x1, bot.Bottom + 5, x2, bot.Bottom + 5);
        }
    }
}


class MainForm : Form
{
    Efl _orig;
    Efl _work;
    string _path;
    List<Site> _sites = new();
    bool _parts;
    SortBy _sort = SortBy.Brightness;
    bool _syncing;
    readonly Stack<byte[]> _undo = new();

    readonly Label _file = new();
    readonly Label _status = new(), _hex = new(), _tally = new();
    readonly Slider _hue = new(), _sat = new(), _val = new(), _con = new();
    readonly Panel _swatch = new();
    readonly RampStrip _ramp = new();
    readonly DataGridView _grid = new();
    readonly Button _saveBtn = Ink.Btn("Save recoloured copy");
    readonly Button _clearBtn = Ink.Btn("Clear selection");
    readonly Button _tabAll = Ink.Btn("Whole effect"), _tabParts = Ink.Btn("Specific parts");
    readonly Button _applyBtn = Ink.Btn("Apply");
    readonly Button _undoBtn = Ink.Btn("Undo");
    readonly Button _resetBtn = Ink.Btn("Reset");
    readonly Button[] _sortBtns = { Ink.Btn("Light to dark"), Ink.Btn("RGB") };
    readonly Label _selInfo = new();
    readonly CheckBox _keepWhite = new();
    readonly Button _tintBtn = Ink.Btn("Tint"), _shiftBtn = Ink.Btn("Shift");
    Mode? _mode;
    double _dominant;
    double _pivot = 0.5;

    double Hue => _hue.Value * 360;
    double Sat => _sat.Value;
    double Val => _val.Value;
    double Con => Math.Pow(2, (_con.Value - 0.5) * 3);
    Color Picked => Efl.HsvToColor(Hue, Sat, Val);

    Efl.Recolour Settings => new()
    {
        Hue = Hue, Sat = Sat, Val = Val, Contrast = Con, Pivot = _pivot,
        Mode = _mode ?? Mode.Tint,
        KeepWhite = _keepWhite.Checked, DominantHue = _dominant,
    };

    public MainForm()
    {
        Text = "EFL Recolor";
        using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
            if (st != null) Icon = new Icon(st);
        DoubleBuffered = true;
        BackColor = Ink.Bg;
        ForeColor = Ink.Text;
        Font = Ink.Ui();
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(940, 680);
        Size = new Size(1020, 900);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (s, e) =>
        {
            var f = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (f.Length > 0) Open(f[0]);
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(22, 22, 22, 0), BackColor = Ink.Bg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(FileBar(), 0, 0);
        root.Controls.Add(Tabs(), 0, 1);
        root.Controls.Add(RampCard(), 0, 2);
        root.Controls.Add(Split(), 0, 3);

        // docked to the form, not a row in the table. as a table row it got
        // clipped off the bottom whenever the content ran taller than the window.
        var footer = new Panel
        {
            Dock = DockStyle.Bottom, BackColor = Ink.Bg, AutoSize = true,
            Padding = new Padding(22, 8, 22, 18),
        };
        footer.Controls.Add(Footer());

        Controls.Add(root);
        Controls.Add(footer);

        _hue.Value = 0.03;
        _sat.Value = 0.85;
        _val.Value = 1.0;
        _con.Value = 0.5;
        Redraw();
    }

    Control FileBar()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            AutoSize = true, Margin = new Padding(0, 0, 0, 18), BackColor = Ink.Bg,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _file.Text = "Drag an .efl file here or click Open to select one.";
        _file.ForeColor = Ink.Dim;
        _file.Font = Ink.Mono(9.5f);
        _file.AutoEllipsis = true;
        _file.Dock = DockStyle.Fill;
        _file.TextAlign = ContentAlignment.MiddleLeft;

        var open = Ink.Btn("Open .efl");
        open.Click += (s, e) =>
        {
            using var d = new OpenFileDialog { Filter = "MT Framework effect (*.efl)|*.efl|All files|*.*" };
            if (d.ShowDialog() == DialogResult.OK) Open(d.FileName);
        };

        row.Controls.Add(_file, 0, 0);
        row.Controls.Add(open, 1, 0);
        return row;
    }

    Control Tabs()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoSize = true, BackColor = Ink.Bg,
            Margin = new Padding(0, 0, 0, 18),
        };
        _tabAll.Margin = new Padding(0, 0, 6, 0);
        _tabAll.Click += (s, e) =>
        {
            _parts = false;
            _ramp.SetSelection(Array.Empty<uint>());
            _grid.ClearSelection();
            Redraw();
        };
        _tabParts.Margin = new Padding(0);
        _tabParts.Click += (s, e) => { _parts = true; Redraw(); };
        row.Controls.Add(_tabAll);
        row.Controls.Add(_tabParts);
        return row;
    }

    Control RampCard()
    {
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };

        var head = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8), BackColor = Ink.Card };
        head.Controls.Add(new Label
        {
            Text = "THE COLOUR RAMP", AutoSize = true, ForeColor = Ink.Dim,
            Font = Ink.Small(), Margin = new Padding(0, 9, 18, 0),
        });
        head.Controls.Add(new Label
        {
            Text = "sort", AutoSize = true, ForeColor = Ink.Dim, Margin = new Padding(0, 9, 8, 0),
        });
        for (int i = 0; i < _sortBtns.Length; i++)
        {
            int n = i;
            _sortBtns[i].Margin = new Padding(0, 0, 6, 0);
            _sortBtns[i].Padding = new Padding(10, 6, 10, 6);
            _sortBtns[i].Click += (s, e) => { _sort = (SortBy)n; Redraw(); };
            head.Controls.Add(_sortBtns[i]);
        }
        stack.Controls.Add(head);

        _ramp.Margin = new Padding(0);
        _ramp.Preview = c => Settings.Apply(c);
        _ramp.SelectionChanged += (s, e) => Redraw(false);
        _ramp.SelectionCommitted += (s, e) => Redraw();
        stack.Controls.Add(_ramp);

        _selInfo.AutoSize = true;
        _selInfo.ForeColor = Ink.Dim;
        _selInfo.Margin = new Padding(0, 6, 0, 0);
        stack.Controls.Add(_selInfo);

        card.Controls.Add(stack);
        card.Resize += (s, e) =>
        {
            int w = card.ClientSize.Width - card.Padding.Horizontal;
            if (w > 0) _ramp.Width = w;
        };
        return card;
    }

    Control Split()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Ink.Bg, Margin = new Padding(0, 0, 0, 18),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(ColourCard(), 0, 0);
        row.Controls.Add(ListCard(), 1, 0);
        return row;
    }

    Control ColourCard()
    {
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };
        stack.Controls.Add(Ink.Eyebrow("colour"));

        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };

        var sliders = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0, 0, 20, 0),
        };
        Label Cap(string t) => new()
        {
            Text = t, AutoSize = true, ForeColor = Ink.Dim, Margin = new Padding(0, 0, 0, 3),
        };

        sliders.Controls.Add(Cap("Hue"));
        _hue.Width = 240;
        _hue.Ramp = t => Efl.HsvToColor(t * 360, 1, 1);
        _hue.Margin = new Padding(0, 0, 0, 12);
        _hue.Changed += (s, e) => Redraw(false);
        _hue.Committed += (s, e) => Redraw();
        sliders.Controls.Add(_hue);

        sliders.Controls.Add(Cap("Saturation"));
        _sat.Width = 240;

        _sat.Ramp = t => Efl.HsvToColor(Hue, t, Val);
        _sat.Margin = new Padding(0, 0, 0, 12);
        _sat.Changed += (s, e) => Redraw(false);
        _sat.Committed += (s, e) => Redraw();
        sliders.Controls.Add(_sat);

        sliders.Controls.Add(Cap("Brightness"));
        _val.Width = 240;
        _val.Ramp = t => Efl.HsvToColor(Hue, Sat, t);
        _val.Margin = new Padding(0, 0, 0, 12);
        _val.Changed += (s, e) => Redraw(false);
        _val.Committed += (s, e) => Redraw();
        sliders.Controls.Add(_val);

        sliders.Controls.Add(Cap("Contrast"));
        _con.Width = 240;
        _con.Ramp = t => Efl.HsvToColor(Hue, Sat,
            Math.Clamp(((0.65 - _pivot) * Math.Pow(2, (t - 0.5) * 3) + _pivot) * Val, 0, 1));
        _con.Margin = new Padding(0);
        _con.Changed += (s, e) => Redraw(false);
        _con.Committed += (s, e) => Redraw();
        sliders.Controls.Add(_con);

        body.Controls.Add(sliders);

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };
        _swatch.Size = new Size(96, 54);
        _swatch.Margin = new Padding(0, 16, 0, 8);
        _swatch.Paint += (s, e) =>
        {
            using var b = new SolidBrush(Picked);
            e.Graphics.FillRectangle(b, _swatch.ClientRectangle);
        };
        right.Controls.Add(_swatch);

        var pick = Ink.Btn("Pick");
        pick.Margin = new Padding(0);
        pick.Click += (s, e) =>
        {
            using var d = new ColorDialog
            {
                Color = Picked, FullOpen = true, AnyColor = true,
                CustomColors = new[] { ColorToOle(Picked) },
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;

            Efl.RgbToHsv(d.Color.R, d.Color.G, d.Color.B,
                         out double h, out double s2, out double v2);
            _hue.Value = h / 360;
            _sat.Value = s2;
            _val.Value = v2;
            Redraw();
        };
        right.Controls.Add(pick);
        body.Controls.Add(right);

        stack.Controls.Add(body);

        var modeRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0, 14, 0, 0),
        };
        modeRow.Controls.Add(new Label
        {
            Text = "Mode", AutoSize = true, ForeColor = Ink.Dim,
            Margin = new Padding(0, 9, 10, 0),
        });
        _tintBtn.Margin = new Padding(0, 0, 6, 0);
        _tintBtn.Click += (s, e) => { _mode = Mode.Tint; Redraw(); };
        _shiftBtn.Margin = new Padding(0, 0, 16, 0);
        _shiftBtn.Click += (s, e) => { _mode = Mode.Shift; Redraw(); };
        modeRow.Controls.Add(_tintBtn);
        modeRow.Controls.Add(_shiftBtn);

        _keepWhite.Text = "Keep white highlights";
        _keepWhite.AutoSize = true;
        _keepWhite.ForeColor = Ink.Dim;
        _keepWhite.Margin = new Padding(0, 8, 0, 0);
        _keepWhite.CheckedChanged += (s, e) => Redraw();
        modeRow.Controls.Add(_keepWhite);
        stack.Controls.Add(modeRow);

        _hex.AutoSize = true;
        _hex.Font = Ink.Mono(9.5f);
        _hex.ForeColor = Ink.Dim;
        _hex.Margin = new Padding(0, 12, 0, 0);
        stack.Controls.Add(_hex);

        card.Controls.Add(stack);
        return card;
    }

    Control ListCard()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0) };

        var head = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1,
            AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0, 0, 0, 12),
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tally.AutoSize = false;
        _tally.Dock = DockStyle.Fill;
        _tally.ForeColor = Ink.Dim;
        _tally.TextAlign = ContentAlignment.MiddleLeft;
        head.Controls.Add(_tally, 0, 0);
        head.Controls.Add(new Label
        {
            Text = "COLOURS", AutoSize = true, ForeColor = Ink.Dim,
            Font = Ink.Small(), Margin = new Padding(0, 6, 0, 0),
        }, 1, 0);

        StyleGrid();
        _grid.Dock = DockStyle.Fill;

        card.Controls.Add(_grid);
        card.Controls.Add(head);
        return card;
    }

    // DataGridView doesn't expose DoubleBuffered, hence the reflection
    static void Buffer(Control c)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance |
                                           System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(c, true, null);
    }

    void StyleGrid()
    {
        Buffer(_grid);
        _grid.BackgroundColor = Ink.Card;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Ink.Line;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Ink.Card;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink.Dim;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ink.Card;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink.Dim;
        _grid.ColumnHeadersDefaultCellStyle.Font = Ink.Small();
        _grid.ColumnHeadersHeight = 32;
        _grid.DefaultCellStyle.BackColor = Ink.Card;
        _grid.DefaultCellStyle.ForeColor = Ink.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0x22, 0x22, 0x26);
        _grid.DefaultCellStyle.SelectionForeColor = Ink.Text;
        _grid.DefaultCellStyle.Font = Ink.Mono();
        _grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToResizeColumns = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowTemplate.Height = 26;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ScrollBars = ScrollBars.Vertical;
        _grid.MultiSelect = true;
        _grid.SelectionChanged += (s, e) =>
        {
            // the rebuild below fires this too, guard or it fights the user
            if (_syncing || _work == null) return;
            var picked = new HashSet<uint>();
            foreach (DataGridViewRow r in _grid.SelectedRows)
                if (r.Index >= 0 && r.Index < _sites.Count)
                    picked.Add(_orig.Colour(_sites[r.Index].Offset));
            // an empty event during a rebuild would otherwise wipe the ramp
            if (picked.Count == 0) return;
            _ramp.SetSelection(picked);
            Redraw();
        };
        _grid.CellDoubleClick += (s, e) => EditRows();

        void Col(string h, int weight) => _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = h, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        // fixed width, otherwise Fill mode stretches the swatches to 140px
        void Chip() => _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "", Width = 26, MinimumWidth = 26,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        Col("Where", 26);
        Col("What", 24);
        Chip();
        Col("Now", 25);
        Chip();
        Col("After", 25);
    }

    Control Footer()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            AutoSize = true, BackColor = Ink.Bg,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Ink.Dim;
        _status.TextAlign = ContentAlignment.MiddleLeft;

        _saveBtn.Enabled = false;
        _saveBtn.Click += (s, e) => Save();

        var acts = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoSize = true, BackColor = Ink.Bg, Margin = new Padding(0),
        };
        _clearBtn.Margin = new Padding(0, 0, 14, 0);
        _clearBtn.Click += (s, e) =>
        {
            _ramp.SetSelection(Array.Empty<uint>());
            _grid.ClearSelection();
            Redraw();
        };
        acts.Controls.Add(_clearBtn);

        _applyBtn.Margin = new Padding(0, 0, 8, 0);
        _applyBtn.Click += (s, e) => Apply();
        _undoBtn.Margin = new Padding(0, 0, 8, 0);
        _undoBtn.Click += (s, e) => Undo();
        _resetBtn.Margin = new Padding(0, 0, 14, 0);
        _resetBtn.Click += (s, e) => Reset();
        acts.Controls.Add(_applyBtn);
        acts.Controls.Add(_undoBtn);
        acts.Controls.Add(_resetBtn);

        row.Controls.Add(_status, 0, 0);
        row.Controls.Add(acts, 1, 0);
        row.Controls.Add(_saveBtn, 2, 0);
        return row;
    }

    static int ColorToOle(Color c) => c.R | (c.G << 8) | (c.B << 16);

    void Open(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            _orig = new Efl(bytes);
            _work = new Efl((byte[])bytes.Clone());
            _undo.Clear();
            _ramp.Selected.Clear();
            _path = path;
            _sites = _work.ColourSites();
            _dominant = _work.DominantHue();

            // no mode until you pick one, otherwise the after strip previews a
            // transform nobody asked for the second a file opens
            _mode = null;
            _parts = false;
            _keepWhite.Checked = _work.GreyFraction() < 0.80;
            _file.Text = path;
            _file.ForeColor = Ink.Text;
            Say($"{_work.NodeCount} nodes, {_work.RenderBlocks().Count} render blocks.", false);
        }
        catch (Exception ex)
        {
            _orig = _work = null; _sites.Clear();
            _file.Text = path; _file.ForeColor = Ink.Dim;
            Say(ex.Message, true);
        }
        Redraw();
    }

    // specific parts with nothing picked hits nothing. it used to fall through
    // to every site, which is how a selection ended up recolouring the lot.
    List<Site> Target()
    {
        if (!_parts) return _sites;
        if (_ramp.Selected.Count == 0) return new List<Site>();
        return _sites.Where(s => _ramp.Selected.Contains(_orig.Colour(s.Offset))).ToList();
    }

    // absolute edit
    void EditRows()
    {
        if (_work == null || _grid.SelectedRows.Count == 0) return;

        var rows = new List<int>();
        foreach (DataGridViewRow r in _grid.SelectedRows)
            if (r.Index >= 0 && r.Index < _sites.Count) rows.Add(r.Index);
        if (rows.Count == 0) return;

        uint seed = _work.Colour(_sites[rows[0]].Offset);
        using var d = new ColorDialog
        {
            Color = Efl.ToColor(seed), FullOpen = true, AnyColor = true,
            CustomColors = new[] { Efl.ToColor(seed).R | (Efl.ToColor(seed).G << 8) | (Efl.ToColor(seed).B << 16) },
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        _undo.Push(_work.Snapshot());
        foreach (int i in rows)
        {
            uint old = _work.Colour(_sites[i].Offset);
            uint a = (old >> 24) & 255;
            _work.SetU32(_sites[i].Offset,
                (a << 24) | ((uint)d.Color.B << 16) | ((uint)d.Color.G << 8) | d.Color.R);
        }
        _dominant = _work.DominantHue();
        _pivot = _work.MeanValue();
        Say(rows.Count == 1
            ? $"Set 0x{_sites[rows[0]].Offset:X6} to #{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}. Alpha kept."
            : $"Set {rows.Count} sites to #{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}. Alpha kept.", false);
        Redraw();
    }

    void Apply()
    {
        if (_work == null || !_mode.HasValue) return;
        var hit = Target();
        if (hit.Count == 0) return;
        _undo.Push(_work.Snapshot());
        int n = _work.ApplyTo(hit, Settings);
        _dominant = _work.DominantHue();
        _pivot = _work.MeanValue();
        Say(n == 0 ? "Nothing changed." : $"Recoloured {n} sites.", n == 0);
        Redraw();
    }

    void Undo()
    {
        if (_work == null || _undo.Count == 0) return;
        _work.Restore(_undo.Pop());
        _ramp.SetSelection(Array.Empty<uint>());
        _dominant = _work.DominantHue();
        Say("Undone.", false);
        Redraw();
    }

    void Reset()
    {
        if (_work == null) return;
        _work.Restore(_orig.Snapshot());
        _undo.Clear();
        _ramp.SetSelection(Array.Empty<uint>());
        _dominant = _work.DominantHue();
        Say("Back to the original.", false);
        Redraw();
    }

    void Say(string msg, bool bad)
    {
        _status.Text = msg;
        _status.ForeColor = bad ? Ink.Bad : Ink.Dim;
    }

    // Dragging a slider fires this dozens of times a second and clearing 260 rows each time is what made it slow.
    void Redraw() => Redraw(true);

    void Redraw(bool full)
    {
        _swatch.Invalidate();
        _sat.Invalidate();
        _hue.Invalidate();
        _hex.Text = !_mode.HasValue
            ? "pick Tint or Shift to start"
            : $"#{Picked.R:X2}{Picked.G:X2}{Picked.B:X2}"
              + (_mode == Mode.Shift ? $"   shifting from {_dominant:0} deg" : "");

        if (_work == null)
        {
            _tally.Text = "No file open.";
            _ramp.Colours.Clear(); _ramp.Invalidate();
            _selInfo.Text = "";
            _grid.Rows.Clear(); _saveBtn.Enabled = false;
            _applyBtn.Enabled = _undoBtn.Enabled = _resetBtn.Enabled = false;
            return;
        }

        bool ready = _mode.HasValue;
        var rc = Settings;

        _ramp.Colours = _orig.Palette(_sort);

        // original colour -> what that site currently is
        var current = new Dictionary<uint, uint>();
        foreach (var st in _sites)
        {
            uint o = _orig.Colour(st.Offset);
            if (!current.ContainsKey(o)) current[o] = _work.Colour(st.Offset);
        }

        var target = Target();
        var hitCols = new HashSet<uint>(target.Select(st => _orig.Colour(st.Offset)));

        _ramp.Preview = c =>
        {
            uint now = current.TryGetValue(c, out uint w) ? w : c;
            return ready && hitCols.Contains(c) ? rc.Apply(now) : now;
        };
        _ramp.Invalidate();
        _selInfo.Text = !_parts
            ? $"{_ramp.Colours.Count} distinct colours \u2022 Apply changes all {_sites.Count} sites"
            : _ramp.Selected.Count == 0
                ? $"{_ramp.Colours.Count} distinct colours \u2022 drag across the lower strip to pick some"
                : $"{_ramp.Selected.Count} of {_ramp.Colours.Count} colours picked \u2022 Apply hits {target.Count} sites";
        if (_parts && _ramp.Selected.Count == 0) _selInfo.ForeColor = Ink.Dim;

        _tabAll.BackColor   = !_parts ? Color.FromArgb(0x30, 0x30, 0x36) : Ink.Card;
        _tabParts.BackColor =  _parts ? Color.FromArgb(0x30, 0x30, 0x36) : Ink.Card;
        _tabAll.ForeColor   = !_parts ? Ink.Text : Ink.Dim;
        _tabParts.ForeColor =  _parts ? Ink.Text : Ink.Dim;

        _ramp.Selectable = _parts;
        _ramp.Cursor = _parts ? Cursors.Hand : Cursors.Default;
        _clearBtn.Visible = _parts;
        _clearBtn.Enabled = _ramp.Selected.Count > 0;

        _applyBtn.Text = _ramp.Selected.Count > 0 ? "Apply to selection" : "Apply to all";
        _applyBtn.Enabled = ready && target.Count > 0;
        _undoBtn.Enabled = _undo.Count > 0;
        _resetBtn.Enabled = _undo.Count > 0;

        int willChange = target.Count(s => rc.Apply(_work.Colour(s.Offset)) != _work.Colour(s.Offset));
        int mat = _sites.Count(s => s.Kind is SiteKind.Primary or SiteKind.Primary2);
        int sec = _sites.Count(s => s.Kind is SiteKind.Secondary or SiteKind.Secondary2);
        int anim = _sites.Count(s => s.Kind == SiteKind.TrackKey);
        _tally.Text = $"{willChange} of {_sites.Count} sites would change   \u2022   "
                    + $"{mat} material, {sec} secondary, {anim} animated   \u2022   double click a row to set it directly";

        if (!full) { _saveBtn.Enabled = true; return; }

        _syncing = true;
        var keep = new List<int>();
        foreach (DataGridViewRow r in _grid.SelectedRows) keep.Add(r.Index);
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var s in _sites)
        {
            uint before = _work.Colour(s.Offset);
            uint after = ready && target.Contains(s) ? rc.Apply(before) : before;
            int i = _grid.Rows.Add($"0x{s.Offset:X6}", s.KindLabel, "",
                                   $"{before:x8}", "", $"{after:x8}");
            var cb = Efl.ToColor(before);
            var ca = Efl.ToColor(after);
            _grid.Rows[i].Cells[2].Style.BackColor = cb;
            _grid.Rows[i].Cells[2].Style.SelectionBackColor = cb;
            _grid.Rows[i].Cells[4].Style.BackColor = ca;
            _grid.Rows[i].Cells[4].Style.SelectionBackColor = ca;
        }
        _grid.ResumeLayout();
        _grid.ClearSelection();
        foreach (int i in keep)
            if (i >= 0 && i < _grid.Rows.Count) _grid.Rows[i].Selected = true;
        _syncing = false;
        

        _saveBtn.Enabled = true;
    }

    void Save()
    {
        if (_work == null) return;
        using var d = new SaveFileDialog
        {
            Filter = "MT Framework effect (*.efl)|*.efl",
            FileName = Path.GetFileNameWithoutExtension(_path) + "_recolour.efl",
        };
        if (d.ShowDialog() != DialogResult.OK) return;

        try
        {
            string problem = _work.Verify(_orig);
            if (problem != null) { Say("Not saved - " + problem + ".", true); return; }

            File.WriteAllBytes(d.FileName, _work.Data);
            int diff = 0;
            for (int i = 0; i < _work.Data.Length; i += 4)
                if (BitConverter.ToUInt32(_work.Data, i) != BitConverter.ToUInt32(_orig.Data, i)) diff++;
            Say($"Saved {Path.GetFileName(d.FileName)}. {diff} values changed.", false);
        }
        catch (Exception ex)
        {
            Say("Not saved - " + ex.Message, true);
        }
    }
}