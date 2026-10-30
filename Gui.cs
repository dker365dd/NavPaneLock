using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ------------------------------------------------------------------ 主题

internal static class Theme
{
    public static bool Dark;

    public static Color Page;
    public static Color Card;
    public static Color CardHover;
    public static Color CardPressed;
    public static Color Border;
    public static Color BorderStrong;

    public static Color Text;
    public static Color TextSecondary;
    public static Color TextTertiary;

    public static Color Accent;
    public static Color AccentHover;
    public static Color AccentPressed;
    public static Color OnAccent;

    public static Color Ok;
    public static Color OkSoft;
    public static Color OkBorder;
    public static Color Warn;
    public static Color WarnSoft;
    public static Color WarnBorder;

    public static Color ToggleOff;

    public static readonly Font Title = new Font("Segoe UI", 19F, FontStyle.Bold);
    public static readonly Font Subtitle = new Font("Segoe UI", 9.75F);
    public static readonly Font SectionLabel = new Font("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font RowTitle = new Font("Segoe UI", 10.5F, FontStyle.Bold);
    public static readonly Font RowSub = new Font("Segoe UI", 9F);
    public static readonly Font StatusTitle = new Font("Segoe UI", 12F, FontStyle.Bold);
    public static readonly Font Footer = new Font("Segoe UI", 8.25F);
    public static readonly Font Button = new Font("Segoe UI", 9.75F);
    public static readonly Font Mono = new Font("Consolas", 9.75F);

    static Theme()
    {
        Init();
    }

    public static void Init()
    {
        bool light = true;

        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (k != null)
                {
                    object v = k.GetValue("AppsUseLightTheme", 1);
                    light = Convert.ToInt32(v) != 0;
                }
            }
        }
        catch
        {
        }

        Dark = !light;

        if (!Dark)
        {
            Page = Color.FromArgb(0xF3, 0xF3, 0xF3);
            Card = Color.White;
            CardHover = Color.FromArgb(0xFA, 0xFA, 0xFA);
            CardPressed = Color.FromArgb(0xF3, 0xF6, 0xFA);
            Border = Color.FromArgb(0xE5, 0xE5, 0xE5);
            BorderStrong = Color.FromArgb(0xD6, 0xD6, 0xD6);

            Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
            TextSecondary = Color.FromArgb(0x61, 0x61, 0x61);
            TextTertiary = Color.FromArgb(0x8A, 0x8A, 0x8A);

            Accent = Color.FromArgb(0x00, 0x67, 0xC0);
            AccentHover = Color.FromArgb(0x19, 0x75, 0xC5);
            AccentPressed = Color.FromArgb(0x00, 0x5B, 0xA1);
            OnAccent = Color.White;

            Ok = Color.FromArgb(0x0F, 0x7B, 0x0F);
            OkSoft = Color.FromArgb(0xF1, 0xF8, 0xF1);
            OkBorder = Color.FromArgb(0xD3, 0xE7, 0xD3);
            Warn = Color.FromArgb(0x9D, 0x5D, 0x00);
            WarnSoft = Color.FromArgb(0xFD, 0xF6, 0xEC);
            WarnBorder = Color.FromArgb(0xF0, 0xE0, 0xC8);

            ToggleOff = Color.FromArgb(0xC9, 0xC9, 0xC9);
        }
        else
        {
            Page = Color.FromArgb(0x20, 0x20, 0x20);
            Card = Color.FromArgb(0x2B, 0x2B, 0x2B);
            CardHover = Color.FromArgb(0x33, 0x33, 0x33);
            CardPressed = Color.FromArgb(0x3A, 0x3A, 0x3A);
            Border = Color.FromArgb(0x3D, 0x3D, 0x3D);
            BorderStrong = Color.FromArgb(0x4C, 0x4C, 0x4C);

            Text = Color.FromArgb(0xF5, 0xF5, 0xF5);
            TextSecondary = Color.FromArgb(0xC2, 0xC2, 0xC2);
            TextTertiary = Color.FromArgb(0x9A, 0x9A, 0x9A);

            Accent = Color.FromArgb(0x4C, 0xC2, 0xFF);
            AccentHover = Color.FromArgb(0x6F, 0xD0, 0xFF);
            AccentPressed = Color.FromArgb(0x33, 0xAC, 0xE8);
            OnAccent = Color.FromArgb(0x0A, 0x0A, 0x0A);

            Ok = Color.FromArgb(0x6C, 0xCB, 0x5F);
            OkSoft = Color.FromArgb(0x25, 0x31, 0x22);
            OkBorder = Color.FromArgb(0x3A, 0x4F, 0x33);
            Warn = Color.FromArgb(0xF2, 0xC9, 0x4C);
            WarnSoft = Color.FromArgb(0x35, 0x2F, 0x1E);
            WarnBorder = Color.FromArgb(0x54, 0x4A, 0x2A);

            ToggleOff = Color.FromArgb(0x5A, 0x5A, 0x5A);
        }
    }
}

// ------------------------------------------------------------------ 分组卡片

/// <summary>设置行在分组卡片里的位置：决定圆角画在哪、接缝处画不画边。</summary>
internal enum RowGroup
{
    /// <summary>独立一行，自带完整圆角边框。</summary>
    Standalone = 0,

    /// <summary>组内第一行：上圆角，无下边框。</summary>
    Top = 1,

    /// <summary>组内中间行：直角，只有左右两条边。</summary>
    Middle = 2,

    /// <summary>组内最后一行：下圆角，无上边框。</summary>
    Bottom = 3
}

// ------------------------------------------------------------------ 绘制辅助

internal static class Draw
{
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        GraphicsPath path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
    {
        using (GraphicsPath path = RoundRect(r, radius))
        using (SolidBrush brush = new SolidBrush(color))
        {
            g.FillPath(brush, path);
        }
    }

    public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color)
    {
        using (GraphicsPath path = RoundRect(r, radius))
        using (Pen pen = new Pen(color, 1f))
        {
            g.DrawPath(pen, path);
        }
    }

    public static void Card(Graphics g, Rectangle r, Color fill, Color border)
    {
        FillRound(g, r, 6, fill);
        StrokeRound(g, r, 6, border);
    }

    /// <summary>
    /// 成组设置行的卡片：几行共用一个圆角外形，组内相邻行之间不画边框。
    /// </summary>
    public static void GroupCard(Graphics g, Rectangle r, Color fill, Color border, RowGroup group)
    {
        if (group == RowGroup.Standalone)
        {
            Card(g, r, fill, border);
            return;
        }

        using (GraphicsPath fillPath = GroupFillPath(r, 6, group))
        using (SolidBrush brush = new SolidBrush(fill))
        {
            g.FillPath(brush, fillPath);
        }

        using (GraphicsPath strokePath = GroupStrokePath(r, 6, group))
        using (Pen pen = new Pen(border, 1f))
        {
            g.DrawPath(pen, strokePath);
        }
    }

    /// <summary>成组卡片的填充轮廓。</summary>
    private static GraphicsPath GroupFillPath(Rectangle r, int radius, RowGroup group)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;

        if (group == RowGroup.Middle)
        {
            path.AddRectangle(r);
            return path;
        }

        if (group == RowGroup.Top)
        {
            // 上面两个角圆，下面两个角是直角
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            Line(path, r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        // Bottom：上面两个角是直角，下面两个角圆
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        Line(path, r.Left, r.Y, r.Right, r.Y);
        Line(path, r.Right, r.Y, r.Right, r.Bottom - radius);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>成组卡片的边框轮廓：组内的接缝处不画线。</summary>
    private static GraphicsPath GroupStrokePath(Rectangle r, int radius, RowGroup group)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;

        if (group == RowGroup.Middle)
        {
            path.StartFigure();
            Line(path, r.Left, r.Y, r.Left, r.Bottom);
            path.StartFigure();
            Line(path, r.Right, r.Y, r.Right, r.Bottom);
            return path;
        }

        if (group == RowGroup.Top)
        {
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            Line(path, r.Right, r.Y + radius, r.Right, r.Bottom);
            path.StartFigure();
            Line(path, r.Left, r.Bottom, r.Left, r.Y + radius);
            return path;
        }

        // Bottom
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        Line(path, r.Left, r.Bottom - radius, r.Left, r.Y);
        path.StartFigure();
        Line(path, r.Right, r.Y, r.Right, r.Bottom - radius);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        Line(path, r.Right - radius, r.Bottom, r.X + radius, r.Bottom);

        return path;
    }

    /// <summary>连一条线。这个 .NET 版本的 GraphicsPath 只有 PointF 版 AddLine。</summary>
    private static void Line(GraphicsPath path, float x1, float y1, float x2, float y2)
    {
        path.AddLine(new PointF(x1, y1), new PointF(x2, y2));
    }

    public static void Dot(Graphics g, Rectangle r, Color color)
    {
        using (SolidBrush b = new SolidBrush(color))
        {
            g.FillEllipse(b, r);
        }
    }

    public static void Toggle(Graphics g, Rectangle track, bool on)
    {
        Color fill = on ? Theme.Accent : Theme.ToggleOff;

        FillRound(g, track, track.Height / 2, fill);

        int knob = track.Height - 6;
        int x = on ? track.Right - knob - 3 : track.X + 3;

        Color knobColor = Theme.Dark ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;

        using (SolidBrush b = new SolidBrush(on ? knobColor : Color.White))
        {
            g.FillEllipse(b, x, track.Y + 3, knob, knob);
        }
    }
}

// ------------------------------------------------------------------ 设置行

internal sealed class SettingRow : Control
{
    private bool _hot;
    private bool _pressed;

    public string TitleText = "";
    public string SubText = "";
    public bool ShowToggle;
    public bool ToggleOn;
    public bool ShowChevron;
    public bool EnabledRow = true;

    /// <summary>在分组卡片里的位置。</summary>
    public RowGroup Group = RowGroup.Standalone;

    /// <summary>行内次级动作（画在副标题右侧，例如「同步到全部」）。为空则不显示。</summary>
    public string LinkText = "";
    public bool LinkEnabled = true;

    /// <summary>副标题颜色；为空时用默认的次要文字色。</summary>
    public Color SubColor = Color.Empty;

    private Rectangle _linkRect;
    private bool _linkHot;

    public event EventHandler Activate;
    public event EventHandler LinkActivated;

    public SettingRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

        BackColor = Theme.Page;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public void SetState(bool toggleOn, bool enabledRow)
    {
        ToggleOn = toggleOn;
        EnabledRow = enabledRow;
        Cursor = enabledRow ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    public void SetLink(string text, bool enabled)
    {
        LinkText = text;
        LinkEnabled = enabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill = Theme.Card;

        if (_hot && EnabledRow) fill = Theme.CardHover;
        if (_pressed && EnabledRow) fill = Theme.CardPressed;
        if (!EnabledRow) fill = Theme.Dark ? Color.FromArgb(0x26, 0x26, 0x26) : Color.FromArgb(0xF7, 0xF7, 0xF7);

        Draw.GroupCard(g, r, fill, Theme.Border, Group);

        // 组内第一行与第二行之间的细分隔线
        if (Group == RowGroup.Top)
        {
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                g.DrawLine(pen, 20, r.Bottom, r.Right - 20, r.Bottom);
            }
        }

        Color titleColor = EnabledRow ? Theme.Text : Theme.TextTertiary;

        int togglePad = ShowToggle ? 86 : 20;
        int linkWidth = 0;

        if (!string.IsNullOrEmpty(LinkText))
        {
            Size size = TextRenderer.MeasureText(g, LinkText, Theme.RowSub,
                new Size(int.MaxValue, 19), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            linkWidth = size.Width;

            int right = Width - togglePad - 8;
            _linkRect = new Rectangle(right - linkWidth, 30, linkWidth, 19);
        }
        else
        {
            _linkRect = Rectangle.Empty;
        }

        int contentWidth = Width - 20 - togglePad;

        TextRenderer.DrawText(g, TitleText, Theme.RowTitle,
            new Rectangle(20, 9, contentWidth, 21), titleColor,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
            | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (!string.IsNullOrEmpty(SubText))
        {
            int subWidth = contentWidth - (linkWidth > 0 ? linkWidth + 14 : 0);

            TextRenderer.DrawText(g, SubText, Theme.RowSub,
                new Rectangle(20, 30, subWidth, 19),
                SubColor.IsEmpty ? Theme.TextSecondary : SubColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        if (linkWidth > 0)
        {
            Color linkColor = !LinkEnabled
                ? Theme.TextTertiary
                : (_linkHot ? Theme.AccentHover : Theme.Accent);

            TextRenderer.DrawText(g, LinkText, Theme.RowSub, _linkRect, linkColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        if (ShowToggle)
        {
            Draw.Toggle(g, new Rectangle(Width - 64, (Height - 22) / 2, 42, 22), ToggleOn && EnabledRow);
        }

        if (ShowChevron)
        {
            using (Font f = new Font("Segoe UI", 15F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "\u203A", f,
                    new Rectangle(Width - 36, 0, 20, Height), Theme.Dark ? Theme.TextSecondary : Theme.TextTertiary,
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                    | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        // 只有键盘操作时才画焦点框，避免鼠标点击后一直留着蓝框
        if (Focused && ShowFocusCues)
        {
            Draw.StrokeRound(g, Rectangle.Inflate(r, -1, -1), 6, Theme.Accent);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hot = !_linkRect.IsEmpty && _linkRect.Contains(e.Location);

        if (hot != _linkHot)
        {
            _linkHot = hot;

            if (EnabledRow) Cursor = Cursors.Hand;

            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        _pressed = false;
        _linkHot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (EnabledRow)
        {
            _pressed = true;
            Invalidate();
            Focus();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool fire = _pressed && EnabledRow;

        _pressed = false;
        Invalidate();

        if (fire && new Rectangle(0, 0, Width, Height).Contains(e.Location))
        {
            // 先判断是不是点在行内链接上
            if (LinkEnabled && !_linkRect.IsEmpty && _linkRect.Contains(e.Location))
            {
                if (LinkActivated != null) LinkActivated(this, EventArgs.Empty);
            }
            else
            {
                OnActivate();
            }
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && EnabledRow)
        {
            OnActivate();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnActivate()
    {
        if (Activate != null) Activate(this, EventArgs.Empty);
    }
}

// ------------------------------------------------------------------ 圆角按钮

internal sealed class RoundButton : Control
{
    private bool _hot;
    private bool _pressed;

    public bool Primary;

    public RoundButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        BackColor = Theme.Page;
        Cursor = Cursors.Hand;
        Font = Theme.Button;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill;
        Color border;
        Color text;

        if (Primary)
        {
            fill = _pressed ? Theme.AccentPressed : (_hot ? Theme.AccentHover : Theme.Accent);
            border = fill;
            text = Theme.OnAccent;
        }
        else
        {
            fill = _pressed ? Theme.CardPressed : (_hot ? Theme.CardHover : Theme.Card);
            border = Theme.BorderStrong;
            text = Theme.Text;
        }

        using (GraphicsPath path = Draw.RoundRect(r, 4))
        using (SolidBrush brush = new SolidBrush(fill))
        using (Pen pen = new Pen(border, 1f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Font, r, text,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
            | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool fire = _pressed;
        _pressed = false;
        Invalidate();

        if (fire && new Rectangle(0, 0, Width, Height).Contains(e.Location))
        {
            OnClick(EventArgs.Empty);
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}

// ------------------------------------------------------------------ 等待框

internal sealed class ProgressForm : Form
{
    public ProgressForm(string text)
    {
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Page;
        ClientSize = new Size(340, 110);
        Text = "请稍候";

        Label label = new Label();
        label.Text = text;
        label.Font = Theme.RowSub;
        label.ForeColor = Theme.TextSecondary;
        label.AutoSize = false;
        label.SetBounds(20, 22, 300, 40);
        Controls.Add(label);

        ProgressBar bar = new ProgressBar();
        bar.Style = ProgressBarStyle.Marquee;
        bar.MarqueeAnimationSpeed = 30;
        bar.SetBounds(20, 66, 300, 14);
        Controls.Add(bar);

        Ui.ApplyWin11Chrome(this);
    }
}

// ------------------------------------------------------------------ 确认对话框

internal sealed class ConfirmForm : Form
{
    public static bool Ask(IWin32Window owner, string caption, string body,
        string confirmText, bool danger)
    {
        using (ConfirmForm f = new ConfirmForm(caption, body, confirmText, danger))
        {
            return f.ShowDialog(owner) == DialogResult.OK;
        }
    }

    private ConfirmForm(string caption, string body, string confirmText, bool danger)
    {
        Ui.SetupDialog(this, caption, 496, 0);

        Label title = new Label();
        title.Text = caption;
        title.Font = Theme.StatusTitle;
        title.ForeColor = Theme.Text;
        title.AutoSize = false;
        title.SetBounds(24, 20, 440, 28);
        Controls.Add(title);

        Label text = new Label();
        text.Text = body;
        text.Font = Theme.RowSub;
        text.ForeColor = Theme.TextSecondary;
        text.AutoSize = false;
        text.SetBounds(24, 56, 440, 150);
        Controls.Add(text);

        RoundButton ok = new RoundButton();
        ok.Text = confirmText;
        ok.Primary = !danger;
        ok.SetBounds(384, 214, 88, 32);
        ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(ok);

        RoundButton cancel = new RoundButton();
        cancel.Text = "取消";
        cancel.SetBounds(288, 214, 88, 32);
        cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);

        ClientSize = new Size(496, 264);

        Ui.ApplyWin11Chrome(this);

        ActiveControl = cancel;
    }
}

// ------------------------------------------------------------------ 详情窗口

internal sealed class DetailsForm : Form
{
    public static void ShowDetails(IWin32Window owner, string text)
    {
        using (DetailsForm f = new DetailsForm("详细信息", text))
        {
            f.ShowDialog(owner);
        }
    }

    public static void ShowMessage(IWin32Window owner, string caption, string text)
    {
        using (DetailsForm f = new DetailsForm(caption, text))
        {
            f.ShowDialog(owner);
        }
    }

    private DetailsForm(string caption, string text)
    {
        int height = Math.Min(624, Screen.FromPoint(Cursor.Position).WorkingArea.Height - 80);
        if (height < 360) height = 360;

        Ui.SetupDialog(this, caption, 764, height);

        int boxHeight = height - 104;

        TextBox box = new TextBox();
        box.Multiline = true;
        box.ReadOnly = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.BorderStyle = BorderStyle.None;
        box.BackColor = Theme.Card;
        box.ForeColor = Theme.Text;
        box.Font = Theme.Mono;
        box.WordWrap = false;
        box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        box.SetBounds(24, 22, 704, boxHeight);
        Controls.Add(box);

        RoundButton close = new RoundButton();
        close.Text = "关闭";
        close.Primary = true;
        close.SetBounds(640, boxHeight + 38, 88, 32);
        close.Click += delegate { Close(); };
        Controls.Add(close);

        RoundButton copy = new RoundButton();
        copy.Text = "复制";
        copy.SetBounds(536, boxHeight + 38, 88, 32);
        copy.Click += delegate
        {
            try { Clipboard.SetText(box.Text); }
            catch { }
        };
        Controls.Add(copy);

        Panel line = new Panel();
        line.BackColor = Theme.Border;
        line.SetBounds(24, boxHeight + 26, 704, 1);
        Controls.Add(line);

        Ui.ApplyWin11Chrome(this);
        ActiveControl = close;
    }
}

// ------------------------------------------------------------------ 窗口通用

internal static class Ui
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void SetupDialog(Form f, string caption, int width, int height)
    {
        f.Text = caption;
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.StartPosition = FormStartPosition.CenterParent;
        f.BackColor = Theme.Page;
        f.Font = Theme.RowSub;
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.ShowIcon = false;

        if (height > 0) f.ClientSize = new Size(width, height);
    }

    public static void ApplyWin11Chrome(Form f)
    {
        if (f.IsHandleCreated) Apply(f);
        else f.HandleCreated += delegate { Apply(f); };
    }

    private static void Apply(Form f)
    {
        try
        {
            if (Environment.OSVersion.Version.Major < 10) return;
            if (Environment.OSVersion.Version.Build < 22000) return;

            int round = 2;
            DwmSetWindowAttribute(f.Handle, 33, ref round, sizeof(int));

            int caption = ColorTranslator.ToWin32(Theme.Page);
            DwmSetWindowAttribute(f.Handle, 35, ref caption, sizeof(int));

            int textColor = ColorTranslator.ToWin32(Theme.Text);
            DwmSetWindowAttribute(f.Handle, 36, ref textColor, sizeof(int));

            if (Theme.Dark)
            {
                int dark = 1;
                DwmSetWindowAttribute(f.Handle, 20, ref dark, sizeof(int));
            }
        }
        catch
        {
        }
    }
}

// ------------------------------------------------------------------ 主窗口

internal sealed class MainForm : Form
{
    private const int RowHeight = 56;
    private const int RowX = 26;
    private const int RowW = 508;

    private readonly FlowLayoutPanel _flow = new FlowLayoutPanel();

    private readonly SettingRow _lockRow = new SettingRow();
    private readonly SettingRow _expandRow = new SettingRow();
    private readonly SettingRow _sortRow = new SettingRow();
    private readonly SettingRow _clipRow = new SettingRow();
    private readonly SettingRow _brightRow = new SettingRow();
    private readonly SettingRow _hibernateRow = new SettingRow();
    private readonly SettingRow _cmdRow = new SettingRow();
    private readonly SettingRow _psRow = new SettingRow();
    private readonly SettingRow _detailRow = new SettingRow();

    private bool _busy;
    private bool _locked;
    private bool _expandOn;
    private bool _sortOn;
    private bool _keyExists;

    private bool _brightnessSupported = true;
    private bool _brightnessOn;
    private bool _brightnessAllOn;
    private bool _brightnessPartial;
    private int _brightnessOnCount;
    private int _brightnessTotal;

    private bool _clipOn = true;
    private bool _clipBlocked;
    private string _clipServiceName = "";

    private bool _hibernateEnabled;

    private bool _cmdOn;
    private bool _cmdAllLocations = true;

    private bool _psOn;
    private bool _psAllLocations = true;

    public MainForm()
    {
        this.Text = App.Title;
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Theme.Page;
        this.Font = Theme.RowSub;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.ShowIcon = false;

        _flow.Dock = DockStyle.Fill;
        _flow.FlowDirection = FlowDirection.TopDown;
        _flow.WrapContents = false;
        _flow.AutoScroll = true;
        _flow.AutoScrollMinSize = new Size(0, 882);
        _flow.HorizontalScroll.Enabled = false;
        _flow.BackColor = Theme.Page;
        _flow.Padding = new Padding(RowX, 14, RowX, 12);
        Controls.Add(_flow);

        BuildContent();
        FitToScreen();

        Ui.ApplyWin11Chrome(this);

        this.Activated += delegate { RefreshState(); };
        this.Shown += delegate { RefreshState(); };
    }

    private void FitToScreen()
    {
        int maxHeight = Screen.FromControl(this).WorkingArea.Height - 40;

        if (maxHeight < 420) maxHeight = 420;

        int desired = 892;

        ClientSize = new Size(560, Math.Min(desired, maxHeight));
    }

    private void BuildContent()
    {
        Label title = new Label();
        title.Text = App.Title;
        title.Font = Theme.Title;
        title.ForeColor = Theme.Text;
        title.AutoSize = false;
        AddFlow(title, 480, 34, 0);

        Label sub = new Label();
        sub.Text = "导航窗格 · 排序 · 电源 · 右键菜单";
        sub.Font = Theme.Subtitle;
        sub.ForeColor = Theme.TextSecondary;
        sub.AutoSize = false;
        AddFlow(sub, 480, 20, 12, 2);

        AddSection("导航窗格", 4);

        // 状态直接并进这一行：副标题即状态，锁定/未锁定用颜色区分
        _lockRow.TitleText = "锁定导航窗格";
        _lockRow.SubText = "冻结当前的展开与折叠状态";
        _lockRow.ShowToggle = true;
        _lockRow.Activate += delegate { OnToggleLock(); };
        AddFlow(_lockRow, RowW, 68, 12);

        _expandRow.TitleText = "展开到打开的文件夹";
        _expandRow.SubText = "自动展开到当前打开的位置";
        _expandRow.ShowToggle = true;
        _expandRow.Activate += delegate { OnToggleExpand(); };
        AddFlow(_expandRow, RowW, RowHeight, 18);

        AddSection("系统设置", 4);

        _sortRow.TitleText = "数字排序（自然排序）";
        _sortRow.SubText = "按 1、2、10 排序";
        _sortRow.ShowToggle = true;
        _sortRow.Activate += delegate { OnToggleSort(); };
        AddFlow(_sortRow, RowW, RowHeight, 6);

        _clipRow.TitleText = "剪贴板历史";
        _clipRow.SubText = "Win+V 查看复制过的内容";
        _clipRow.ShowToggle = true;
        _clipRow.Activate += delegate { OnToggleClip(); };
        _clipRow.LinkActivated += delegate { OnRepairClipService(); };
        AddFlow(_clipRow, RowW, 68, 12);

        _brightRow.TitleText = "关闭自动亮度";
        _brightRow.SubText = "关闭随环境光自动调整亮度";
        _brightRow.ShowToggle = true;
        _brightRow.Activate += delegate { OnToggleBrightness(); };
        _brightRow.LinkActivated += delegate { OnSyncBrightness(); };
        AddFlow(_brightRow, RowW, 68, 12);

        _hibernateRow.TitleText = "关闭休眠";
        _hibernateRow.SubText = "同时关闭「快速启动」";
        _hibernateRow.ShowToggle = true;
        _hibernateRow.Activate += delegate { OnToggleHibernate(); };
        AddFlow(_hibernateRow, RowW, RowHeight, 18);

        AddSection("右键菜单", 4);

        _cmdRow.TitleText = "在此处打开CMD窗口";
        _cmdRow.SubText = "点击开启，在文件夹空白处右键打开 CMD";
        _cmdRow.ShowToggle = true;
        _cmdRow.Group = RowGroup.Top;
        _cmdRow.Activate += delegate { OnToggleCmd(); };
        _cmdRow.LinkActivated += delegate { OnToggleCmdScope(); };
        AddFlow(_cmdRow, RowW, 68, 0);

        _psRow.TitleText = "在此处打开PowerShell窗口";
        _psRow.SubText = "点击开启，在文件夹空白处右键打开 PowerShell";
        _psRow.ShowToggle = true;
        _psRow.Group = RowGroup.Bottom;
        _psRow.Activate += delegate { OnTogglePowerShell(); };
        _psRow.LinkActivated += delegate { OnTogglePowerShellScope(); };
        AddFlow(_psRow, RowW, 68, 6);

        _detailRow.TitleText = "查看详细信息";
        _detailRow.SubText = "权限、注册表值、电源状态、备份与日志";
        _detailRow.ShowChevron = true;
        _detailRow.Activate += delegate { ShowDetails(); };
        AddFlow(_detailRow, RowW, RowHeight, 20);

        Panel line = new Panel();
        line.BackColor = Theme.Border;
        AddFlow(line, RowW, 1, 7);

        Label data = new Label();
        data.Text = "数据目录  " + NavPaneCore.DataDir;
        data.Font = Theme.Footer;
        data.ForeColor = Theme.TextTertiary;
        data.AutoSize = false;
        AddFlow(data, RowW, 18, 0);

        Label hint = new Label();
        hint.Text = "v" + App.Version + "    锁定前请先摆好展开形态，并关闭所有资源管理器窗口";
        hint.Font = Theme.Footer;
        hint.ForeColor = Theme.TextTertiary;
        hint.AutoSize = false;
        AddFlow(hint, RowW, 18, 0);
    }

    /// <summary>
    /// 纵向流式堆叠：控件按加入顺序从上往下排，间距由 gapBelow 决定。
    /// 以后加一行只要多调一次 AddFlow，不用再算 Y 坐标。
    /// </summary>
    private void AddFlow(Control c, int width, int height, int gapBelow, int left = 0)
    {
        c.Size = new Size(width, height);
        c.Margin = new Padding(left, 0, 0, gapBelow);
        _flow.Controls.Add(c);
    }

    private void AddSection(string text, int gapBelow)
    {
        Label label = new Label();
        label.Text = text;
        label.Font = Theme.SectionLabel;
        label.ForeColor = Theme.TextSecondary;
        label.AutoSize = false;
        AddFlow(label, 200, 18, gapBelow, 2);
    }

    // ------------------------------------------------------------ 状态刷新

    private void RefreshState()
    {
        if (_busy) return;

        StatusSnapshot s = NavPaneCore.GetStatus();

        _keyExists = s.KeyExists;
        _locked = s.KeyExists && s.Locked;
        _expandOn = s.ExpandToCurrentFolder.HasValue && s.ExpandToCurrentFolder.Value == 1;
        _sortOn = !s.NumericSort.HasValue || s.NumericSort.Value == 0;

        _lockRow.SetState(_locked, _keyExists);

        if (!_keyExists)
        {
            _lockRow.SubText = "状态键不存在，先打开一次资源管理器";
            _lockRow.SubColor = Theme.Warn;
        }
        else if (_locked)
        {
            _lockRow.SubText = "已锁定：展开状态已冻结，重启后回到当前形态";
            _lockRow.SubColor = Theme.Ok;
        }
        else
        {
            _lockRow.SubText = "未锁定：可以自由保存展开与折叠状态";
            _lockRow.SubColor = Color.Empty;
        }

        _expandRow.SetState(_expandOn, true);
        _expandRow.SubText = _expandOn ? "已开启，导航窗格会自动展开" : "已关闭，点击开启";

        _sortRow.SetState(_sortOn, true);
        _sortRow.SubText = _sortOn
            ? "已启用：按数值大小排序（1、2、10）"
            : "已禁用：逐字符比较（1、10、2）";

        ClipboardInfo c = ClipboardCore.GetInfo();

        _clipOn = c.HistoryEnabled;
        _clipBlocked = c.Blocked;
        _clipServiceName = c.ServiceName;

        _clipRow.SetState(_clipOn, true);

        if (c.Blocked)
        {
            _clipRow.SubText = "剪贴板服务被禁用，Win+V 不会生效";
            _clipRow.SubColor = Theme.Warn;
            _clipRow.SetLink("修复", true);
        }
        else if (!c.ServiceFound && !c.TemplateStart.HasValue)
        {
            _clipRow.SubText = "未找到剪贴板服务，本机可能不支持该功能";
            _clipRow.SubColor = Theme.Warn;
            _clipRow.SetLink("", false);
        }
        else
        {
            _clipRow.SubText = _clipOn
                ? "已开启，Win+V 可查看复制历史"
                : "已关闭，Win+V 不再显示历史";
            _clipRow.SubColor = Color.Empty;
            _clipRow.SetLink("", false);
        }

        BrightnessInfo b = PowerCore.GetBrightness();

        _brightnessSupported = b.Supported;
        _brightnessOn = b.ActiveOn;
        _brightnessAllOn = b.AllOn;
        _brightnessPartial = b.PartiallyOn;
        _brightnessOnCount = b.SchemeOnCount;
        _brightnessTotal = b.SchemeCount;

        // 这一行的语义是「关闭自动亮度」：开关打开 = 已关闭
        bool brightnessOff = !_brightnessOn;
        bool consistent = _brightnessOnCount == 0 || _brightnessAllOn;

        _brightRow.SetState(brightnessOff, _brightnessSupported);

        if (!_brightnessSupported)
        {
            _brightRow.SubText = "此设备不支持该设置（当前方案读取失败）";
            _brightRow.SetLink("", false);
        }
        else if (consistent)
        {
            _brightRow.SubText = brightnessOff
                ? "已关闭，所有电源方案一致"
                : "仍处于开启状态，所有方案一致";

            _brightRow.SetLink("已全部同步", false);
        }
        else
        {
            _brightRow.SubText = "当前" + (brightnessOff ? "已关闭" : "仍开启")
                + "，另有方案未同步（" + (_brightnessTotal - _brightnessOnCount) + " 个开启）";

            _brightRow.SetLink("同步到全部", true);
        }

        HibernateInfo h = PowerCore.GetHibernate();

        _hibernateEnabled = h.Enabled.HasValue && h.Enabled.Value;
        _hibernateRow.SetState(!_hibernateEnabled, true);
        _hibernateRow.SubText = _hibernateEnabled
            ? "当前已开启，hiberfil.sys 约占内存 40%"
            : "已关闭（快速启动也已关闭）";

        ContextMenuInfo cm = ContextMenuCore.GetInfo();

        UpdateCmdRow(cm);
        UpdatePowerShellRow(cm);
    }

    /// <summary>刷新「在此处打开CMD窗口」这一行。</summary>
    private void UpdateCmdRow(ContextMenuInfo cm)
    {
        _cmdOn = cm.CmdBackground;

        _cmdRow.SetState(_cmdOn, true);

        if (_cmdOn)
        {
            // 三处都在才算「全部位置」；万一被手工改过，点一下链接就能重新写齐
            _cmdAllLocations = cm.CmdDirectory && cm.CmdDrive;

            _cmdRow.SubText = _cmdAllLocations
                ? "已开启：空白处、文件夹上、驱动器右键都能打开"
                : "已开启：仅文件夹空白处右键时出现";

            _cmdRow.SetLink(_cmdAllLocations ? "仅空白处" : "全部位置", true);
        }
        else
        {
            // 关闭状态下用上次保存的作用范围偏好
            _cmdAllLocations = ContextMenuCore.GetScope(ShellKind.Cmd);

            _cmdRow.SubText = "点击开启，在文件夹空白处右键打开 CMD";
            _cmdRow.SetLink("", false);
        }
    }

    /// <summary>刷新「在此处打开PowerShell窗口」这一行。</summary>
    private void UpdatePowerShellRow(ContextMenuInfo cm)
    {
        _psOn = cm.PsBackground;

        _psRow.SetState(_psOn, true);

        if (_psOn)
        {
            _psAllLocations = cm.PsDirectory && cm.PsDrive;

            _psRow.SubText = _psAllLocations
                ? "已开启：空白处、文件夹上、驱动器右键都能打开"
                : "已开启：仅文件夹空白处右键时出现";

            _psRow.SetLink(_psAllLocations ? "仅空白处" : "全部位置", true);
        }
        else
        {
            _psAllLocations = ContextMenuCore.GetScope(ShellKind.PowerShell);

            _psRow.SubText = "点击开启，在文件夹空白处右键打开 PowerShell";
            _psRow.SetLink("", false);
        }
    }

    // ------------------------------------------------------------ 交互

    private void OnToggleLock()
    {
        if (!_keyExists)
        {
            MessageBox.Show(this,
                "未找到导航窗格状态键。\n请先打开一次资源管理器，关闭后再点「查看详细信息」刷新。",
                "无法操作", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string message = "";

        if (!_locked)
        {
            string body =
                "锁定后，资源管理器无法再保存新的展开/折叠状态，重启后会回到现在这个形态。\n\n" +
                "请先确认三件事：\n" +
                "1. 已在左侧摆好想保留的展开形态\n" +
                "2. 已关闭所有资源管理器窗口（含打开/另存为对话框）\n" +
                "3. 锁定后点菜单项会「没反应」，这是正常现象";

            if (!ConfirmForm.Ask(this, "锁定导航窗格", body, "锁定", false)) return;

            OpResult r = RunWithProgress<OpResult>("正在锁定…", delegate
            {
                string m;
                OpResult res = NavPaneCore.Lock(out m);
                message = m;
                return res;
            });

            RefreshState();
            HandleResult(r, message, false);
            return;
        }

        OpResult ru = RunWithProgress<OpResult>("正在解锁…", delegate
        {
            string m;
            OpResult res = NavPaneCore.Unlock(out m);
            message = m;
            return res;
        });

        RefreshState();
        HandleResult(ru, message, true);
    }

    private void OnToggleExpand()
    {
        bool target = !_expandOn;
        string message = "";

        OpResult result = RunWithProgress<OpResult>("正在应用…", delegate
        {
            string m;
            OpResult r = NavPaneCore.SetExpandToCurrentFolder(target, out m);
            message = m;
            return r;
        });

        RefreshState();
        HandleResult(result, message, true);
    }

    private void OnToggleSort()
    {
        ApplySystemSetting("sort", !_sortOn, "正在修改数字排序…", true);
    }

    private void OnToggleClip()
    {
        bool target = !_clipOn;

        if (!target)
        {
            string body =
                "关闭后 Win+V 不再显示复制过的历史内容。\n" +
                "开关可以随时再打开，不会影响已复制到剪贴板里的当前内容。";

            if (!ConfirmForm.Ask(this, "关闭剪贴板历史", body, "关闭", false)) return;
        }

        string message = "";

        OpResult r = RunWithProgress<OpResult>("正在修改剪贴板历史…", delegate
        {
            string m;
            OpResult res = ClipboardCore.SetHistory(target, out m);
            message = m;
            return res;
        });

        RefreshState();
        HandleResult(r, message, false);

        if (r == OpResult.Ok && target && ClipboardCore.GetInfo().Blocked)
        {
            MessageBox.Show(this,
                "剪贴板历史已开启，但剪贴板服务当前被禁用，Win+V 仍然不会生效。\n\n" +
                "点这一行的「修复」把服务改回手动启动，然后注销重新登录即可。",
                "还需要修复服务", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnRepairClipService()
    {
        string body =
            "将把剪贴板服务（模板 cbdhsvc 和当前用户实例）的启动类型改回「手动」。\n\n" +
            "这个改动是机器范围的，需要管理员权限。\n" +
            "改完后必须注销重新登录才会生效 —— Windows 的剪贴板服务按用户会话启动，无法用命令立即拉起。";

        if (!ConfirmForm.Ask(this, "修复剪贴板服务", body, "修复", false)) return;

        ApplySystemSetting("clip-service", true, "正在修复剪贴板服务…", false);
    }

    private void OnToggleBrightness()
    {
        ApplySystemSetting("brightness-current", !_brightnessOn, "正在修改当前电源方案…", true);
    }

    /// <summary>把当前状态同步到所有电源方案。</summary>
    private void OnSyncBrightness()
    {
        // 同步的是「自动亮度是否开启」这个真实状态
        bool enable = _brightnessOn;

        if (enable)
        {
            string body = "当前是「未关闭」状态，同步会为其余 "
                + Math.Max(0, _brightnessTotal - _brightnessOnCount)
                + " 个方案重新开启自动亮度。";

            if (!ConfirmForm.Ask(this, "同步到所有方案", body, "同步", false)) return;
        }

        ApplySystemSetting("brightness-all", enable, "正在同步到所有电源方案…", true);
    }

    private void OnToggleHibernate()
    {
        // 这一行的语义是「关闭休眠」：开关打开 = 休眠已关闭
        bool enable = !_hibernateEnabled;

        if (enable)
        {
            string body =
                "重新开启休眠会重建 hiberfil.sys，占用约内存 40% 的磁盘空间，\n" +
                "同时会恢复「快速启动」功能。";

            if (!ConfirmForm.Ask(this, "开启休眠", body, "开启", false)) return;
        }
        else
        {
            string body =
                "关闭休眠会删除 hiberfil.sys，并同时关闭「快速启动」。\n" +
                "这是 Windows 的固有行为，之后重新开启休眠才会恢复。";

            if (!ConfirmForm.Ask(this, "关闭休眠", body, "关闭休眠", false)) return;
        }

        ApplySystemSetting("hibernate", enable,
            enable ? "正在开启休眠，可能需要几十秒…" : "正在关闭休眠…", true);
    }

    /// <summary>注册 / 注销右键菜单里的「在此处打开CMD窗口」。</summary>
    private void OnToggleCmd()
    {
        bool target = !_cmdOn;
        string message = "";

        OpResult r = RunWithProgress<OpResult>("正在更新右键菜单…", delegate
        {
            string m;
            OpResult res = ContextMenuCore.SetEnabled(ShellKind.Cmd, target, _cmdAllLocations, out m);
            message = m;
            return res;
        });

        if (r == OpResult.Ok) ContextMenuCore.SetScope(ShellKind.Cmd, _cmdAllLocations);

        RefreshState();
        HandleResult(r, message, false);
    }

    /// <summary>切换作用范围：只空白处 / 三处都注册。</summary>
    private void OnToggleCmdScope()
    {
        if (!_cmdOn) return;

        bool target = !_cmdAllLocations;
        string message = "";

        OpResult r = RunWithProgress<OpResult>("正在更新右键菜单…", delegate
        {
            string m;
            OpResult res = ContextMenuCore.SetEnabled(ShellKind.Cmd, true, target, out m);
            message = m;
            return res;
        });

        if (r == OpResult.Ok) ContextMenuCore.SetScope(ShellKind.Cmd, target);

        RefreshState();
        HandleResult(r, message, false);
    }

    /// <summary>注册 / 注销右键菜单里的「在此处打开PowerShell窗口」。</summary>
    private void OnTogglePowerShell()
    {
        bool target = !_psOn;
        string message = "";

        OpResult r = RunWithProgress<OpResult>("正在更新右键菜单…", delegate
        {
            string m;
            OpResult res = ContextMenuCore.SetEnabled(ShellKind.PowerShell, target, _psAllLocations, out m);
            message = m;
            return res;
        });

        if (r == OpResult.Ok) ContextMenuCore.SetScope(ShellKind.PowerShell, _psAllLocations);

        RefreshState();
        HandleResult(r, message, false);
    }

    /// <summary>切换 PowerShell 菜单项的作用范围。</summary>
    private void OnTogglePowerShellScope()
    {
        if (!_psOn) return;

        bool target = !_psAllLocations;
        string message = "";

        OpResult r = RunWithProgress<OpResult>("正在更新右键菜单…", delegate
        {
            string m;
            OpResult res = ContextMenuCore.SetEnabled(ShellKind.PowerShell, true, target, out m);
            message = m;
            return res;
        });

        if (r == OpResult.Ok) ContextMenuCore.SetScope(ShellKind.PowerShell, target);

        RefreshState();
        HandleResult(r, message, false);
    }

    /// <summary>需要 HKLM 写入的操作：已提权就直接做，否则拉起提权实例。</summary>
    private void ApplySystemSetting(string action, bool target, string busyText, bool offerRestart)
    {
        int code = -1;
        string message = "";

        if (NavPaneCore.IsAdminUser())
        {
            OpResult r = RunWithProgress<OpResult>(busyText, delegate
            {
                string m;
                OpResult res;

                switch (action)
                {
                    case "sort":
                        res = NavPaneCore.SetNumericSort(target, out m);
                        break;

                    case "brightness-current":
                        res = PowerCore.SetBrightness(target, false, out m);
                        break;

                    case "brightness-all":
                        res = PowerCore.SetBrightness(target, true, out m);
                        break;

                    case "clip-history":
                        res = ClipboardCore.SetHistory(target, out m);
                        break;

                    case "clip-service":
                        res = ClipboardCore.RepairService(out m);
                        break;

                    default:
                        res = PowerCore.SetHibernate(target, out m);
                        break;
                }

                message = m;
                return res;
            });

            code = r == OpResult.Ok ? 0 : (r == OpResult.NoPermission ? 4 : 3);
        }
        else
        {
            int exit = -1;

            RunWithProgress<int>(busyText, delegate
            {
                int innerExit;
                bool started = RunElevated("--apply " + action + " " + (target ? "1" : "0"), out innerExit);

                if (!started)
                {
                    exit = innerExit;
                    return 0;
                }

                int fileCode;
                string fileMessage;
                App.ReadResult(out fileCode, out fileMessage);

                exit = fileCode >= 0 ? fileCode : innerExit;
                message = fileMessage;
                return 0;
            });

            code = exit;
        }

        RefreshState();
        ReportSystemResult(code, message, offerRestart);
    }

    private void ReportSystemResult(int code, string message, bool offerRestart)
    {
        if (code == 1223)
        {
            MessageBox.Show(this, "已取消提权，设置未修改。", "已取消",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (code == -2)
        {
            MessageBox.Show(this,
                "提权实例执行超时。\n请稍后在「查看详细信息」里确认状态是否已经生效。",
                "超时", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (code < 0)
        {
            MessageBox.Show(this, "无法启动提权实例（错误码 " + code + "）。", "操作失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (code != 0)
        {
            string text = string.IsNullOrEmpty(message)
                ? "操作未能完成（退出码 " + code + "）。"
                : message;

            if (text.Length > 100 || text.IndexOf('\n') >= 0)
            {
                DetailsForm.ShowMessage(this, "操作失败", text);
            }
            else
            {
                MessageBox.Show(this, text, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return;
        }

        if (offerRestart) AskRestart();
    }

    private void HandleResult(OpResult result, string message, bool offerRestart)
    {
        if (result != OpResult.Ok)
        {
            if (message.Length > 100 || message.IndexOf('\n') >= 0)
            {
                DetailsForm.ShowMessage(this, "操作失败", message);
            }
            else
            {
                MessageBox.Show(this, message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return;
        }

        if (!offerRestart) return;

        AskRestart();
    }

    private void AskRestart()
    {
        if (!ConfirmForm.Ask(this, "重启资源管理器？",
                "设置已生效。是否立即重启资源管理器？\n\n重启会关闭当前所有资源管理器窗口。",
                "立即重启", false))
        {
            return;
        }

        bool ok = RunWithProgress<bool>("正在重启资源管理器…", delegate
        {
            return NavPaneCore.RestartExplorer();
        });

        if (!ok)
        {
            MessageBox.Show(this,
                "未能自动拉起资源管理器。\n请按 Ctrl+Shift+Esc 打开任务管理器 → 文件 → 运行新任务 → 输入 explorer。",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshState();
    }

    // ------------------------------------------------------------ 线程辅助

    /// <summary>后台线程执行 + 模态等待框，避免长操作把窗口冻住。</summary>
    private T RunWithProgress<T>(string text, Func<T> work)
    {
        _busy = true;

        try
        {
            using (ProgressForm dlg = new ProgressForm(text))
            {
                T result = default(T);
                Exception error = null;

                Thread t = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        result = work();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        try
                        {
                            dlg.BeginInvoke((MethodInvoker)delegate { dlg.Close(); });
                        }
                        catch
                        {
                        }
                    }
                }));

                t.IsBackground = true;
                t.Start();

                dlg.ShowDialog(this);

                if (error != null)
                {
                    throw new InvalidOperationException(error.Message, error);
                }

                return result;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool RunElevated(string arguments, out int exitCode)
    {
        exitCode = -1;

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Application.ExecutablePath;
            psi.Arguments = arguments;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            using (Process p = Process.Start(psi))
            {
                if (p == null) return false;

                if (!p.WaitForExit(180000))
                {
                    exitCode = -2;
                    return false;
                }

                exitCode = p.ExitCode;
                return true;
            }
        }
        catch (Win32Exception ex)
        {
            exitCode = ex.NativeErrorCode;
            return false;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 详细信息

    private void ShowDetails()
    {
        StatusSnapshot s = NavPaneCore.GetStatus(true);

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("程序        " + App.Title + " v" + App.Version);
        sb.AppendLine("主题        " + (Theme.Dark ? "深色" : "浅色"));
        sb.AppendLine("用户        " + s.UserName);
        sb.AppendLine("SID         " + s.UserSid);
        sb.AppendLine("管理员      " + (s.IsElevated ? "是" : "否"));
        sb.AppendLine("注册表可写  " + (s.HiveWritable ? "是" : "否（进程被限制，通常是把 exe 放在沙箱/受控目录里运行）"));
        sb.AppendLine("系统        " + s.OsVersion);
        sb.AppendLine();
        sb.AppendLine("状态键      HKCU\\" + NavPaneCore.NavPanePath);
        sb.AppendLine("            " + (s.KeyExists ? "存在" : "不存在"));
        sb.AppendLine("ExpandedState  " + (s.ValueBytes >= 0
            ? "<二进制 " + s.ValueBytes + " 字节>"
            : (s.KeyExists ? "无法读取（权限不足）" : "未找到")));
        sb.AppendLine("锁定状态    " + (!s.KeyExists ? "-" : (s.Locked ? "已锁定" : "未锁定")));
        sb.AppendLine();
        sb.AppendLine("所有者      " + s.Owner);
        sb.AppendLine("继承        " + (s.InheritanceEnabled ? "已启用" : "已关闭（规则均为显式）"));
        sb.AppendLine();
        sb.AppendLine("权限（ACE）");

        foreach (AceInfo a in s.Aces)
        {
            string identity = a.Identity;
            if (identity.Length > 36) identity = identity.Substring(0, 33) + "...";

            sb.AppendLine(string.Format("  {0,-6}{1,-38}{2,-28}{3}",
                a.Type, identity, a.Rights, a.IsInherited ? "继承" : "显式"));
        }

        if (s.Aces.Count == 0) sb.AppendLine("  <无法读取>");

        sb.AppendLine();
        sb.AppendLine("全局开关    HKCU\\" + NavPaneCore.AdvancedPath);
        sb.AppendLine("  NavPaneExpandToCurrentFolder  " + FormatDword(s.ExpandToCurrentFolder));
        sb.AppendLine("  NavPaneShowAllFolders         " + FormatDword(s.ShowAllFolders));
        sb.AppendLine();

        sb.AppendLine("数字排序    HKLM\\" + NavPaneCore.SortPolicyPath);
        sb.AppendLine("  NoStrCmpLogical  " + (s.NumericSort.HasValue
            ? s.NumericSort.Value + (s.NumericSort.Value == 0 ? "（已启用）" : "（已禁用）")
            : "未设置（默认：已启用）"));

        if (NavPaneCore.IsDomainJoined())
        {
            sb.AppendLine("  注意：当前处于域环境，该策略可能被组策略覆盖");
        }

        sb.AppendLine();

        ClipboardInfo ci = ClipboardCore.GetInfo();

        sb.AppendLine("剪贴板历史  HKCU\\" + ClipboardCore.RegPath);
        sb.AppendLine("  EnableClipboardHistory  " + (ci.ValuePresent
            ? ci.HistoryEnabled.ToString()
            : "未设置（默认开启）"));
        sb.AppendLine("  服务模板 cbdhsvc        " + DescribeServiceStart(ci.TemplateStart));
        sb.AppendLine("  用户实例                " + (ci.ServiceFound
            ? ci.ServiceName + "  " + DescribeServiceStart(ci.ServiceStart)
            : "未找到"));

        if (ci.Blocked)
        {
            sb.AppendLine("  注意：服务被禁用，Win+V 不会生效；修复后需注销重新登录");
        }

        sb.AppendLine();

        BrightnessInfo b = PowerCore.GetBrightness();

        sb.AppendLine("自适应亮度  fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
        sb.AppendLine("  当前方案  " + (b.Supported ? (b.ActiveOn ? "已开启" : "已关闭") : "读取失败（可能不支持）"));
        sb.AppendLine("  电源方案  " + b.SchemeOnCount + " / " + b.SchemeCount + " 个已开启");
        sb.AppendLine();

        HibernateInfo h = PowerCore.GetHibernate();

        sb.AppendLine("休眠        HKLM\\" + PowerCore.HibernateRegistryPath);
        sb.AppendLine("  HibernateEnabled  " + (h.Enabled.HasValue ? (h.Enabled.Value ? "1（已开启）" : "0（已关闭）") : "未找到"));
        sb.AppendLine("  hiberfil.sys      " + (h.FileExists.HasValue
            ? (h.FileExists.Value ? "存在" : "不存在")
            : "无法确定（该文件是系统文件，可能需要管理员权限）"));
        sb.AppendLine();

        ContextMenuInfo cm = ContextMenuCore.GetInfo();

        sb.AppendLine("右键菜单    HKCU\\Software\\Classes");
        sb.AppendLine("  CMD      " + ContextMenuCore.CmdVerbName);
        sb.AppendLine("    空白处  " + (cm.CmdBackground ? "已注册" : "未注册"));
        sb.AppendLine("    文件夹  " + (cm.CmdDirectory ? "已注册" : "未注册"));
        sb.AppendLine("    驱动器  " + (cm.CmdDrive ? "已注册" : "未注册"));
        sb.AppendLine("  PowerShell  " + ContextMenuCore.PsVerbName);
        sb.AppendLine("    程序    " + ContextMenuCore.PowerShellPath());
        sb.AppendLine("    空白处  " + (cm.PsBackground ? "已注册" : "未注册"));
        sb.AppendLine("    文件夹  " + (cm.PsDirectory ? "已注册" : "未注册"));
        sb.AppendLine("    驱动器  " + (cm.PsDrive ? "已注册" : "未注册"));
        sb.AppendLine("  作用范围  CMD=" + (ContextMenuCore.GetScope(ShellKind.Cmd) ? "全部位置" : "仅空白处")
            + "  PowerShell=" + (ContextMenuCore.GetScope(ShellKind.PowerShell) ? "全部位置" : "仅空白处"));

        sb.AppendLine();

        sb.AppendLine("权限备份    " + (s.BackupPath.Length > 0 ? s.BackupPath : "无（尚未锁定过）"));
        if (s.BackupTime.Length > 0) sb.AppendLine("            " + s.BackupTime);
        sb.AppendLine("操作日志    " + (s.LogPath.Length > 0 ? s.LogPath : "无"));
        if (s.LogTime.Length > 0) sb.AppendLine("            " + s.LogTime);
        sb.AppendLine("数据目录    " + NavPaneCore.DataDir);

        DetailsForm.ShowDetails(this, sb.ToString());
    }

    private static string FormatDword(int? value)
    {
        if (!value.HasValue) return "未设置（默认 0）";

        return value.Value + (value.Value == 1 ? "（已开启）" : "（已关闭）");
    }

    private static string DescribeServiceStart(int? start)
    {
        if (!start.HasValue) return "未找到";

        switch (start.Value)
        {
            case 2: return "自动";
            case 3: return "手动";
            case 4: return "禁用";
            default: return "未知(" + start.Value + ")";
        }
    }
}
