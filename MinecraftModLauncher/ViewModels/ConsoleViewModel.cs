using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MinecraftModLauncher.ViewModels {
    public partial class ConsoleViewModel : ViewModelBase {
        public MainViewModel Main { get; }
        public ObservableCollection<string> GameLogs { get; } = new();

        public ConsoleViewModel(MainViewModel main) {
            Main = main;
        }

        public void AddLog(string message) {
            Dispatcher.UIThread.Post(() => GameLogs.Add(message));
        }
    }
}
