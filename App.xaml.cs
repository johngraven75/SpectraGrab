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
        services.AddSingleton<IStreamCaptureService, StreamCaptureService>();
        services.AddSingleton<IPersistentConfigService, PersistentConfigService>();
        services.AddSingleton<YtdlpService>();
        services.AddSingleton<HlsYtdlpService>();
        services.AddSingleton<CrawlerService>();
        services.AddSingleton<IHeadlessBrowserService, HeadlessBrowserService>();
        services.AddSingleton<ICrawlerService, HeadlessCrawlerService>();
        services.AddSingleton<AdultMediaYtdlpService>();
        services.AddSingleton<IAutomatedMediaService, AutomatedMediaService>();
        services.AddSingleton<IYtdlpService, CaptchaAwareYtdlpService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();
        string? configFailure = null;
        try
        {
            serviceProvider.GetRequiredService<IPersistentConfigService>().EnsureInitialized();
        }
        catch (Exception ex)
        {
            configFailure = $"Integration configuration verification failed: {ex.Message}";
        }

        var window = serviceProvider.GetRequiredService<MainWindow>();
        if (configFailure is not null && window.DataContext is MainViewModel viewModel)
        {
            viewModel.StatusMessage = configFailure;
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
