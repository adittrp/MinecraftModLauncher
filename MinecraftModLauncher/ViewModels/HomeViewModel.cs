using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MinecraftModLauncher.ViewModels {
    public partial class HomeViewModel : ViewModelBase {
        public MainViewModel Main { get; }

        public HomeViewModel(MainViewModel main) {
            Main = main;
        }
    }
}
