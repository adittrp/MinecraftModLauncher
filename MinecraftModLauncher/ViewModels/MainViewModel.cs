using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32.SafeHandles;
using MinecraftModLauncher.Models;
using MinecraftModLauncher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MinecraftModLauncher.Models.Modrinth;

namespace MinecraftModLauncher.ViewModels {
    public partial class MainViewModel : ViewModelBase {
        [ObservableProperty]
        private string _greeting = "Click the button below to get started!";
        
        public ObservableCollection<string> GameLogs { get; } = new();
        
        [ObservableProperty]
        private MinecraftAccount? _account;
        
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
        private static readonly SemaphoreSlim _downloadSemaphore = new(10);
        private readonly MicrosoftAuthService _authService = new();
        private readonly ModrinthService _modrinthService = new();
        private readonly InstanceService _instanceService;
        private readonly JavaService _javaService;

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
            _javaService = new JavaService(launcherRoot);
            _instanceService = new InstanceService(launcherRoot);
            _ = LoadInstances();
            _ = LoadAvailableGameVersions();

            ModrinthSearch = new ModrinthSearchViewModel(
                _modrinthService,
                getGameVersion: () => SelectedInstance?.GameVersion ?? "1.21.1", // replace with real selected variables
                getLoader: () => SelectedInstance?.Loader ?? "fabric", // replace with real selected variables
                installHandlers: new()
                {
                    ["mod"] = InstallMod,
                    ["modpack"] = InstallModpack
                    // add resourcepacks and shaders here as well
                });
        }

        [RelayCommand]
        private async Task LoadAvailableGameVersions()
        {
            try
            {
                JsonElement manifest = await fetchVersionManifest();
                AvailableGameVersions.Clear();
                foreach (JsonElement version in manifest.GetProperty("versions").EnumerateArray())
                {
                    if (version.GetProperty("type").GetString() == "release")
                    {
                        AvailableGameVersions.Add(version.GetProperty("id").GetString()!);
                    }
                }
            } catch { // dont care
            }
        }

        [RelayCommand]
        private void BeginCreateInstance()
        {
            NewInstanceName = "";
            NewInstanceDescription = "";
            NewInstanceIconUrl = null;
            NewInstanceGameVersion = AvailableGameVersions.Count > 0 ? AvailableGameVersions[0] : null;
            NewInstanceLoader = AvailableLoaders[0];
            CreateInstanceError = "";
            IsCreatingInstance = true;
        }

        [RelayCommand]
        private void CancelCreateInstance()
        {
            IsCreatingInstance = false;
        }

        [RelayCommand]
        private async Task ConfirmCreateInstance()
        {
            if (string.IsNullOrWhiteSpace(NewInstanceName))
            {
                CreateInstanceError = "Name is required";
                return;
            }

            if (Instances.Any(i => i.Name == NewInstanceName))
            {
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
        private async Task LoadInstances()
        {
            Instances.Clear();
            foreach (var instance in await _instanceService.loadAllInstances())
                Instances.Add(instance);
        }

        [RelayCommand]
        private void SelectInstance(Instance? instance)
        {
            if (instance is null) return;
            
            SelectedInstance = instance;
            
            InstanceMods.Clear();
            foreach (var mod in instance.Mods)
                InstanceMods.Add(mod);
            IsViewingInstance = true;
        }

        [RelayCommand]
        private void BackToSearch()
        {
            IsViewingInstance = false;
        }

        private async Task InstallMod(ModrinthSearchHit hit)
        {
            if (SelectedInstance is not { } instance)
            {
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

        private async Task InstallModpack(ModrinthSearchHit hit)
        {
            
        }

        private async Task<JsonElement> fetchVersionManifest() {
            string cachePath = Path.Combine(launcherRoot, "cache", "version_manifest_v2.json");

            // Check and use cached metadata if it's recent enough
            if (File.Exists(cachePath)) {
                TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
                if (age.TotalHours < 1) {
                    string cached = await File.ReadAllTextAsync(cachePath);
                    return JsonDocument.Parse(cached).RootElement;
                }
            }

            string url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
            string json = await _httpClient.GetStringAsync(url);

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, json);

            return JsonDocument.Parse(json).RootElement;
        }

        private async Task<JsonElement> fetchVersionMetadata(string versionId) {
            // Check if there's catched metadata
            string cachePath = Path.Combine(launcherRoot, "cache", "versions", $"{versionId}.json");

            if (File.Exists(cachePath)) {
                string cached = await File.ReadAllTextAsync(cachePath);
                return JsonDocument.Parse(cached).RootElement;
            }

            JsonElement manifest = await fetchVersionManifest();
            JsonElement versions = manifest.GetProperty("versions");

            foreach (JsonElement version in versions.EnumerateArray()) {
                if (version.GetProperty("id").GetString() == versionId) {
                    string metadataUrl = version.GetProperty("url").GetString()!;
                    string json = await _httpClient.GetStringAsync(metadataUrl);

                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllTextAsync(cachePath, json);

                    return JsonDocument.Parse(json).RootElement;
                }
            }

            throw new Exception($"Version {versionId} not found in manifest");
        }

        private async Task downloadFile(string url, string destPath) {
            if (File.Exists(destPath)) return;

            await _downloadSemaphore.WaitAsync();
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using FileStream fileStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);
            } catch (Exception ex) {
                if (File.Exists(destPath)) File.Delete(destPath);

                throw new Exception($"Failed to download {url}: {ex.Message}", ex);
            } finally {
                _downloadSemaphore.Release();
            }
        }

        private async Task downloadClientJar(JsonElement versionMeta, string versionsDir, string versionId) {
            string url = versionMeta
                .GetProperty("downloads")
                .GetProperty("client")
                .GetProperty("url")
                .GetString()!;

            string destPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
            await downloadFile(url, destPath);
        }

        private async Task<List<string>> downloadLibraries(JsonElement versionMeta, string librariesDir) {
            var classpathEntries = new List<string>();
            var downloadTasks = new List<Task>();
            JsonElement libraries = versionMeta.GetProperty("libraries");

            foreach (JsonElement lib in libraries.EnumerateArray()) {
                if (!shouldIncludeLibrary(lib))
                    continue;

                JsonElement downloads = lib.GetProperty("downloads");

                if (downloads.TryGetProperty("artifact", out JsonElement artifact)) {
                    string url = artifact.GetProperty("url").GetString()!;
                    string relativePath = artifact.GetProperty("path").GetString()!;
                    string fullPath = Path.Combine(librariesDir,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));

                    classpathEntries.Add(fullPath);
                    downloadTasks.Add(downloadFile(url, fullPath));
                }
            }

            await Task.WhenAll(downloadTasks);
            return classpathEntries;
        }

        private bool shouldIncludeLibrary(JsonElement lib) {
            if (!lib.TryGetProperty("rules", out JsonElement rules))
                return true;

            bool allowed = false;

            foreach (JsonElement rule in rules.EnumerateArray()) {
                string action = rule.GetProperty("action").GetString()!;

                if (rule.TryGetProperty("os", out JsonElement os)) {
                    string osName = os.GetProperty("name").GetString()!;
                    string currentOS = getCurrentOsName();

                    if (osName == currentOS)
                        allowed = action == "allow";
                } else {
                    allowed = action == "allow";
                }
            }

            return allowed;
        }

        private string getCurrentOsName() {
            if (OperatingSystem.IsWindows()) return "windows";
            if (OperatingSystem.IsMacOS()) return "osx";
            return "linux";
        }


        private void launchGame(
            string javaPath,
            string versionId,
            JsonElement versionMeta,
            List<string> libraryPaths,
            string clientJarPath,
            string gameDirPath,
            string assetsDirPath
        ) {
            string classPathSeparator = OperatingSystem.IsWindows() ? ";" : ":";

            var allJars = new List<string>(libraryPaths) { clientJarPath };
            string classpath = string.Join(classPathSeparator, allJars);

            string mainClass = versionMeta.GetProperty("mainClass").GetString()!;

            string assetIndex = versionMeta
                .GetProperty("assetIndex")
                .GetProperty("id")
                .GetString()!;

            var startInfo = new ProcessStartInfo {
                FileName = javaPath,
                WorkingDirectory = gameDirPath,
                UseShellExecute = false, 
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            
            
            
            // JVM arguments
            startInfo.ArgumentList.Add("-Xmx2G");
            startInfo.ArgumentList.Add("-Xms512M");
            startInfo.ArgumentList.Add($"-Djava.library.path={Path.Combine(gameDirPath, "natives")}");
            startInfo.ArgumentList.Add("-cp");
            startInfo.ArgumentList.Add(classpath);
            startInfo.ArgumentList.Add(mainClass);

            // Game arguments
            startInfo.ArgumentList.Add("--username");
            startInfo.ArgumentList.Add(Account?.Username ?? "Player");
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(versionId);
            startInfo.ArgumentList.Add("--gameDir");
            startInfo.ArgumentList.Add(gameDirPath);
            startInfo.ArgumentList.Add("--assetsDir");
            startInfo.ArgumentList.Add(assetsDirPath);
            startInfo.ArgumentList.Add("--assetIndex");
            startInfo.ArgumentList.Add(assetIndex);
            startInfo.ArgumentList.Add("--uuid");
            startInfo.ArgumentList.Add(Account?.Uuid ?? Guid.NewGuid().ToString("N"));
            startInfo.ArgumentList.Add("--accessToken");
            startInfo.ArgumentList.Add(Account?.AccessToken ?? "0");
            startInfo.ArgumentList.Add("--userType");
            startInfo.ArgumentList.Add(Account != null ? "msa" : "legacy");
            
            Process process = new Process{StartInfo = startInfo};
            
            process.OutputDataReceived += (sender, e) =>
            {
                if(!string.IsNullOrEmpty(e.Data))UpdateUiLogs(e.Data);
            };
            
            process.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data)) UpdateUiLogs($"[ERROR] {e.Data}");
            };
            
            process.Start();
            
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        [RelayCommand]
        private async Task launchMinecraft() {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string launcherRoot = Path.Combine(appData, "MinecraftModLauncher");
            string versionId = "1.21.1";

            Greeting = "Fetching version metadata";
            JsonElement versionMeta = await fetchVersionMetadata(versionId);

            Greeting = "Downloading client";
            string versionsDir = Path.Combine(launcherRoot, "versions");
            string clientJarPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
            await downloadClientJar(versionMeta, versionsDir, versionId);

            Greeting = "Downloading libraries";
            string librariesDir = Path.Combine(launcherRoot, "libraries");
            List<string> libraryPaths = await downloadLibraries(versionMeta, librariesDir);

            Greeting = "Checking java runtime";
            JavaRequirement requirement = _javaService.getRequiredJavaVersion(versionMeta);
            string javaPath;
            try
            {
                javaPath = await _javaService.ensureJavaRuntime(requirement, status => Greeting = status);
            }
            catch (Exception ex)
            {
                Greeting = $"Failed to download Java runtime: {ex.Message}";
                return;
            }

            Greeting = "Launching";
            string gameDir = Path.Combine(launcherRoot, "instances", "default", ".minecraft");
            string assetsDir = Path.Combine(launcherRoot, "assets");
            Directory.CreateDirectory(gameDir);
            
            
            launchGame(
                    javaPath,
                    versionId,
                    versionMeta,
                    libraryPaths,
                    clientJarPath,
                    gameDir,
                    assetsDir);
            
            

            Greeting = "Launched game";
        }

        [RelayCommand]
        private async Task signIn() {
            try {
                Account = await _authService.authenticateFullFlow(
                    status => Greeting = status,
                    (code, url) => {
                        UserCode = code;
                        VerificationUrl = url;
                    });
            } catch (Exception ex) {
                Greeting = $"Sign-in failed: {ex.Message}";
            }
        }

        private void UpdateUiLogs(string message)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine(message);
                GameLogs.Add(message);
            });
        }
    }
}
