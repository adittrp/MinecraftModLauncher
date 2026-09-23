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

        public ConsoleViewModel ConsolePage { get; }
        public HomeViewModel HomePage { get; }
        public LibraryViewModel LibraryPage { get; }
        public SettingsViewModel SettingsPage { get; }

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
            _ = RestoreSession();

            ModrinthSearch = new ModrinthSearchViewModel(
                _modrinthService,
                getGameVersion: () => SelectedInstance?.GameVersion ?? "1.21.1", // replace with real selected variables
                getLoader: () => SelectedInstance?.Loader ?? "fabric", // replace with real selected variables
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
        private void BeginCreateInstance() {
            NewInstanceName = "";
            NewInstanceDescription = "";
            NewInstanceIconUrl = null;
            NewInstanceGameVersion = AvailableGameVersions.Count > 0 ? AvailableGameVersions[0] : null;
            NewInstanceLoader = AvailableLoaders[0];
            CreateInstanceError = "";
            IsCreatingInstance = true;
        }

        [RelayCommand]
        private void CancelCreateInstance() {
            IsCreatingInstance = false;
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

            Instance created = await _instanceService.createInstance(NewInstanceName, NewInstanceIconUrl,
                NewInstanceGameVersion, NewInstanceLoader!, NewInstanceDescription);

            Instances.Add(created);
            IsCreatingInstance = false;

            SelectInstance(created);
        }

        [RelayCommand]
        private async Task LoadInstances() {
            Instances.Clear();
            foreach (var instance in await _instanceService.loadAllInstances())
                Instances.Add(instance);
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
