using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpectraGrab.Services;
using SpectraGrab.ViewModels;

namespace SpectraGrab;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IToolLocator, ToolLocator>();
        services.AddSingleton<ISitePluginCatalog, SitePluginCatalog>();
        services.AddSingleton<IJDownloaderService, JDownloaderService>();
        services.AddSingleton<ICodecPackService, CodecPackService>();
        services.AddSingleton<ICodecProfileCatalog, CodecProfileCatalog>();
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<HlsYtdlpService>();
        services.AddSingleton<CrawlerService>();
        services.AddSingleton<IHeadlessBrowserService, HeadlessBrowserService>();
        services.AddSingleton<ICrawlerService, HeadlessCrawlerService>();
        services.AddSingleton<AdultMediaYtdlpService>();
        services.AddSingleton<IYtdlpService, CaptchaAwareYtdlpService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
