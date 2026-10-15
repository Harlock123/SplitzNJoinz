using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SplitzNJoinz.Services;
using SplitzNJoinz.ViewModels;

namespace SplitzNJoinz;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = new MainWindowViewModel(new FileDialogs(window));
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
