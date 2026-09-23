using System;
using System.Collections.Generic;
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
    private readonly FabricLikeMetaService _fabricMetaService;
    private readonly FabricLikeMetaService _quiltMetaService;

    public MinecraftLaunchService(string launcherRoot, HttpClient httpClient, JavaService javaService)
    {
        _launcherRoot = launcherRoot;
        _versionManifestService = new VersionManifestService(httpClient, launcherRoot);
        _libraryDownloader = new LibraryDownloader(new HttpDownloader(httpClient));
        _javaService = javaService;
        _gameProcessLauncher = new GameProcessLauncher();
        _fabricMetaService = new FabricLikeMetaService(httpClient, "https://meta.fabricmc.net/v2");
        _quiltMetaService = new FabricLikeMetaService(httpClient, "https://meta.quiltmc.org/v3");
    }

    public async Task LaunchAsync(
        Instance instance,
        MinecraftAccount? account,
        Action<string> onStatus,
        Action<string> onLogLine)
    {
        string versionId = instance.GameVersion;
        string loader = instance.Loader?.ToLowerInvariant() ?? "";

        if (loader is "forge" or "neoforge") {
            throw new NotSupportedException(
                $"{instance.Loader} isn't supported yet — Forge/NeoForge need a full installer " +
                "(binary-patched client + processor pipeline), unlike Fabric/Quilt's plain " +
                "classpath+mainClass swap. Pick Fabric or Quilt for now.");
        }

        onStatus("Fetching version metadata...");
        JsonElement versionMeta = await _versionManifestService.FetchVersionMetadata(versionId);

        JsonElement? loaderProfile = null;
        string mainClass = versionMeta.GetProperty("mainClass").GetString()!;
        string launchVersionId = versionId;

        if (loader is "fabric" or "quilt") {
            onStatus($"Fetching {instance.Loader} loader metadata...");
            FabricLikeMetaService metaService = loader == "fabric" ? _fabricMetaService : _quiltMetaService;
            string loaderVersion = await metaService.GetLatestLoaderVersion(versionId);
            JsonElement profile = await metaService.GetProfile(versionId, loaderVersion);

            loaderProfile = profile;
            mainClass = profile.GetProperty("mainClass").GetString()!;
            launchVersionId = profile.TryGetProperty("id", out JsonElement idProp) ? idProp.GetString()! : versionId;
        }

        onStatus("Downloading client.");
        string versionsDir = Path.Combine(_launcherRoot, "versions");
        string clientJarPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
        await _libraryDownloader.DownloadClientJar(versionMeta, versionsDir, versionId);

        onStatus("Downloading libraries");
        string librariesDir = Path.Combine(_launcherRoot, "libraries");
        var libraryPaths = await _libraryDownloader.DownloadLibraries(versionMeta, librariesDir);

        if (loaderProfile is { } profileForLibraries) {
            onStatus($"Downloading {instance.Loader} libraries...");
            List<string> loaderLibraryPaths = await _libraryDownloader.DownloadLibrariesFromArray(
                profileForLibraries.GetProperty("libraries"), librariesDir);
            libraryPaths.AddRange(loaderLibraryPaths);
        }

        onStatus("Checking java runtime.");
        JavaRequirement requirement = _javaService.getRequiredJavaVersion(versionMeta);
        string javaPath = await _javaService.ensureJavaRuntime(requirement, onStatus);

        onStatus("Launching game.");
        string gameDir = Path.Combine(_launcherRoot, "instances", instance.Name);
        string assetsDir = Path.Combine(_launcherRoot, "assets");
        Directory.CreateDirectory(gameDir);

        _gameProcessLauncher.Launch(
            javaPath, launchVersionId, mainClass, versionMeta, libraryPaths, clientJarPath,
            gameDir, assetsDir, account, onLogLine);

        onStatus("Game launched!");
    }
}