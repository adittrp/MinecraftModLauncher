using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MinecraftModLauncher.ViewModels;

namespace MinecraftModLauncher.Views {
    public partial class MainWindow : Window {
        public MainWindow() {
            AvaloniaXamlLoader.Load(this);

            DataContext = new MainViewModel();
        }
    }
}
