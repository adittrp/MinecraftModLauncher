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
    
    // Downloads every applicable library from a vanilla version meta, returning the classpath paths.
    public Task<List<string>> DownloadLibraries(JsonElement versionMeta, string librariesDir) =>
        DownloadLibrariesFromArray(versionMeta.GetProperty("libraries"), librariesDir);

    // Downloads every applicable library from a raw libraries array — used for both
    // vanilla version metas and mod-loader profiles (Fabric/Quilt), which list their
    // libraries as Maven {name, url} coordinates rather than vanilla's downloads.artifact.
    public async Task<List<string>> DownloadLibrariesFromArray(JsonElement libraries, string librariesDir) {
        var classpathEntries = new List<string>();
        var downloadTasks = new List<Task>();

        foreach (JsonElement lib in libraries.EnumerateArray()) {
            if (!ShouldIncludeLibrary(lib))
                continue;

            (string url, string relativePath)? resolved = ResolveLibrary(lib);
            if (resolved is not { } library) continue;

            string fullPath = Path.Combine(librariesDir,
                library.relativePath.Replace('/', Path.DirectorySeparatorChar));

            classpathEntries.Add(fullPath);
            downloadTasks.Add(_downloader.DownloadFile(library.url, fullPath));
        }

        await Task.WhenAll(downloadTasks);
        return classpathEntries;
    }

    private (string url, string relativePath)? ResolveLibrary(JsonElement lib) {
        // Vanilla shape: downloads.artifact.{url,path}
        if (lib.TryGetProperty("downloads", out JsonElement downloads) &&
            downloads.TryGetProperty("artifact", out JsonElement artifact)) {
            return (artifact.GetProperty("url").GetString()!, artifact.GetProperty("path").GetString()!);
        }

        // Fabric/Quilt shape: Maven coordinate name + repo base url
        if (lib.TryGetProperty("name", out JsonElement nameProp)) {
            string mavenCoordinate = nameProp.GetString()!;
            string baseUrl = lib.TryGetProperty("url", out JsonElement urlProp)
                ? urlProp.GetString()!
                : "https://maven.fabricmc.net/";

            string relativePath = MavenCoordinateToPath(mavenCoordinate);
            return (baseUrl.TrimEnd('/') + "/" + relativePath, relativePath);
        }

        return null;
    }

    // "group.id:artifact:version[:classifier]" -> "group/id/artifact/version/artifact-version[-classifier].jar"
    private static string MavenCoordinateToPath(string coordinate) {
        string[] parts = coordinate.Split(':');
        string group = parts[0].Replace('.', '/');
        string artifact = parts[1];
        string version = parts[2];
        string classifierSuffix = parts.Length > 3 ? $"-{parts[3]}" : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifierSuffix}.jar";
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