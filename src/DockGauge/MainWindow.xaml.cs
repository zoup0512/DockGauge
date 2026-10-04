using Application = System.Windows.Application;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using DockGauge.Controls;
using DockGauge.Services;

namespace DockGauge;

public partial class MainWindow : Window
{
    // 窗口尺寸 = 内容设计尺寸（Root 已整体 LayoutTransform 缩放 2/3）
    // 紧凑条与展开面板同宽（270）；速度值一位小数后列宽可收窄
    private const double CompactWidth = 270, CompactHeight = 48;
    private const double ExpandedWidth = 270, ExpandedMaxHeight = 700;
    private const int MaxPoints = 100;

    private readonly MetricsService _metrics = new();
    private readonly List<double> _netUp = new(), _netDown = new(), _diskR = new(), _diskW = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MenuItem _menuTopmost, _menuAutoStart;
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayTop, _trayAuto;
    private AppConfig _cfg = new();
    private bool _expanded;
    private double _compactLeft, _compactTop; // 紧凑条锚点：展开的基准 / 收起的还原位
    private int _driveCounter;

    public MainWindow()
    {
        InitializeComponent();
        _cfg = AppConfig.Load();
        Topmost = _cfg.Topmost;

        NetChart.Series1 = _netUp;
        NetChart.Series2 = _netDown;
        DiskChart.Series1 = _diskR;
        DiskChart.Series2 = _diskW;
        DeviceName.Text = Environment.MachineName;

        var menu = (ContextMenu)Resources["RootMenu"];
        Root.ContextMenu = menu;
        _menuTopmost = (MenuItem)menu.Items[0];
        _menuAutoStart = (MenuItem)menu.Items[1];

        InitTray();

        Loaded += OnLoaded;
        Closing += (_, _) =>
        {
            // 位置只在关闭时持久化，避免菜单联动 Save 把未定位的坐标写进配置
            _cfg.X = Left;
            _cfg.Y = Top;
            _cfg.Save();
        };
    }

    // ---------- 生命周期 ----------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();

        if (Environment.GetCommandLineArgs().Contains("--expanded"))
            SetExpanded(true);
        else
        {
            // 显式归位默认视图，不依赖 XAML 初始状态
            CompactRoot.Visibility = Visibility.Visible;
            ExpandedRoot.Visibility = Visibility.Collapsed;
        }

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick(); // 第一拍只做基线采样
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false; // 先移除图标，再释放
            _tray.Dispose();
            _tray = null;
        }
        base.OnClosed(e);
    }

    // ---------- 系统托盘 ----------

    private void InitTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "DockGauge — 性能悬浮窗",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("显示 / 隐藏悬浮窗", null, (_, _) => ToggleWindowVisibility()));
        menu.Items.Add(new WinForms.ToolStripMenuItem("展开 / 收起面板", null, (_, _) => SetExpanded(!_expanded)));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _trayTop = new WinForms.ToolStripMenuItem("置顶显示") { CheckOnClick = true };
        _trayTop.CheckedChanged += (_, _) => ApplyTopmost(_trayTop.Checked);
        menu.Items.Add(_trayTop);
        _trayAuto = new WinForms.ToolStripMenuItem("开机自启") { CheckOnClick = true };
        _trayAuto.CheckedChanged += (_, _) => ApplyAutoStart(_trayAuto.Checked);
        menu.Items.Add(_trayAuto);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("退出", null, (_, _) => Close()));

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ToggleWindowVisibility();
        };
    }

    /// <summary>运行时绘制托盘图标：蓝色圆角底 + 白色仪表盘弧线与指针。</summary>
    private static System.Drawing.Icon MakeTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            path.AddArc(2, 2, 10, 10, 180, 90);
            path.AddArc(20, 2, 10, 10, 270, 90);
            path.AddArc(20, 20, 10, 10, 0, 90);
            path.AddArc(2, 20, 10, 10, 90, 90);
            path.CloseFigure();
            using var lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Point(0, 0), new System.Drawing.Point(0, 32),
                System.Drawing.Color.FromArgb(0xFF, 0x3B, 0x79, 0xF6),
                System.Drawing.Color.FromArgb(0xFF, 0x21, 0x54, 0xC4));
            g.FillPath(lg, path);
        }

        using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3f))
        {
            g.DrawArc(pen, 8, 9, 16, 16, -60, 240);
        }
        using (var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3f))
        {
            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            g.DrawLine(pen, 16, 17, 22, 11);
        }

        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(bmp.GetHicon()).Clone();
        return icon;
    }

    private void ToggleWindowVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Hidden;
        }
        else
        {
            PositionWindow();
            Visibility = Visibility.Visible;
        }
    }

    // ---------- 采样刷新 ----------

    private void Tick()
    {
        var s = _metrics.Sample();

        CDiskRead.Text = Fmt.Speed(s.DiskReadBps);
        CDiskWrite.Text = Fmt.Speed(s.DiskWriteBps);
        CNetUp.Text = Fmt.Speed(s.NetUploadBps);
        CNetDown.Text = Fmt.Speed(s.NetDownloadBps);
        CCpu.Text = s.CpuPercent.ToString("0") + "%";
        CRam.Text = s.MemoryPercent.ToString("0") + "%";

        ENetUp.Text = Fmt.Speed(s.NetUploadBps);
        ENetDown.Text = Fmt.Speed(s.NetDownloadBps);
        EDiskRead.Text = Fmt.Speed(s.DiskReadBps);
        EDiskWrite.Text = Fmt.Speed(s.DiskWriteBps);
        UptimeText.Text = Fmt.Uptime(s.Uptime);

        CpuGauge.Percent = s.CpuPercent / 100.0;
        CpuGauge.ValueText = s.CpuPercent.ToString("0") + "%";
        MemGauge.Percent = s.MemoryPercent / 100.0;
        MemGauge.ValueText = Fmt.MemGb(s.MemoryUsed);

        Push(_netUp, s.NetUploadBps);
        Push(_netDown, s.NetDownloadBps);
        Push(_diskR, s.DiskReadBps);
        Push(_diskW, s.DiskWriteBps);
        NetChart.InvalidateVisual();
        DiskChart.InvalidateVisual();

        if (++_driveCounter >= 5)
        {
            _driveCounter = 0;
            DrivesList.ItemsSource = s.Drives.Select(d => new DriveRow
            {
                Title = $"{d.Label} ({d.Letter})",
                Usage = $"{Fmt.Size(d.Used)} / {Fmt.Size(d.Total)}",
                Percent = d.Total > 0 ? (double)d.Used / d.Total : 0,
            }).ToList();
        }
    }

    private static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > MaxPoints) list.RemoveAt(0);
    }

    private sealed class DriveRow
    {
        public string Title { get; init; } = "";
        public string Usage { get; init; } = "";
        public double Percent { get; init; }
    }

    // ---------- 视图切换 ----------

    private void ToggleClick(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

    private void SetExpanded(bool expanded)
    {
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        var wa = SystemParameters.WorkArea;

        if (expanded)
        {
            // 以紧凑条当前位置为锚点展开；收起时精确还原，反复伸缩不漂移
            _compactLeft = Left;
            _compactTop = Top;

            double newH = Math.Min(ExpandedMaxHeight, wa.Height - 16);
            CompactRoot.Visibility = Visibility.Collapsed;
            ExpandedRoot.Visibility = Visibility.Visible;
            Width = ExpandedWidth;
            Height = newH;
            Left = Math.Clamp(_compactLeft, wa.Left + 4, Math.Max(wa.Left + 4, wa.Right - Width - 4));

            // 下拉菜单式定位：放得下向下展开，贴底向上展开，否则以锚点居中
            double topBelow = _compactTop;
            double topAbove = _compactTop + CompactHeight - newH;
            double topCenter = _compactTop + CompactHeight / 2 - newH / 2;
            if (topBelow + newH <= wa.Bottom - 4)
                Top = topBelow;
            else if (topAbove >= wa.Top + 4)
                Top = topAbove;
            else
                Top = Math.Clamp(topCenter, wa.Top + 4, Math.Max(wa.Top + 4, wa.Bottom - newH - 4));
        }
        else
        {
            CompactRoot.Visibility = Visibility.Visible;
            ExpandedRoot.Visibility = Visibility.Collapsed;
            Width = CompactWidth;
            Height = CompactHeight;
            Left = _compactLeft;
            Top = _compactTop;
        }
        _expanded = expanded;
    }

    private void PositionWindow()
    {
        var wa = SystemParameters.WorkArea;
        if (_cfg.X.HasValue && _cfg.Y.HasValue)
        {
            Left = Math.Clamp(_cfg.X.Value, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            Top = Math.Clamp(_cfg.Y.Value, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        }
        else
        {
            // 默认停靠屏幕右上角（12 逻辑像素呼吸边距，避开圆角和阴影裁切）
            Left = wa.Right - Width - 12;
            Top = wa.Top + 12;
        }
        _compactLeft = Left;
        _compactTop = Top;
    }

    private void RootDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            try { DragMove(); } catch { /* 并发调用会抛异常，忽略 */ }
            // 拖动后同步锚点：收起时悬浮条回到用户拖到的位置
            if (_expanded)
            {
                _compactLeft = Left;
                _compactTop = Top + Height / 2 < SystemParameters.WorkArea.Top + SystemParameters.WorkArea.Height / 2
                    ? Top                                  // 面板在上半屏 → 悬浮条对齐面板顶部
                    : Top + Height - CompactHeight;        // 面板在下半屏 → 悬浮条对齐面板底部
            }
            else
            {
                _compactLeft = Left;
                _compactTop = Top;
            }
        }
    }

    // ---------- 菜单 ----------

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (Root.ContextMenu is null) return;
        Root.ContextMenu.PlacementTarget = (UIElement)sender;
        Root.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        Root.ContextMenu.IsOpen = true;
    }

    private void ApplyTopmost(bool on)
    {
        Topmost = on;
        _cfg.Topmost = on;
        _cfg.Save();
        SyncMenus();
    }

    private void ApplyAutoStart(bool on)
    {
        SetAutoStart(on);
        _cfg.AutoStart = on;
        _cfg.Save();
        SyncMenus();
    }

    /// <summary>让窗口右键菜单与托盘菜单的勾选状态和实际状态一致。</summary>
    private void SyncMenus()
    {
        _menuTopmost.IsChecked = Topmost;
        _menuAutoStart.IsChecked = AutoStartEnabled();
        if (_trayTop is not null) _trayTop.Checked = Topmost;
        if (_trayAuto is not null) _trayAuto.Checked = _menuAutoStart.IsChecked;
    }

    private void MenuTopmostClick(object? sender, RoutedEventArgs e) => ApplyTopmost(_menuTopmost.IsChecked);

    private void MenuAutoStartClick(object sender, RoutedEventArgs e) => ApplyAutoStart(_menuAutoStart.IsChecked);

    private void MenuExitClick(object sender, RoutedEventArgs e) => Close();

    private static void SetAutoStart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (on) key.SetValue("DockGauge", Environment.ProcessPath ?? "");
            else key.DeleteValue("DockGauge", false);
        }
        catch { /* 注册表不可写时静默失败 */ }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 注册表自启状态与配置不一致时，以注册表为准并写回配置
        var regAuto = AutoStartEnabled();
        if (_cfg.AutoStart != regAuto)
        {
            _cfg.AutoStart = regAuto;
            _cfg.Save();
        }
        SyncMenus();
    }

    private static bool AutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("DockGauge") is string;
        }
        catch { return false; }
    }
}

public class AppConfig
{
    public bool Topmost { get; set; } = true;
    public bool AutoStart { get; set; }
    // null = 未保存过位置（首次启动用默认停靠位）
    public double? X { get; set; }
    public double? Y { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DockGauge");
    private static string FilePath => Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig();
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
