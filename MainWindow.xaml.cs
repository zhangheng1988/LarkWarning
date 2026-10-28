using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace LarkWarning;

/// <summary>
/// 主窗口（彻底隐藏）：
///   - 注册 4 个全局热键：普通 / 紧急 / 解除 / 呼出设置窗口
///   - UDP 多网卡广播 + 监听
///   - 按"设置热键"弹出可视化设置窗口
///   - 无托盘、无任务栏图标、无任何可见痕迹
/// </summary>
public partial class MainWindow : Window
{
    // 协议：异或 0x5A 后传输，网管抓包看不到明文
    private const byte XorKey = 0x5A;

    // 四个热键 ID
    private const int HK_NORMAL = 0xC001;
    private const int HK_URGENT = 0xC002;
    private const int HK_CLEAR  = 0xC003;
    private const int HK_SETTINGS = 0xC004;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private DateTime _lastSend = DateTime.MinValue;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT    = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 读配置
        Config.Load();

        // 开机自启写注册表
        ApplyAutoStart(Config.AutoStart);

        // 彻底藏起来
        Hide();

        // 注册四个热键
        var helper = new WindowInteropHelper(this);
        var src = HwndSource.FromHwnd(helper.Handle);
        src?.AddHook(WndProc);

        TryRegister(helper.Handle, HK_NORMAL,   Config.HotkeyNormal);
        TryRegister(helper.Handle, HK_URGENT,   Config.HotkeyUrgent);
        TryRegister(helper.Handle, HK_CLEAR,    Config.HotkeyClear);
        TryRegister(helper.Handle, HK_SETTINGS, Config.HotkeySettings);

        // 启动 UDP
        StartUdp();
    }

    private void TryRegister(IntPtr hwnd, int id, string hotkey)
    {
        var (mods, vk) = ParseHotkey(hotkey);
        if (mods == 0 && vk == 0) return;   // 解析失败就跳过
        RegisterHotKey(hwnd, id, mods, vk);
    }

    // 解析 "ctrl+alt+shift+l" -> (mods位掩码, 虚拟键码)
    private static (uint mods, uint vk) ParseHotkey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (0, 0);
        var parts = s.ToLower().Split('+', StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => x.Trim()).ToArray();
        uint mods = 0;
        string keyPart = parts[^1];
        foreach (var p in parts[..^1])
        {
            if (p == "ctrl")  mods |= MOD_CONTROL;
            else if (p == "alt")   mods |= MOD_ALT;
            else if (p == "shift") mods |= MOD_SHIFT;
        }
        uint vk;
        if (keyPart.StartsWith("f") && int.TryParse(keyPart[1..], out var fn) && fn is >=1 and <=24)
            vk = (uint)(0x6F + fn - 1);   // VK_F1 = 0x70
        else if (keyPart.Length == 1)
            vk = char.ToUpper(keyPart[0]);
        else
            return (0, 0);
        return (mods, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();

        switch (id)
        {
            case HK_NORMAL:
                FireWithCooldown(() => Send("normal"));
                break;
            case HK_URGENT:
                FireWithCooldown(() => Send("urgent"));
                break;
            case HK_CLEAR:
                FireWithCooldown(() => Send("clear"));
                break;
            case HK_SETTINGS:
                // 呼出设置窗口（不冷却，随时可开）
                OpenSettings();
                break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    private void FireWithCooldown(Action act)
    {
        var now = DateTime.Now;
        if ((now - _lastSend).TotalMilliseconds < Config.CooldownMs) return;
        _lastSend = now;
        act();
        ShowPetInternal("self");   // 自己按了也弹一下当反馈
    }

    private void OpenSettings()
    {
        // 已经开着就别重复开
        foreach (Window w in System.Windows.Application.Current.Windows)
            if (w is SettingsWindow) { w.Activate(); return; }

        new SettingsWindow().Show();
    }

    // ==================== UDP ====================

    private void StartUdp()
    {
        _udp = new UdpClient();
        _udp.EnableBroadcast = true;
        _udp.ExclusiveAddressUse = false;
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Config.Port));

        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var res = await _udp.ReceiveAsync();
                    string level = Decode(res.Buffer);
                    if (level.Length > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ShowPetInternal(level);
                            if (Config.SoundOn) System.Media.SystemSounds.Beep.Play();
                        });
                    }
                }
            }
            catch { }
        }, _cts.Token);
    }

    private void Send(string level)
    {
        try
        {
            var bytes = Encode(level);
            // 全局广播
            _udp?.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Config.Port));
            // 每个网卡的子网广播再发一遍
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var u in ni.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (u.IPv4Mask == null) continue;
                    var bc = GetBroadcast(u.Address, u.IPv4Mask);
                    try { _udp?.Send(bytes, bytes.Length, new IPEndPoint(bc, Config.Port)); } catch { }
                }
            }
        }
        catch { }
    }

    private static IPAddress GetBroadcast(IPAddress ip, IPAddress mask)
    {
        var a = ip.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(a);
    }

    private static byte[] Encode(string level)
    {
        var b = Encoding.UTF8.GetBytes(level);
        for (int i = 0; i < b.Length; i++) b[i] ^= XorKey;
        return b;
    }

    private static string Decode(byte[] raw)
    {
        try
        {
            var b = (byte[])raw.Clone();
            for (int i = 0; i < b.Length; i++) b[i] ^= XorKey;
            var s = Encoding.UTF8.GetString(b).Trim();
            return s switch { "normal" or "urgent" or "clear" => s, _ => "" };
        }
        catch { return ""; }
    }

    // ==================== 弹宠物 ====================

    /// <summary>
    /// level: normal / urgent / clear / self（自己按的反馈，按普通样式）
    /// </summary>
    private void ShowPetInternal(string level)
    {
        if (level == "self") level = "normal";

        // 关旧的
        foreach (Window w in System.Windows.Application.Current.Windows.OfType<PetWindow>()) w.Close();

        double size = Config.Size;
        double duration = level switch
        {
            "urgent" => Config.UrgentDurationMs,
            "clear"  => Config.ClearDurationMs,
            _        => Config.NormalDurationMs,
        };

        var pet = new PetWindow(level, size, duration);

        // 四角定位
        var work = SystemParameters.WorkArea;
        pet.Left = Config.Corner.ToLower() switch
        {
            "topleft"     => work.Left + 20,
            "topright"    => work.Right - size - 20,
            "bottomleft"  => work.Left + 20,
            "center"      => work.Left + (work.Width - size) / 2,
            _             => work.Right - size - 20,   // bottomright 默认
        };
        pet.Top = Config.Corner.ToLower() switch
        {
            "topleft"    => work.Top + 20,
            "topright"   => work.Top + 20,
            "bottomleft" => work.Bottom - size - 20,
            "center"     => work.Top + (work.Height - size) / 2,
            _            => work.Bottom - size - 20,
        };

        pet.Show();
    }

    // ==================== 开机自启 ====================
    private static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            string exe = Environment.ProcessPath ?? "";
            if (enable) key.SetValue("LarkWarning", exe);
            else if (key.GetValue("LarkWarning") != null) key.DeleteValue("LarkWarning", false);
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        var h = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(h, HK_NORMAL);
        UnregisterHotKey(h, HK_URGENT);
        UnregisterHotKey(h, HK_CLEAR);
        UnregisterHotKey(h, HK_SETTINGS);
        _udp?.Close();
        base.OnClosed(e);
    }
}

/// <summary>
/// 宠物小窗：根据 level 决定颜色 / emoji / 闪烁频率。
/// </summary>
public class PetWindow : Window
{
    public PetWindow(string level, double size, double durationMs)
    {
        Width = size; Height = size;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;

        (System.Windows.Media.Color bg, string emoji) = level switch
        {
            "urgent" => (System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B), EmojiOf(Config.UrgentPet)),
            "clear"  => (System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60), "\u2705"),
            _        => (System.Windows.Media.Color.FromRgb(0x29, 0x80, 0xB9), EmojiOf(Config.NormalPet)),
        };

        var border = new System.Windows.Controls.Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = new SolidColorBrush(bg),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = emoji,
                FontSize = size * (level == "clear" ? 0.55 : 0.6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            }
        };
        Content = border;

        Loaded += (_, _) =>
        {
            var sb = new Storyboard();
            int repeat = level switch { "urgent" => 5, "clear" => 1, _ => 3 };
            var blink = new DoubleAnimation
            {
                From = 1.0, To = 0.3,
                Duration = new Duration(TimeSpan.FromMilliseconds(level == "urgent" ? 150 : 250)),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(repeat),
            };
            Storyboard.SetTarget(blink, this);
            Storyboard.SetTargetProperty(blink, new PropertyPath(OpacityProperty));
            sb.Children.Add(blink);

            var fade = new DoubleAnimation
            {
                To = 0.0,
                BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, durationMs - 400)),
                Duration = new Duration(TimeSpan.FromMilliseconds(400)),
            };
            Storyboard.SetTarget(fade, this);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            sb.Children.Add(fade);

            sb.Completed += (_, _) => Close();
            BeginStoryboard(sb);
        };
    }

    private static string EmojiOf(string pet) => (pet ?? "frog").ToLower() switch
    {
        "wolf"  => "\U0001F43A",
        "poop"  => "\U0001F4A9",
        "bear"  => "\U0001F43B",
        "pig"   => "\U0001F437",
        "frog"  => "\U0001F438",
        "skull" => "\U0001F480",
        _       => "\U0001F438",
    };
}
