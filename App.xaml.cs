using System.Windows;

namespace LarkWarning;

/// <summary>
/// WPF 应用入口类。这里不需要写什么逻辑，真正的工作都在 MainWindow 里。
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        MainWindow = new MainWindow();
        MainWindow.Show();
        MainWindow.Hide();
    }
}
