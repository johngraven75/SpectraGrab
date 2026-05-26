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
        services.AddSingleton<IYtdlpService, YtdlpService>();
        services.AddSingleton<ICrawlerService, CrawlerService>();
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
