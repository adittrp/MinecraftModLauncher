using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;

namespace MinecraftModLauncher.ViewModels {
    public partial class MainViewModel : ViewModelBase {
        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";

        [RelayCommand]
        private void createFileSystem()
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string launcherRoot = Path.Combine(path, "MinecraftModLauncher");

            string instanceName = "Fabric_26.2";
            string instanceGameDir = Path.Combine(launcherRoot, "instances", instanceName, ".minecraft");
            string assetsDir = Path.Combine(launcherRoot, "assets");
            string librariesDir = Path.Combine(launcherRoot, "libraries");
            
            string launcher_config = Path.Combine(launcherRoot, "launcher_config.json");
            
            Directory.CreateDirectory(instanceGameDir);
            Directory.CreateDirectory(assetsDir);
            Directory.CreateDirectory(librariesDir);
            
            File.Create(launcher_config);
            
            Console.WriteLine(launcherRoot);
        }
    }
}
