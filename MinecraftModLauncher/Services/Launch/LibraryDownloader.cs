using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftModLauncher.Services.Net;

namespace MinecraftModLauncher.Services.Launch;


// Downloads the client jar and the subset of a version's libraries
// that apply to the current OS/architecture.

public class LibraryDownloader
{
    private readonly HttpDownloader _downloader;

    public LibraryDownloader(HttpDownloader downloader)
    {
        _downloader = downloader;
    }
    
    public Task DownloadClientJar(JsonElement versionMeta, string versionsDir, string versionId) {
        string url = versionMeta
            .GetProperty("downloads")
            .GetProperty("client")
            .GetProperty("url")
            .GetString()!;

        string destPath = Path.Combine(versionsDir, versionId, $"{versionId}.jar");
        return _downloader.DownloadFile(url, destPath);
    }
    
    // Downloads every applicable library, returning the classpath paths.
    public async Task<List<string>> DownloadLibraries(JsonElement versionMeta, string librariesDir) {
        var classpathEntries = new List<string>();
        var downloadTasks = new List<Task>();
        JsonElement libraries = versionMeta.GetProperty("libraries");

        foreach (JsonElement lib in libraries.EnumerateArray()) {
            if (!ShouldIncludeLibrary(lib))
                continue;

            JsonElement downloads = lib.GetProperty("downloads");

            if (downloads.TryGetProperty("artifact", out JsonElement artifact)) {
                string url = artifact.GetProperty("url").GetString()!;
                string relativePath = artifact.GetProperty("path").GetString()!;
                string fullPath = Path.Combine(librariesDir,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                classpathEntries.Add(fullPath);
                downloadTasks.Add(_downloader.DownloadFile(url, fullPath));
            }
        }

        await Task.WhenAll(downloadTasks);
        return classpathEntries;
    }
    
    private bool ShouldIncludeLibrary(JsonElement lib) {
        if (!lib.TryGetProperty("rules", out JsonElement rules))
            return true;

        bool allowed = false;

        foreach (JsonElement rule in rules.EnumerateArray()) {
            string action = rule.GetProperty("action").GetString()!;

            if (rule.TryGetProperty("os", out JsonElement os)) {
                string osName = os.GetProperty("name").GetString()!;
                string currentOS = GetCurrentOsName();

                if (osName == currentOS)
                    allowed = action == "allow";
            } else {
                allowed = action == "allow";
            }
        }

        return allowed;
    }
    
    private string GetCurrentOsName() {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "linux";
    }
}