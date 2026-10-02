using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using DockGauge.Controls;
using DockGauge.Services;

namespace DockGauge;

public partial class MainWindow : Window
{
    private const double CompactWidth = 480, CompactHeight = 72;
    private const double ExpandedWidth = 404, ExpandedMaxHeight = 934;
    private const int MaxPoints = 100;

    private readonly MetricsService _metrics = new();
    private readonly List<double> _netUp = new(), _netDown = new(), _diskR = new(), _diskW = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MenuItem _menuTopmost, _menuAutoStart;
    private AppConfig _cfg = new();
    private bool _expanded;
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

        Loaded += OnLoaded;
        Closing += (_, _) => { _cfg.Save(); };
    }

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
        _expanded = expanded;
        var wa = SystemParameters.WorkArea;

        double newW = expanded ? ExpandedWidth : CompactWidth;
        double newH = expanded ? Math.Min(ExpandedMaxHeight, wa.Height - 16) : CompactHeight;

        CompactRoot.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedRoot.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        double cx = Left + Width / 2;
        double bottom = Top + Height;
        Width = newW;
        Height = newH;
        Left = Math.Clamp(cx - newW / 2, wa.Left + 4, Math.Max(wa.Left + 4, wa.Right - newW - 4));
        Top = Math.Clamp(bottom - newH, wa.Top + 4, Math.Max(wa.Top + 4, wa.Bottom - newH - 4));
    }

    private void PositionWindow()
    {
        var wa = SystemParameters.WorkArea;
        if (!double.IsNaN(_cfg.X) && !double.IsNaN(_cfg.Y))
        {
            Left = Math.Clamp(_cfg.X, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            Top = Math.Clamp(_cfg.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        }
        else
        {
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Bottom - Height - 8;
        }
    }

    private void RootDragMove(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            try { DragMove(); } catch { /* 并发调用会抛异常，忽略 */ }
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

    private void MenuTopmostClick(object? sender, RoutedEventArgs e)
    {
        Topmost = _menuTopmost.IsChecked;
        _cfg.Topmost = Topmost;
        _cfg.Save();
    }

    private void MenuAutoStartClick(object sender, RoutedEventArgs e)
    {
        SetAutoStart(_menuAutoStart.IsChecked);
        _cfg.AutoStart = _menuAutoStart.IsChecked;
        _cfg.Save();
    }

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
        // 菜单勾选状态与配置对齐
        _menuTopmost.IsChecked = _cfg.Topmost;
        _menuAutoStart.IsChecked = AutoStartEnabled();
        if (_cfg.AutoStart != _menuAutoStart.IsChecked)
        {
            _cfg.AutoStart = _menuAutoStart.IsChecked;
            _cfg.Save();
        }
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
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;

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
            X = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()?.Left ?? X;
            Y = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()?.Top ?? Y;
            File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
