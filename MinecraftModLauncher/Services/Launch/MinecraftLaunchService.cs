using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftModLauncher.Models;
using MinecraftModLauncher.Services.Net;

namespace MinecraftModLauncher.Services.Launch;

// Coordinates launch process for the game instance.

// This is the entry point view models should call.

public class MinecraftLaunchService
{
    private readonly string _launcherRoot;
    private readonly VersionManifestService _versionManifestService;
    private readonly LibraryDownloader _libraryDownloader;
    private readonly GameProcessLauncher _gameProcessLauncher;
    private readonly JavaService _javaService;

    public MinecraftLaunchService(string launcherRoot, HttpClient httpClient, JavaService javaService)
    {
        _launcherRoot = launcherRoot;
        _versionManifestService = new VersionManifestService(httpClient, launcherRoot);
        _libraryDownloader = new LibraryDownloader(new HttpDownloader(httpClient));
        _javaService = javaService;
        _gameProcessLauncher = new GameProcessLauncher();
    }

    public async Task LaunchAsync(
        Instance instance,
        MinecraftAccount? account,
        Action<string> onStatus,
        Action<string> onLogLine)
    {
        string versionId = instance.GameVersion;
        
        onStatus("Fetching version metadata...");
        JsonElement versionMeta = await _versionManifestService.FetchVersionMetadata(versionId);

        onStatus("Downloading client.");
        string versionsDir = Path.Combine(_launcherRoot, "versions");
        string clientJarPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
        await _libraryDownloader.DownloadClientJar(versionMeta, versionsDir, versionId);
        
        onStatus("Downloading libraries");
        string librariesDir = Path.Combine(_launcherRoot, "libraries");
        var libraryPaths = await _libraryDownloader.DownloadLibraries(versionMeta, librariesDir);
        
        onStatus("Checking java runtime.");
        JavaRequirement requirement = _javaService.getRequiredJavaVersion(versionMeta);
        string javaPath = await _javaService.ensureJavaRuntime(requirement, onStatus);
        
        onStatus("Launching game.");
        string gameDir = Path.Combine(_launcherRoot, "instances", instance.Name);
        string assetsDir = Path.Combine(_launcherRoot, "assets");
        Directory.CreateDirectory(gameDir);

        _gameProcessLauncher.Launch(
            javaPath, versionId, versionMeta, libraryPaths, clientJarPath, 
            gameDir, assetsDir, account, onLogLine);
        
        onStatus("Game launched!");
    }
}