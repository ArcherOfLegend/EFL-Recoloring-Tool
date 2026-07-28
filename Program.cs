// Program.cs - the window.
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

// ---------------------------------------------------------------- theme

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

    /// small dim uppercase label that heads each area
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

/// padded panel with a hairline border
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

// ---------------------------------------------------------------- slider

class Slider : Control
{
    public double Value;
    public Func<double, Color> Ramp;
    public event EventHandler Changed;
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
    protected override void OnMouseDown(MouseEventArgs e) { _drag = true; Set(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) Set(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; }
}

// ---------------------------------------------------------------- the hero
class TrailPreview : Control
{
    public List<uint> Before = new();
    public List<uint> After = new();

    public TrailPreview()
    {
        Height = 116;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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
        using (var b = new SolidBrush(Ink.Card)) g.FillRectangle(b, ClientRectangle);

        if (Before.Count == 0)
        {
            TextRenderer.DrawText(g, "Open a smear to see it here.", Ink.Ui(),
                                  new Point(2, 8), Ink.Dim);
            return;
        }

        int lab = 44, h = 40, gap = 12, top = 4;
        int x = lab, w = Width - lab;
        TextRenderer.DrawText(g, "now", Ink.Mono(8.5f), new Point(0, top + h / 2 - 8), Ink.Dim);
        TextRenderer.DrawText(g, "after", Ink.Mono(8.5f), new Point(0, top + h + gap + h / 2 - 8), Ink.Dim);
        Strip(g, new Rectangle(x, top, w, h), Before);
        Strip(g, new Rectangle(x, top + h + gap, w, h), After);
    }
}

// ---------------------------------------------------------------- window

class MainForm : Form
{
    Efl _efl;
    string _path;
    List<Site> _sites = new();
    bool _showAll;

    readonly Label _file = new(), _verdict = new(), _score = new();
    readonly Label _status = new(), _hex = new(), _tally = new();
    readonly FlowLayoutPanel _checks = new();
    readonly Slider _hue = new(), _sat = new(), _val = new(), _con = new();
    double _pivot = 0.5;
    readonly Panel _swatch = new();
    readonly TrailPreview _trail = new();
    readonly DataGridView _grid = new();
    readonly Button _saveBtn = Ink.Btn("Save recoloured copy");
    readonly Button _toggle = Ink.Btn("Show every colour");
    readonly CheckBox _force = new(), _keepWhite = new();
    readonly Button _tintBtn = Ink.Btn("Tint"), _shiftBtn = Ink.Btn("Shift");
    Mode _mode = Mode.Tint;
    double _dominant;

    double Hue => _hue.Value * 360;
    double Sat => _sat.Value;              // full 0..1, so white is reachable
    double Val => _val.Value;              // scales the file's own brightness
    double Con => Math.Pow(2, (_con.Value - 0.5) * 3); // log scale so the middle of the slider is 1.0 and both ends are usable
    Color Picked => Efl.HsvToColor(Hue, Sat, Val);

    Efl.Recolour Settings => new()
    {
        Hue = Hue, Sat = Sat, Val = Val, Mode = _mode,
        Contrast = Con, Pivot = _pivot,
        KeepWhite = _keepWhite.Checked, DominantHue = _dominant,
    };

    public MainForm()
    {
        Text = "EFL Recolor";
        BackColor = Ink.Bg;
        ForeColor = Ink.Text;
        Font = Ink.Ui();
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 720);
        Size = new Size(1000, 840);
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
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(22), BackColor = Ink.Bg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(FileBar(), 0, 0);
        root.Controls.Add(TrailCard(), 0, 1);
        root.Controls.Add(TwoColumns(), 0, 2);
        root.Controls.Add(ListCard(), 0, 3);
        root.Controls.Add(Footer(), 0, 4);
        Controls.Add(root);

        _hue.Value = 0.03;
        _sat.Value = 0.85;
        _val.Value = 1.0;
        _con.Value = 0.5;
        Redraw();
    }

    // ---- sections ----------------------------------------------------------

    Control FileBar()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            AutoSize = true, Margin = new Padding(0, 0, 0, 18), BackColor = Ink.Bg,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _file.Text = "Drag an efl into here or open one in the file manager.";
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

    Control TrailCard()
    {
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };
        stack.Controls.Add(Ink.Eyebrow("EFL Colour Ramp"));
        _trail.Margin = new Padding(0);
        stack.Controls.Add(_trail);
        card.Controls.Add(stack);
        card.Resize += (s, e) =>
        {
            int w = card.ClientSize.Width - card.Padding.Horizontal;
            if (w > 0) _trail.Width = w;
        };
        return card;
    }

    Control TwoColumns()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            AutoSize = true, Margin = new Padding(0, 0, 0, 18), BackColor = Ink.Bg,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(CheckCard(), 0, 0);
        row.Controls.Add(ColourCard(), 1, 0);
        return row;
    }

    Control CheckCard()
    {
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 9, 0) };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = true, BackColor = Ink.Card, Margin = new Padding(0),
        };

        var head = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), BackColor = Ink.Card };
        head.Controls.Add(Ink.Eyebrow("does this look like a smear"));
        _score.AutoSize = true;
        _score.Font = Ink.Small();
        _score.Margin = new Padding(10, 0, 0, 10);
        head.Controls.Add(_score);
        stack.Controls.Add(head);

        _checks.FlowDirection = FlowDirection.TopDown;
        _checks.WrapContents = false;
        _checks.AutoSize = true;
        _checks.BackColor = Ink.Card;
        _checks.Margin = new Padding(0);
        stack.Controls.Add(_checks);

        _verdict.AutoSize = true;
        _verdict.MaximumSize = new Size(400, 0);
        _verdict.Margin = new Padding(0, 12, 0, 0);
        stack.Controls.Add(_verdict);

        card.Controls.Add(stack);
        return card;
    }

    Control ColourCard()
    {
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(9, 0, 0, 0) };
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
        _hue.Changed += (s, e) => Redraw();
        sliders.Controls.Add(_hue);

        sliders.Controls.Add(Cap("Saturation"));
        _sat.Width = 240;
        _sat.Ramp = t => Efl.HsvToColor(Hue, t, Val);
        _sat.Margin = new Padding(0, 0, 0, 12);
        _sat.Changed += (s, e) => Redraw();
        sliders.Controls.Add(_sat);

        sliders.Controls.Add(Cap("Brightness"));
        _val.Width = 240;
        _val.Ramp = t => Efl.HsvToColor(Hue, Sat, t);
        _val.Margin = new Padding(0, 0, 0, 12);
        _val.Changed += (s, e) => Redraw();
        sliders.Controls.Add(_val);

        sliders.Controls.Add(Cap("Contrast"));
        _con.Width = 240;
        _con.Ramp = t => Efl.HsvToColor(Hue, Sat,
            Math.Clamp(((0.65 - _pivot) * Math.Pow(2, (t - 0.5) * 3) + _pivot) * Val, 0, 1));
        _con.Margin = new Padding(0);
        _con.Changed += (s, e) => Redraw();
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
        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 18) };

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
        _toggle.Click += (s, e) => { _showAll = !_showAll; Redraw(); };
        head.Controls.Add(_toggle, 1, 0);

        StyleGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.Visible = false;

        card.Controls.Add(_grid);
        card.Controls.Add(head);
        return card;
    }

    void StyleGrid()
    {
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

        void Col(string h, int weight) => _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = h, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable,
        });

        Col("Where", 20);
        Col("What", 22);
        Col("", 7);
        Col("Now", 22);
        Col("", 7);
        Col("After", 22);
    }

    Control Footer()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            AutoSize = true, BackColor = Ink.Bg,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _force.Text = "Recolour it anyway";
        _force.AutoSize = true;
        _force.ForeColor = Ink.Dim;
        _force.Margin = new Padding(0, 10, 18, 0);
        _force.CheckedChanged += (s, e) => Redraw();

        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Ink.Dim;
        _status.TextAlign = ContentAlignment.MiddleLeft;

        _saveBtn.Enabled = false;
        _saveBtn.Click += (s, e) => Save();

        row.Controls.Add(_force, 0, 0);
        row.Controls.Add(_status, 1, 0);
        row.Controls.Add(_saveBtn, 2, 0);
        return row;
    }

    // ---- behaviour ---------------------------------------------------------

    /// ColorDialog wants COLORREF (0x00BBGGRR), not ARGB
    static int ColorToOle(Color c) => c.R | (c.G << 8) | (c.B << 16);

    void Open(string path)
    {
        try
        {
            _efl = new Efl(File.ReadAllBytes(path));
            _path = path;
            _sites = _efl.ColourSites();
            _dominant = _efl.DominantHue();
            _pivot = _efl.MeanValue();
            // a mostly grey smear has no palette to preserve, so Tint. one that
            // already carries colour does, so Shift.
            _mode = _efl.GreyFraction() >= 0.80 ? Mode.Tint : Mode.Shift;
            _keepWhite.Checked = _efl.GreyFraction() < 0.80;
            _file.Text = path;
            _file.ForeColor = Ink.Text;
            Say($"{_efl.NodeCount} nodes, {_efl.RenderBlocks().Count} render blocks.", false);
        }
        catch (Exception ex)
        {
            _efl = null; _sites.Clear();
            _file.Text = path; _file.ForeColor = Ink.Dim;
            Say(ex.Message, true);
        }
        Redraw();
    }

    void Say(string msg, bool bad)
    {
        _status.Text = msg;
        _status.ForeColor = bad ? Ink.Bad : Ink.Dim;
    }

    void Redraw()
    {
        _swatch.Invalidate();
        _sat.Invalidate();
        _hue.Invalidate();
        _hex.Text = $"#{Picked.R:X2}{Picked.G:X2}{Picked.B:X2}"
                  + (_mode == Mode.Shift ? $"   shifting from {_dominant:0} deg" : "");

        if (_efl == null)
        {
            _checks.Controls.Clear();
            _score.Text = ""; _verdict.Text = "";
            _tally.Text = "No file open.";
            _toggle.Visible = false; _force.Visible = false;
            _trail.Before.Clear(); _trail.After.Clear(); _trail.Invalidate();
            _grid.Visible = false; _saveBtn.Enabled = false;
            return;
        }

        var checks = _efl.Detect();
        int total = checks.Count(c => !c.Info);
        int passed = checks.Count(c => c.Passed && !c.Info);
        bool smear = passed >= total;

        _checks.SuspendLayout();
        _checks.Controls.Clear();
        foreach (var c in checks)
        {
            _checks.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(0, c.Info ? 10 : 0, 0, 1),
                ForeColor = c.Info ? Ink.Dim : (c.Passed ? Ink.Text : Ink.Dim),
                Text = (c.Info ? "\u2022   " : c.Passed ? "\u2713   " : "\u2715   ") + c.Name,
            });
            _checks.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(22, 0, 0, 9),
                ForeColor = Ink.Dim, Font = Ink.Mono(8.5f), Text = c.Detail,
            });
        }
        _checks.ResumeLayout();

        _score.Text = $"{passed} OF {total}";
        _score.ForeColor = smear ? Ink.Good : Ink.Bad;
        _verdict.ForeColor = smear ? Ink.Good : Ink.Bad;
        _verdict.Text = smear
            ? "Safe to recolour."
            : "This is a normal effect. Recolouring would flatten it to a single colour.";

        var distinct = _sites.Select(s => _efl.Colour(s.Offset))
                             .Where(c => (c & 0xFFFFFF) != 0)
                             .Distinct()
                             .OrderByDescending(c => ((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255))
                             .ToList();
        _trail.Before = distinct;
        var rc = Settings;
        _trail.After = distinct.Select(c => rc.Apply(c)).ToList();
        _trail.Invalidate();

        _tintBtn.BackColor  = _mode == Mode.Tint  ? Color.FromArgb(0x30, 0x30, 0x36) : Ink.Card;
        _shiftBtn.BackColor = _mode == Mode.Shift ? Color.FromArgb(0x30, 0x30, 0x36) : Ink.Card;
        _tintBtn.ForeColor  = _mode == Mode.Tint  ? Ink.Text : Ink.Dim;
        _shiftBtn.ForeColor = _mode == Mode.Shift ? Ink.Text : Ink.Dim;

        int willChange = _sites.Count(s => rc.Apply(_efl.Colour(s.Offset)) != _efl.Colour(s.Offset));
        int mat = _sites.Count(s => s.Kind is SiteKind.Primary or SiteKind.Primary2);
        int sec = _sites.Count(s => s.Kind is SiteKind.Secondary or SiteKind.Secondary2);
        int anim = _sites.Count(s => s.Kind == SiteKind.TrackKey);
        _tally.Text = $"{willChange} of {_sites.Count} colours change   \u2022   "
                    + $"{mat} material, {sec} secondary, {anim} animated";
        _toggle.Visible = true;
        _toggle.Text = _showAll ? "Hide the list" : "Show every colour";

        _grid.Visible = _showAll;
        if (_showAll)
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var s in _sites)
            {
                uint before = _efl.Colour(s.Offset);
                uint after = rc.Apply(before);
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
        }

        _force.Visible = !smear;
        _saveBtn.Enabled = smear || _force.Checked;
    }

    void Save()
    {
        if (_efl == null) return;
        var dc = _efl.Detect();
        if (dc.Count(c => c.Passed && !c.Info) < dc.Count(c => !c.Info) && !_force.Checked)
        {
            Say("This does not look like a smear. Tick the box to do it anyway.", true);
            return;
        }

        using var d = new SaveFileDialog
        {
            Filter = "MT Framework effect (*.efl)|*.efl",
            FileName = Path.GetFileNameWithoutExtension(_path) + "_recolour.efl",
        };
        if (d.ShowDialog() != DialogResult.OK) return;

        try
        {
            var rc = Settings;
            var patched = new Efl((byte[])_efl.Data.Clone());
            foreach (var s in _sites)
                patched.SetU32(s.Offset, rc.Apply(patched.Colour(s.Offset)));

            string problem = patched.Verify(_efl);
            if (problem != null) { Say("Not saved - " + problem + ".", true); return; }

            File.WriteAllBytes(d.FileName, patched.Data);
            Say($"Saved {Path.GetFileName(d.FileName)}.", false);
        }
        catch (Exception ex)
        {
            Say("Not saved - " + ex.Message, true);
        }
    }
}