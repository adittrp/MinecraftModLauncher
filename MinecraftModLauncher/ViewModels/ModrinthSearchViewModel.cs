using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        private readonly Func<string?> _getGameVersion;
        private readonly Dictionary<string, Func<ModrinthSearchHit, Task>> _installHandlers;

        [ObservableProperty]
        private string _searchQuery = "";

        [ObservableProperty] private string _selectedProjectType = "mod";

        [ObservableProperty] private string? _selectedLoader;

        [ObservableProperty] private string _sortIndex = "relevance";

        [ObservableProperty] private string _statusMessage = "";

        [ObservableProperty] private bool _isBusy;

        public ObservableCollection<ModrinthSearchHit> Results { get; } = new();
        public ObservableCollection<FilterOptionViewModel> CategoryFilterOptions { get; } = new();

        public List<string> ProjectTypes { get;  } = new() { "mod", "modpack", "resourcepack", "shader", "datapack" };
        public List<string> AvailableLoaders { get; } = new() { "fabric", "forge", "quilt", "neoforge" };

        public ModrinthSearchViewModel(ModrinthService modrinthService, Func<string?> getGameVersion,
            Dictionary<string, Func<ModrinthSearchHit, Task>> installHandlers)
        {
            _modrinthService = modrinthService;
            _getGameVersion = getGameVersion;
            _installHandlers = installHandlers;

            // Fires exactly once, at app start, so results are already loaded by the
            // time the Library page is first opened.
            _ = LoadCategoryFilterOptions();
            _ = Search();
        }

        // Called by MainViewModel whenever the target instance changes (Home's gallery,
        // Library's "installing to" picker, or a new instance being created). Keeps the
        // Loader filter matched to whatever instance is currently selected, and always
        // re-searches — SelectedLoader's own change-hook alone would miss the case where
        // two instances share a loader but differ in game version.
        public void OnTargetInstanceChanged(string? loader) {
            SelectedLoader = loader;
            _ = Search();
        }

        partial void OnSelectedProjectTypeChanged(string value) {
            _ = LoadCategoryFilterOptions();
            _ = Search();
        }

        partial void OnSelectedLoaderChanged(string? value) => _ = Search();

        partial void OnSortIndexChanged(string value) => _ = Search();

        [RelayCommand]
        private void SetProjectType(string type) => SelectedProjectType = type;

        [RelayCommand]
        private void SetLoader(string? loader) => SelectedLoader = loader;

        [RelayCommand]
        private void SetSortIndex(string index) => SortIndex = index;

        private async Task LoadCategoryFilterOptions() {
            try {
                List<ModrinthCategory> categories = await _modrinthService.getCategories();
                List<string> checkedCategories = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();

                CategoryFilterOptions.Clear();
                foreach (ModrinthCategory category in categories.Where(c => c.ProjectType == SelectedProjectType)) {
                    var option = new FilterOptionViewModel(category.Name) { IsSelected = checkedCategories.Contains(category.Name) };
                    option.OnChanged = () => _ = Search();
                    CategoryFilterOptions.Add(option);
                }
            } catch { // dont care
            }
        }

        [RelayCommand]
        private async Task Search()
        {
            IsBusy = true;
            StatusMessage = "Searching...";
            try
            {
                bool loaderApplies = SelectedProjectType is "mod" or "modpack";
                List<string> checkedCategories = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();

                ModrinthSearchResult result = await _modrinthService.search(
                    SearchQuery,
                    projectType: SelectedProjectType,
                    gameVersion: _getGameVersion(),
                    loader: loaderApplies ? SelectedLoader : null,
                    categories: checkedCategories.Count > 0 ? checkedCategories : null,
                    index: SortIndex);

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
            if (!_installHandlers.TryGetValue(hit.ProjectType, out var handler))
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
