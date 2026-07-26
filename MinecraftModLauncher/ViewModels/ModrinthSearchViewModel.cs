using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftModLauncher.Models.Modrinth;
using MinecraftModLauncher.Services;

namespace MinecraftModLauncher.ViewModels
{
    public partial class ModrinthSearchViewModel : ViewModelBase
    {
        private readonly ModrinthService _modrinthService;
        private readonly Func<string> _getGameVersion;
        private readonly Func<string?> _getLoader;
        private readonly Dictionary<string, Func<ModrinthSearchHit, Task>> _installHandlers;
        
        [ObservableProperty]
        private string _searchQuery = "";

        [ObservableProperty] private string _selectedProjectType = "mod";
        
        [ObservableProperty] private string _statusMessage = "";

        [ObservableProperty] private bool _isBusy;

        public ObservableCollection<ModrinthSearchHit> Results { get; } = new();
        
        public List<string> ProjectTypes { get;  } = new() { "mod", "modpack", "resourcepack", "shader", "datapack" };

        public ModrinthSearchViewModel(ModrinthService modrinthService, Func<string> getGameVersion,
            Func<string?> getLoader, Dictionary<string, Func<ModrinthSearchHit, Task>> installHandlers)
        {
            _modrinthService = modrinthService;
            _getGameVersion = getGameVersion;
            _getLoader = getLoader;
            _installHandlers = installHandlers;
        }

        [RelayCommand]
        private async Task Search()
        {
            IsBusy = true;
            StatusMessage = "Searching...";
            try
            {
                bool loaderApplies = SelectedProjectType is "mod" or "modpack";

                ModrinthSearchResult result = await _modrinthService.search(
                    SearchQuery,
                    projectType: SelectedProjectType,
                    gameVersion: _getGameVersion(),
                    loader: loaderApplies ? _getLoader() : null);

                Results.Clear();
                foreach (var hit in result.Hits) Results.Add(hit);

                StatusMessage = $"{result.TotalHits} results found";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task Install(ModrinthSearchHit hit)
        {
            if (_installHandlers.TryGetValue(hit.ProjectType, out var handler))
            {
                StatusMessage = $"Cannot install {hit.ProjectType}s yet";
                return;
            }
            
            StatusMessage = $"Installing {hit.Title}...";
            try
            {
                await handler(hit);
                StatusMessage = $"{hit.Title} installed successfully!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to install {hit.Title}: {ex.Message}";
            }
        }
    }
}