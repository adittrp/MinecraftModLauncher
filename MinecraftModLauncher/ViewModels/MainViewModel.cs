using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32.SafeHandles;
using MinecraftModLauncher.Models;
using MinecraftModLauncher.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftModLauncher.ViewModels {
    public partial class MainViewModel : ViewModelBase {
        [ObservableProperty]
        private string _greeting = "Click the button below to get started!";
        [ObservableProperty]
        private MinecraftAccount? _account;

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


        private async Task<JsonElement> fetchVersionManifest() {
            string cachePath = Path.Combine(launcherRoot, "cache", "version_manifest_v2.json");

            // Check and use cached metadata if its recent enough
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
            // Check if theres catched metadata
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

            Process.Start(startInfo);
        }

        // archived func
        //[RelayCommand]
        //private void createFileSystem(string instanceName) {
        //    string path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        //    string instanceGameDir = Path.Combine(launcherRoot, "instances", instanceName, ".minecraft");
        //    string assetsDir = Path.Combine(launcherRoot, "assets");
        //    string librariesDir = Path.Combine(launcherRoot, "libraries");
            
        //    string launcherConfig = Path.Combine(launcherRoot, "launcher_config.json");
            
        //    Directory.CreateDirectory(instanceGameDir);
        //    Directory.CreateDirectory(assetsDir);
        //    Directory.CreateDirectory(librariesDir);
            
        //    using FileStream fileStream = File.Create(launcherConfig);
            
        //    Console.WriteLine(launcherRoot);
        //}

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

            Greeting = "Launching";
            string gameDir = Path.Combine(launcherRoot, "instances", "default", ".minecraft");
            string assetsDir = Path.Combine(launcherRoot, "assets");
            Directory.CreateDirectory(gameDir);

            launchGame(
                // this assumes java is on PATH but we should have a path picker later
                "java",
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
    }
}
