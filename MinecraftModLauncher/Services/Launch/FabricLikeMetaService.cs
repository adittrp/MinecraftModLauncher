using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services.Launch;

// Fabric and Quilt each expose an API-compatible "meta" server: a list of
// loader versions for a game version, and a profile/json endpoint that
// returns a launch-ready version-meta JSON (flat mainClass string, flat
// libraries array of Maven {name, url} coordinates) for a game+loader
// version pair. One class serves both, parameterized by base URL:
//   Fabric: https://meta.fabricmc.net/v2
//   Quilt:  https://meta.quiltmc.org/v3
public class FabricLikeMetaService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public FabricLikeMetaService(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    // Picks the newest stable loader version for a game version (falls back
    // to the newest entry if the API doesn't report a "stable" flag, as with
    // Quilt's meta server).
    public async Task<string> GetLatestLoaderVersion(string gameVersion)
    {
        string url = $"{_baseUrl}/versions/loader/{gameVersion}";
        string json = await _httpClient.GetStringAsync(url);
        using JsonDocument doc = JsonDocument.Parse(json);

        if (doc.RootElement.GetArrayLength() == 0)
            throw new Exception($"No loader versions available for Minecraft {gameVersion}");

        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            JsonElement loader = entry.GetProperty("loader");
            if (!loader.TryGetProperty("stable", out JsonElement stable) || stable.GetBoolean())
                return loader.GetProperty("version").GetString()!;
        }

        return doc.RootElement[0].GetProperty("loader").GetProperty("version").GetString()!;
    }

    // Launch-ready profile: flat "mainClass" string, flat "libraries" array
    // of {name, url} Maven coordinates. Same shape used by the standard
    // Minecraft launcher's "inheritsFrom" version chaining.
    public async Task<JsonElement> GetProfile(string gameVersion, string loaderVersion)
    {
        string url = $"{_baseUrl}/versions/loader/{gameVersion}/{loaderVersion}/profile/json";
        string json = await _httpClient.GetStringAsync(url);
        return JsonDocument.Parse(json).RootElement;
    }
}
