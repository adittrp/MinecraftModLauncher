using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MinecraftModLauncher.Models;
using MinecraftModLauncher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftModLauncher.Models.Modrinth;
using MinecraftModLauncher.Services.Launch;

namespace MinecraftModLauncher.ViewModels {
    public partial class MainViewModel : ViewModelBase {
        [ObservableProperty]
        private string _greeting = "Click the button below to get started!";
        
        [ObservableProperty]
        private ViewModelBase _currentPage;

        private ConsoleViewModel ConsolePage { get; }
        private HomeViewModel HomePage { get; }
        private LibraryViewModel LibraryPage { get; }
        private SettingsViewModel SettingsPage { get; }

        [ObservableProperty]
        private string _currentPageTag = "Home";
        public bool IsHome => CurrentPageTag == "Home";
        public bool IsLibrary => CurrentPageTag == "Library";
        public bool IsConsole => CurrentPageTag == "Console";
        public bool IsSettings => CurrentPageTag == "Settings";

        [ObservableProperty]
        private MinecraftAccount? _account;
        private readonly AccountStore _accountStore;

        [ObservableProperty]
        private string _modSearchQuery = "";

        [ObservableProperty]
        private ObservableCollection<ModrinthSearchHit> _modSearchResults = new();

        [ObservableProperty]
        private string _userCode = "";

        [ObservableProperty]
        private string _verificationUrl = "";

        private string launcherRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinecraftModLauncher");

        private static readonly HttpClient _httpClient = new() {
            Timeout = TimeSpan.FromSeconds(60)
        };
        private readonly MicrosoftAuthService _authService = new();
        private readonly ModrinthService _modrinthService = new();
        private readonly InstanceService _instanceService;
        private readonly JavaService _javaService;
        private readonly VersionManifestService _manifestService;
        private readonly MinecraftLaunchService _launchService;

        public ObservableCollection<Instance> Instances { get; } = new();
        public ObservableCollection<InstalledMod> InstanceMods { get; } = new();

        // Instances after the active Version/Loader/Category filters and date sort;
        // this is what the Home gallery binds to instead of Instances directly.
        public ObservableCollection<Instance> FilteredInstances { get; } = new();

        public ObservableCollection<FilterOptionViewModel> VersionFilterOptions { get; } = new();
        public ObservableCollection<FilterOptionViewModel> LoaderFilterOptions { get; } = new();
        public ObservableCollection<FilterOptionViewModel> CategoryFilterOptions { get; } = new();

        [ObservableProperty] private bool _dateSortDescending = true;

        [ObservableProperty]
        private Instance? _selectedInstance;

        [ObservableProperty] private bool _isViewingInstance;

        // Instance creation form
        [ObservableProperty] private string _newInstanceName = "";
        [ObservableProperty] private string _newInstanceDescription = "";
        [ObservableProperty] private string _newInstanceIconUrl;
        [ObservableProperty] private string _newInstanceGameVersion;
        [ObservableProperty] private string _newInstanceLoader;
        [ObservableProperty] private bool _isCreatingInstance;
        [ObservableProperty] private string _createInstanceError = "";

        public ObservableCollection<string> AvailableGameVersions { get; } = new();
        public List<string> AvailableLoaders { get; } = new() { "fabric", "forge", "quilt", "neoforge" };

        // Modrinth's real modpack category slugs, fetched once. Instance.Categories
        // is drawn from this same list, so it lines up with Library's future
        // category filtering with no separate/invented taxonomy.
        public ObservableCollection<string> AvailableModpackCategories { get; } = new();
        public ObservableCollection<FilterOptionViewModel> NewInstanceCategoryOptions { get; } = new();

        public ModrinthSearchViewModel ModrinthSearch { get; }

        public MainViewModel() {
            ConsolePage = new ConsoleViewModel(this);
            HomePage = new HomeViewModel(this);
            LibraryPage = new LibraryViewModel(this);
            SettingsPage = new SettingsViewModel();

            _manifestService = new VersionManifestService(_httpClient, launcherRoot);
            _javaService = new JavaService(launcherRoot);
            _instanceService = new InstanceService(launcherRoot);
            _launchService = new MinecraftLaunchService(launcherRoot, _httpClient, _javaService);
            _accountStore = new AccountStore(launcherRoot);
            _ = LoadInstances();
            _ = LoadAvailableGameVersions();
            _ = LoadAvailableModpackCategories();
            _ = RestoreSession();

            ModrinthSearch = new ModrinthSearchViewModel(
                _modrinthService,
                getGameVersion: () => SelectedInstance?.GameVersion,
                installHandlers: new() {
                    ["mod"] = InstallMod,
                    ["modpack"] = InstallModpack
                    // add resourcepacks and shaders here as well
                });

            _currentPage = new HomeViewModel(this);
        }

        partial void OnCurrentPageTagChanged(string value) {
            OnPropertyChanged(nameof(IsHome));
            OnPropertyChanged(nameof(IsLibrary));
            OnPropertyChanged(nameof(IsConsole));
            OnPropertyChanged(nameof(IsSettings));
        }

        partial void OnSelectedInstanceChanged(Instance? value) {
            ModrinthSearch.OnTargetInstanceChanged(value?.Loader);
        }

        [RelayCommand]
        private void Navigate(string destination) {
            if (CurrentPageTag == destination)
                return;

            CurrentPage = destination switch {
                "Home" => HomePage,
                "Library" => LibraryPage,
                "Console" => ConsolePage,
                "Settings" => SettingsPage,
                _ => HomePage
            };

            CurrentPageTag = destination;
        }

        private async Task RestoreSession() {
            MinecraftAccount? saved = await _accountStore.Load();
            if (saved == null)
                return;

            if (!saved.IsExpired) {
                Account = saved;
                Greeting = $"Signed in as {saved.Username}";
                return;
            }

            if (string.IsNullOrEmpty(saved.RefreshToken))
                return;

            try {
                Account = await _authService.authenticateWithRefreshToken(
                    saved.RefreshToken,
                    status => Greeting = status);
                await _accountStore.Save(Account);
                Greeting = $"Signed in as {Account.Username}";
            } catch (Exception e) {
                Greeting = $"Refresh failed: {e.Message}";
            }
        }

        [RelayCommand]
        private async Task LoadAvailableGameVersions() {
            try {
                JsonElement manifest = await _manifestService.FetchVersionManifest();
                AvailableGameVersions.Clear();
                foreach (JsonElement version in manifest.GetProperty("versions").EnumerateArray()) {
                    if (version.GetProperty("type").GetString() == "release") {
                        AvailableGameVersions.Add(version.GetProperty("id").GetString()!);
                    }
                }
            } catch { // dont care
            }
        }

        [RelayCommand]
        private async Task LoadAvailableModpackCategories() {
            try {
                List<ModrinthCategory> categories = await _modrinthService.getCategories();
                AvailableModpackCategories.Clear();
                foreach (ModrinthCategory category in categories.Where(c => c.ProjectType == "modpack"))
                    AvailableModpackCategories.Add(category.Name);
            } catch { // dont care
            }
        }

        partial void OnDateSortDescendingChanged(bool value) => RefreshFilteredInstances();

        // Rebuilds the Version/Loader/Category filter option lists (with fresh counts)
        // from the current Instances, preserving whatever was already checked, then
        // reapplies filtering/sorting. Call whenever Instances changes.
        private void RebuildFilterOptions() {
            List<string> checkedVersions = VersionFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            List<string> checkedLoaders = LoaderFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            List<string> checkedCategories = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();

            VersionFilterOptions.Clear();
            foreach (var group in Instances.GroupBy(i => i.GameVersion).OrderBy(g => g.Key)) {
                var option = new FilterOptionViewModel(group.Key, group.Count()) { IsSelected = checkedVersions.Contains(group.Key) };
                option.OnChanged = RefreshFilteredInstances;
                VersionFilterOptions.Add(option);
            }

            LoaderFilterOptions.Clear();
            foreach (var group in Instances.GroupBy(i => i.Loader).OrderBy(g => g.Key)) {
                var option = new FilterOptionViewModel(group.Key, group.Count()) { IsSelected = checkedLoaders.Contains(group.Key) };
                option.OnChanged = RefreshFilteredInstances;
                LoaderFilterOptions.Add(option);
            }

            CategoryFilterOptions.Clear();
            var categoryGroups = Instances
                .SelectMany(i => i.Categories is { Count: > 0 } cats ? cats : new List<string> { "Uncategorized" })
                .GroupBy(c => c)
                .OrderBy(g => g.Key);
            foreach (var group in categoryGroups) {
                var option = new FilterOptionViewModel(group.Key, group.Count()) { IsSelected = checkedCategories.Contains(group.Key) };
                option.OnChanged = RefreshFilteredInstances;
                CategoryFilterOptions.Add(option);
            }

            RefreshFilteredInstances();
        }

        // Applies the current Version/Loader/Category filters and date sort to
        // Instances, writing the result into FilteredInstances.
        private void RefreshFilteredInstances() {
            List<string> checkedVersions = VersionFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            List<string> checkedLoaders = LoaderFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            List<string> checkedCategories = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();

            IEnumerable<Instance> query = Instances;

            if (checkedVersions.Count > 0)
                query = query.Where(i => checkedVersions.Contains(i.GameVersion));

            if (checkedLoaders.Count > 0)
                query = query.Where(i => checkedLoaders.Contains(i.Loader));

            if (checkedCategories.Count > 0)
                query = query.Where(i => (i.Categories is { Count: > 0 } cats ? cats : new List<string> { "Uncategorized" })
                    .Any(checkedCategories.Contains));

            query = DateSortDescending
                ? query.OrderByDescending(i => i.UpdatedAt)
                : query.OrderBy(i => i.UpdatedAt);

            FilteredInstances.Clear();
            foreach (Instance instance in query)
                FilteredInstances.Add(instance);
        }

        [RelayCommand]
        private void SortByDateNewest() => DateSortDescending = true;

        [RelayCommand]
        private void SortByDateOldest() => DateSortDescending = false;

        [RelayCommand]
        private void BeginCreateInstance() {
            NewInstanceName = "";
            NewInstanceDescription = "";
            NewInstanceIconUrl = null;
            NewInstanceGameVersion = AvailableGameVersions.Count > 0 ? AvailableGameVersions[0] : null;
            NewInstanceLoader = AvailableLoaders[0];
            CreateInstanceError = "";

            NewInstanceCategoryOptions.Clear();
            foreach (string category in AvailableModpackCategories)
                NewInstanceCategoryOptions.Add(new FilterOptionViewModel(category));

            IsCreatingInstance = true;
        }

        [RelayCommand]
        private void CancelCreateInstance() {
            IsCreatingInstance = false;
        }

        [RelayCommand]
        private async Task PickInstanceIcon() {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                return;

            IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                Title = "Choose instance thumbnail",
                AllowMultiple = false,
                FileTypeFilter = new[] {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } }
                }
            });

            if (files.Count > 0)
                NewInstanceIconUrl = files[0].TryGetLocalPath();
        }

        [RelayCommand]
        private async Task ConfirmCreateInstance() {
            if (string.IsNullOrWhiteSpace(NewInstanceName)) {
                CreateInstanceError = "Name is required";
                return;
            }

            if (Instances.Any(i => i.Name == NewInstanceName)) {
                CreateInstanceError = "Instance name already exists";
                return;
            }

            List<string> selectedCategories = NewInstanceCategoryOptions
                .Where(o => o.IsSelected)
                .Select(o => o.Value)
                .ToList();

            Instance created = await _instanceService.createInstance(NewInstanceName, NewInstanceIconUrl,
                NewInstanceGameVersion, NewInstanceLoader!, selectedCategories, NewInstanceDescription);

            Instances.Add(created);
            IsCreatingInstance = false;

            RebuildFilterOptions();
            SelectInstance(created);
        }

        [RelayCommand]
        private async Task LoadInstances() {
            Instances.Clear();
            foreach (var instance in await _instanceService.loadAllInstances())
                Instances.Add(instance);

            RebuildFilterOptions();
        }

        [RelayCommand]
        private void SelectInstance(Instance? instance) {
            if (instance is null) return;

            SelectedInstance = instance;

            InstanceMods.Clear();
            foreach (var mod in instance.Mods)
                InstanceMods.Add(mod);
            IsViewingInstance = true;
        }

        [RelayCommand]
        private void BackToSearch() {
            IsViewingInstance = false;
        }

        private async Task InstallMod(ModrinthSearchHit hit) {
            if (SelectedInstance is not { } instance) {
                Greeting = "Please select an instance first";
                return;
            }

            List<ModrinthVersion> versions = await _modrinthService.getProjectVersions(hit.ProjectId, gameVersion: instance.GameVersion, loader: instance.Loader);
            if (versions.Count == 0) throw new Exception("No compatible versions found for this mod");

            // change default to the selected instance

            ModrinthVersion version = versions[0];
            string modsDir = _instanceService.getInstanceModsDir(instance.Name);
            await _modrinthService.downloadVersionFile(version, modsDir);

            var installedMod = new InstalledMod(
                hit.ProjectId, version.Id, hit.Title, hit.IconUrl,
                version.VersionNumber,
                version.Files.Find(f => f.Primary)?.Filename ?? version.Files[0].Filename,
                hit.ProjectType, DateTimeOffset.UtcNow);

            Instance updated = await _instanceService.addMod(instance, installedMod);

            SelectedInstance = updated;
            int idx = Instances.IndexOf(instance);
            if (idx >= 0) Instances[idx] = updated;
            RefreshFilteredInstances();

            InstanceMods.Clear();
            foreach (var mod in updated.Mods)
                InstanceMods.Add(mod);
        }

        private async Task InstallModpack(ModrinthSearchHit hit) {
            throw new NotImplementedException("Not implemented yet");
        }

        [RelayCommand]
        private async Task LaunchMinecraft() {
            if (SelectedInstance is not { } instance)
            {
                Greeting = "Please select an instance first";
                return;
            }

            try
            {
                await _launchService.LaunchAsync(
                    instance,
                    Account,
                    status => Greeting = status,
                    ConsolePage.AddLog);
            }
            catch (Exception ex)
            {
                Greeting = $"Failed to launch: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SignIn() {
            try {
                Account = await _authService.authenticateFullFlow(
                    status => Greeting = status,
                    (code, url) => {
                        UserCode = code;
                        VerificationUrl = url;
                    });
                await _accountStore.Save(Account);
            } catch (Exception ex) {
                Greeting = $"Sign-in failed: {ex.Message}";
            }
        }
    }
}
