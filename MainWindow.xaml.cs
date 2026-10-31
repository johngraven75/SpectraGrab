using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpectraGrab.ViewModels;

namespace SpectraGrab;

public partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<string, int> SidebarTabMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = 0,
        ["New Download"] = 0,
        ["Capture"] = 1,
        ["Queue"] = 2,
        ["Crawler"] = 3,
        ["Library"] = 4,
        ["Settings"] = 5
    };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (MainTabControl is null)
        {
            SetStatus("Navigation could not initialize because the main tab control was not found.");
            return;
        }

        var wiredButtons = 0;
        foreach (var button in FindVisualChildren<Button>(this))
        {
            if (button.Content is not string label || !SidebarTabMap.ContainsKey(label))
            {
                continue;
            }

            button.Click -= SidebarButton_Click;
            button.Click += SidebarButton_Click;
            wiredButtons++;
        }

        if (DataContext is not MainViewModel { StatusMessage: var status }
            || !status.StartsWith("Integration configuration verification failed", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(wiredButtons == SidebarTabMap.Count
                ? "Navigation ready. HLS downloads use yt-dlp with FFmpeg fallback when required."
                : $"Navigation initialized with {wiredButtons} of {SidebarTabMap.Count} sidebar actions wired.");
        }
    }

    private void SidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string label })
        {
            return;
        }

        if (!SidebarTabMap.TryGetValue(label, out var tabIndex))
        {
            SetStatus($"No navigation target is configured for {label}.");
            return;
        }

        if (tabIndex < 0 || tabIndex >= MainTabControl.Items.Count)
        {
            SetStatus($"The {label} view is unavailable in this build.");
            return;
        }

        MainTabControl.SelectedIndex = tabIndex;
        MainTabControl.BringIntoView();
        SetStatus(label switch
        {
            "Home" => "Home opened.",
            "Library" => "Library and conversion tools opened.",
            _ => $"{label} opened."
        });
    }

    private void SetStatus(string message)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.StatusMessage = message;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
