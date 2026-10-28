using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LarkWarning;

/// <summary>
/// 配置读取/写入：从 config.ini 读，设置窗口里改完写回。
/// </summary>
public static class Config
{
    // ---- 网络 ----
    public static int Port { get; set; } = 17891;

    // ---- 宠物 ----
    public static string NormalPet { get; set; } = "frog";
    public static string UrgentPet { get; set; } = "skull";

    // ---- 外观 ----
    public static double Size { get; set; } = 120;
    public static double NormalDurationMs { get; set; } = 3000;
    public static double UrgentDurationMs { get; set; } = 6000;
    public static double ClearDurationMs { get; set; } = 1200;
    public static string Corner { get; set; } = "bottomright";   // 四角之一或 center

    // ---- 热键 ----
    public static string HotkeyNormal { get; set; } = "ctrl+alt+l";
    public static string HotkeyUrgent { get; set; } = "ctrl+alt+k";
    public static string HotkeyClear  { get; set; } = "ctrl+alt+j";
    public static string HotkeySettings { get; set; } = "ctrl+alt+shift+s";

    // ---- 高级 ----
    public static double CooldownMs { get; set; } = 2000;
    public static bool SoundOn { get; set; } = false;
    public static bool AutoStart { get; set; } = false;

    public static string IniPath =>
        Path.Combine(AppContext.BaseDirectory, "config.ini");

    public static void Load()
    {
        string path = IniPath;
        if (!File.Exists(path)) { Save(); return; }

        try
        {
            var d = ParseIni(File.ReadAllLines(path, Encoding.UTF8));

            Port               = GetInt(d, "Port", Port);
            NormalPet          = GetStr(d, "NormalPet", NormalPet);
            UrgentPet          = GetStr(d, "UrgentPet", UrgentPet);
            Size               = GetDouble(d, "Size", Size);
            NormalDurationMs   = GetDouble(d, "NormalDurationMs", NormalDurationMs);
            UrgentDurationMs   = GetDouble(d, "UrgentDurationMs", UrgentDurationMs);
            ClearDurationMs    = GetDouble(d, "ClearDurationMs", ClearDurationMs);
            Corner             = GetStr(d, "Corner", Corner);
            HotkeyNormal       = GetStr(d, "HotkeyNormal", HotkeyNormal);
            HotkeyUrgent       = GetStr(d, "HotkeyUrgent", HotkeyUrgent);
            HotkeyClear        = GetStr(d, "HotkeyClear", HotkeyClear);
            HotkeySettings     = GetStr(d, "HotkeySettings", HotkeySettings);
            CooldownMs         = GetDouble(d, "CooldownMs", CooldownMs);
            SoundOn            = GetBool(d, "SoundOn", SoundOn);
            AutoStart          = GetBool(d, "AutoStart", AutoStart);
        }
        catch { }
    }

    /// <summary>把当前所有配置写回 config.ini（带注释模板）。</summary>
    public static void Save()
    {
        try
        {
            string txt = $@"; ============================================================
;   工位预警软件 - 配置文件
;   一般不用手改！按隐藏热键 {HotkeySettings} 呼出设置窗口改。
; ============================================================

[网络]
; 广播端口，所有同事必须一致
Port = {Port}

[宠物]
; 可选: wolf / poop / bear / pig / frog / skull
NormalPet = {NormalPet}
UrgentPet = {UrgentPet}

[外观]
; 图标大小（像素）
Size = {Size}
; 普通预警停留时间（毫秒）
NormalDurationMs = {NormalDurationMs}
; 紧急预警停留时间（毫秒）
UrgentDurationMs = {UrgentDurationMs}
; 解除提示停留时间（毫秒）
ClearDurationMs = {ClearDurationMs}
; 出现位置: bottomright / bottomleft / topright / topleft / center
Corner = {Corner}

[快捷键]
; 格式: ctrl+alt+字母，支持 shift
HotkeyNormal = {HotkeyNormal}
HotkeyUrgent = {HotkeyUrgent}
HotkeyClear  = {HotkeyClear}
; 呼出设置窗口的隐藏热键（自己人知道就行）
HotkeySettings = {HotkeySettings}

[高级]
CooldownMs = {CooldownMs}
SoundOn = {(SoundOn ? "true" : "false")}
AutoStart = {(AutoStart ? "true" : "false")}
";
            File.WriteAllText(IniPath, txt, Encoding.UTF8);
        }
        catch { }
    }

    // ---- 内部解析 ----
    private static Dictionary<string, string> ParseIni(string[] lines)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }

    private static string GetStr(Dictionary<string,string> d, string k, string def)
        => d.TryGetValue(k, out var v) && v.Length > 0 ? v : def;
    private static int GetInt(Dictionary<string,string> d, string k, int def)
        => int.TryParse(d.GetValueOrDefault(k), out var v) ? v : def;
    private static double GetDouble(Dictionary<string,string> d, string k, double def)
        => double.TryParse(d.GetValueOrDefault(k), out var v) ? v : def;
    private static bool GetBool(Dictionary<string,string> d, string k, bool def)
    {
        string s = (d.GetValueOrDefault(k) ?? "").Trim().ToLower();
        return s switch { "true" or "1" or "yes" => true, "false" or "0" or "no" => false, _ => def };
    }
}
