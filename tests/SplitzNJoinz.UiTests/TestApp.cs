using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(SplitzNJoinz.UiTests.TestAppBuilder))]

namespace SplitzNJoinz.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
