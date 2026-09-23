using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services.Launch;

public class VersionManifestService
{
    
    // Fetches Mojang's version manifest and per-version metadata, caching both under launcherRoot/cache
    
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly TimeSpan ManifestChacheTt1 = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public VersionManifestService(HttpClient httpClient, string launcherRoot)
    {
        _httpClient = httpClient;
        _cacheDir = Path.Combine(launcherRoot, "cache");
    }

    
    // Returns the full version metadata for a given version, caches the result
    public async Task<JsonElement> FetchVersionMetadata(string versionId)
    {
        string cachePath = Path.Combine(_cacheDir, "versions", $"{versionId}.json");

        if (File.Exists(cachePath))
        {
            string cached = await File.ReadAllTextAsync(cachePath);
            return JsonDocument.Parse(cached).RootElement;
        }
        
        JsonElement manifest = await FetchVersionManifest();
        JsonElement versions = manifest.GetProperty("versions");

        foreach (JsonElement version in versions.EnumerateArray())
        {
            if (version.GetProperty("id").GetString() != versionId) continue;

            string metadataUrl = version.GetProperty("url").GetString()!;
            string json = await _httpClient.GetStringAsync(metadataUrl);
            
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            await File.WriteAllTextAsync(cachePath, json);
            
            return JsonDocument.Parse(json).RootElement;
        }
        
        throw new Exception($"Version {versionId} not found in manifest");
    }
    
    // Returns full version manifest, using a cached version if possible
    public async Task<JsonElement> FetchVersionManifest() {
        string cachePath = Path.Combine(_cacheDir, "version_manifest_v2.json");

        if (File.Exists(cachePath))
        {
            TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
            if (age < ManifestChacheTt1)
            {
                string cached = await File.ReadAllTextAsync(cachePath);
                return JsonDocument.Parse(cached).RootElement;
            }
        }

        string json = await _httpClient.GetStringAsync(ManifestUrl);

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
        await File.WriteAllTextAsync(cachePath, json);
        
        return JsonDocument.Parse(json).RootElement;
    }
}