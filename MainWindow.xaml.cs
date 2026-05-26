using System.Windows;
using SpectraGrab.ViewModels;

namespace SpectraGrab;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
