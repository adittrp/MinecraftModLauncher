

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services;
public record JavaRequirement(string Component, int MajorVersion);

public class JavaService
{
    private const string RuntimeManifestUrl = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly SemaphoreSlim _downloadSemaphore = new(10);

    private readonly string _runtimesDir;
    
    public JavaService(string launcherRoot)
    {
        _runtimesDir = Path.Combine(launcherRoot, "runtimes");
    }
    
    // Get the required Java version for a given version
    public JavaRequirement getRequiredJavaVersion(JsonElement versionMeta)
    {
        if (versionMeta.TryGetProperty("javaVersion", out JsonElement javaVersion))
        {
            string component = javaVersion.GetProperty("component").GetString() ?? "jre-legacy";
            int majorVersion = javaVersion.GetProperty("majorVersion").GetInt32();
            return new JavaRequirement(component, majorVersion);
        }
        return new JavaRequirement("jre-legacy", 8);
    }
    
    // Ensure runtime exists locally, return path.

    public async Task<string> ensureJavaRuntime(JavaRequirement requirement, Action<string>? onStatus = null)
    {
        string componentDir = Path.Combine(_runtimesDir, requirement.Component);
        string versionMarkerPath = Path.Combine(componentDir, ".version");
        string javaExePath = getJavaExecutablePath(componentDir);

        JsonElement buildEntry = await getPlatformBuildEntry(requirement.Component);
        string latestVersionName = buildEntry.GetProperty("version").GetProperty("name").GetString()!;
        
        // if it already exists, just skip the download
        if (File.Exists(javaExePath) && File.Exists(versionMarkerPath))
        {
            string installedVersion = await File.ReadAllTextAsync(versionMarkerPath);
            if (installedVersion.Trim() == latestVersionName)
            {
                return javaExePath;
            }
        }
        
        onStatus?.Invoke($"Downloading Java runtime {requirement.Component} {latestVersionName}...");

        string manifestUrl = buildEntry.GetProperty("manifest").GetProperty("url").GetString()!;
        string manifestJson = await _httpClient.GetStringAsync(manifestUrl);
        JsonElement manifest = JsonDocument.Parse(manifestJson).RootElement;
        
        await downloadRuntimeFiles(manifest.GetProperty("files"), componentDir, onStatus);
        
        Directory.CreateDirectory(componentDir);
        await File.WriteAllTextAsync(versionMarkerPath, latestVersionName);
        
        if (!File.Exists(javaExePath)) 
            throw new Exception($"Runtime downloaded but Java executable not found at {javaExePath}");
        
        return javaExePath;
    }
    
    // Platform lookup
    private async Task<JsonElement> getPlatformBuildEntry(string component)
    {
        String json = await _httpClient.GetStringAsync(RuntimeManifestUrl);
        JsonElement root = JsonDocument.Parse(json).RootElement;

        string platformKey = getPlatformKey();
        if (!root.TryGetProperty(platformKey, out JsonElement platform))
            throw new Exception($"No Java runtimes available for platform '{platformKey}'");
        if (!platform.TryGetProperty(component, out JsonElement componentArray) || componentArray.GetArrayLength() == 0)
            {
                throw new Exception($"No '{component}' runtimes available for platform '{platformKey}'");
            }
        
        return componentArray[0];
    }

    // Get the platform's key to use in the build entry
    private string getPlatformKey()
    {
        bool is64 = RuntimeInformation.OSArchitecture == Architecture.X64;
        bool isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;

        if (OperatingSystem.IsWindows())
        {
            if (isArm64) return "windows-arm64";
            return is64 ? "windows-x64" : "windows-x86";
        }

        if (OperatingSystem.IsMacOS())
        {
            return isArm64 ? "mac-os-arm64" : "mac-os";
        }
        
        return is64 ? "linux" : "linux-i386";
    }

    // Get the path to the Java executable
    private string getJavaExecutablePath(string componentDir)
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(componentDir, "jre.bundle", "Contents", "Home", "bin", "java");
        }
        string exeName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        return Path.Combine(componentDir, "bin", exeName);
    }
    
    // Download java runtime and reconstruct the file tree
    private async Task downloadRuntimeFiles(JsonElement files, string targetDir, Action<string>? onStatus)
    {
        var downloadTasks = new List<Task>();
        int total = files.EnumerateObject().GetEnumberableCount();
        int completed = 0;

        foreach (JsonProperty entry in files.EnumerateObject())
        {
            string relativePath = entry.Name;
            JsonElement node = entry.Value;
            string fullPath = Path.Combine(targetDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string type = node.GetProperty("type").GetString()!;

            switch (type)
            {
                case "directory":
                    Directory.CreateDirectory(fullPath);
                    break;
                case "link":
                    break;
                case "file":
                    bool executable = node.TryGetProperty("executable", out JsonElement execProp) &&
                                      execProp.GetBoolean();
                    string url = node.GetProperty("downloads").GetProperty("raw").GetProperty("url").GetString()!;
                    downloadTasks.Add(downloadRuntimeFile(url, fullPath, executable, () =>
                    {
                        completed++;
                        if (completed % 25 == 0)
                            onStatus?.Invoke($"{completed}/{total} runtime files downloaded");
                    }));
                    break;
            }
        }
        await Task.WhenAll(downloadTasks);
        
        // handle symlinks
        foreach (JsonProperty entry in files.EnumerateObject())
        {
            JsonElement node = entry.Value;
            if (node.GetProperty("type").GetString() != "link") continue;
            
            string relativePath = entry.Name;
            string fullPath = Path.Combine(targetDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string target = node.GetProperty("target").GetString();

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            try
            {
                if (File.Exists(fullPath) || Directory.Exists(fullPath)) continue;
                File.CreateSymbolicLink(fullPath, target);
            }
            catch (IOException)
            {
                // ignore
            }
        }
    }

    // Download the runtime file itself
    private async Task downloadRuntimeFile(string url, string destPath, bool executable, Action onComplete)
    {
        if (File.Exists(destPath))
        {
            onComplete();
            return;
        }
        
        await _downloadSemaphore.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using HttpResponseMessage response =
                await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (FileStream fileStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            if (executable && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex)
        {
            if (File.Exists(destPath)) File.Delete(destPath);
            throw new Exception($"Failed to download {url}: {ex.Message}", ex);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
        
        onComplete();
    }
}

// Add enumerable count to JsonElement
internal static class JsonElementExtensions
{
    public static int GetEnumberableCount(this JsonElement.ObjectEnumerator enumerator)
    {
        int count = 0;
        foreach (var _ in enumerator) count++;
        return count;
    }
}