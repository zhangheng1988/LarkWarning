using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LarkWarning;

/// <summary>
/// 设置窗口：从 Config 读当前值填进去，用户改完点保存写回 ini，再重启程序。
/// </summary>
public partial class SettingsWindow : Window
{
    // 宠物下拉的候选项：(显示名, 配置里写的 key)
    private static readonly List<(string Label, string Key)> Pets = new()
    {
        ("🐸 frog  蛤蟆",   "frog"),
        ("🐺 wolf  狼",     "wolf"),
        ("💩 poop  粑粑",   "poop"),
        ("🐻 bear  黑熊",   "bear"),
        ("🐷 pig   小猪",   "pig"),
        ("💀 skull 骷髅",   "skull"),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        LoadToUI();
    }

    /// <summary>把当前 Config 的值填到控件里。</summary>
    private void LoadToUI()
    {
        TxtPort.Text = Config.Port.ToString();

        // 宠物下拉
        FillPetCombo(CmbNormal, Config.NormalPet);
        FillPetCombo(CmbUrgent, Config.UrgentPet);

        // 外观
        SldSize.Value = Config.Size;
        LblSize.Text  = ((int)Config.Size).ToString();
        SldSize.ValueChanged += (_, _) => LblSize.Text = ((int)SldSize.Value).ToString();

        switch (Config.Corner.ToLower())
        {
            case "topleft":     RbTL.IsChecked = true; break;
            case "topright":    RbTR.IsChecked = true; break;
            case "bottomleft":  RbBL.IsChecked = true; break;
            case "center":      RbCenter.IsChecked = true; break;
            default:            RbBR.IsChecked = true; break;
        }

        // 热键
        TxtHotNormal.Text    = Config.HotkeyNormal;
        TxtHotUrgent.Text    = Config.HotkeyUrgent;
        TxtHotClear.Text     = Config.HotkeyClear;
        TxtHotSettings.Text  = Config.HotkeySettings;

        // 高级
        ChkSound.IsChecked    = Config.SoundOn;
        ChkAutoStart.IsChecked = Config.AutoStart;
    }

    private void FillPetCombo(WpfComboBox cmb, string currentKey)
    {
        foreach (var p in Pets) cmb.Items.Add(p.Label);
        // 找到当前选中项
        for (int i = 0; i < Pets.Count; i++)
        {
            if (Pets[i].Key.Equals(currentKey, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedIndex = i; break;
            }
        }
        if (cmb.SelectedItem == null) cmb.SelectedIndex = 0;
    }

    // ---- 按钮 ----

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // 把 UI 的值写回 Config
        if (int.TryParse(TxtPort.Text.Trim(), out var port)) Config.Port = port;
        if (CmbNormal.SelectedIndex >= 0 && CmbNormal.SelectedIndex < Pets.Count)
            Config.NormalPet = Pets[CmbNormal.SelectedIndex].Key;
        if (CmbUrgent.SelectedIndex >= 0 && CmbUrgent.SelectedIndex < Pets.Count)
            Config.UrgentPet = Pets[CmbUrgent.SelectedIndex].Key;

        Config.Size = SldSize.Value;
        Config.Corner =
            RbTL.IsChecked == true ? "topleft" :
            RbTR.IsChecked == true ? "topright" :
            RbBL.IsChecked == true ? "bottomleft" :
            RbCenter.IsChecked == true ? "center" : "bottomright";

        Config.HotkeyNormal   = TxtHotNormal.Text.Trim().ToLower();
        Config.HotkeyUrgent   = TxtHotUrgent.Text.Trim().ToLower();
        Config.HotkeyClear    = TxtHotClear.Text.Trim().ToLower();
        Config.HotkeySettings = TxtHotSettings.Text.Trim().ToLower();

        Config.SoundOn    = ChkSound.IsChecked == true;
        Config.AutoStart  = ChkAutoStart.IsChecked == true;

        Config.Save();   // 写盘

        System.Windows.MessageBox.Show("设置已保存，程序马上重启生效。", "OK",
                        MessageBoxButton.OK, MessageBoxImage.Information);

        // 重启自己
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("确定退出？预警功能会失效。", "确认",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
