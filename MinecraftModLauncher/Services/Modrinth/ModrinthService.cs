using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Quic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using MinecraftModLauncher.Models.Modrinth;

namespace MinecraftModLauncher.Services;

public class ModrinthService
{
    private const string ApiUrl = "https://api.modrinth.com/v2";
    private readonly HttpClient _httpClient;
    private readonly ModrinthRateLimiter _rateLimiter = new();
    
    // Info from buildinfo for the user-agent sent to the api (or else modrinth will block our requests lol) also prevents forks from being misattributed to our public api requests
    public ModrinthService(
        string? githubUsername = null,
        string? projectName = null,
        string? version = null,
        string? contact = null
    )
    {
        githubUsername ??= BuildInfo.GitHubUsername;
        projectName ??= BuildInfo.ProjectName;
        version ??= BuildInfo.Version;
        contact ??= BuildInfo.ContactUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        // make sure it is sent as string and as a USER AGENT
        string userAgent = $"{githubUsername}/{projectName}/{version} ({contact})";
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
    }

    // perform a search
    
    public async Task<ModrinthSearchResult> search(
        string query,
        string? projectType = null,
        string? gameVersion = null,
        string? loader = null,
        int limit = 20,
        int offset = 0)
    {
        var facets = new List<List<string>>();
        if (projectType != null) facets.Add(new List<string> { $"project_type:{projectType}" });
        if (gameVersion != null) facets.Add(new List<string>{$"versions:{gameVersion}"});
        if (loader != null) facets.Add(new List<string>{$"categories:{loader}"});
        
        var query_params = HttpUtility.ParseQueryString(string.Empty);
        query_params["query"] = query;
        query_params["limit"] = limit.ToString();
        query_params["offset"] = offset.ToString();
        if (facets.Count > 0)
        {
            query_params["facets"] = JsonSerializer.Serialize(facets);
        }
        
        string url = $"{ApiUrl}/search?{query_params}";
        string json = await getRateLimited(url);
        
        return JsonSerializer.Deserialize<ModrinthSearchResult>(json)
            ?? throw new Exception("Failed to parse Modrinth search result");
    }
    
    // get a project's versions'

    public async Task<List<ModrinthVersion>> getProjectVersions(
        string projectId,
        string? gameVersion = null,
        string? loader = null)
    {
        var query_params = HttpUtility.ParseQueryString(string.Empty);
        if (gameVersion != null) query_params["game_versions"] = JsonSerializer.Serialize(new[] {gameVersion});
        if (loader != null) query_params["loaders"] = JsonSerializer.Serialize(new[] {loader});

        string url = $"{ApiUrl}/project/{projectId}/version";
        if (query_params.Count > 0) url += $"?{query_params}";
        
        string json = await _httpClient.GetStringAsync(url);
        
        return JsonSerializer.Deserialize<List<ModrinthVersion>>(json)
            ?? throw new Exception("Failed to parse Modrinth project versions");
    }
    
    // batch methods to get multiple projects info at once to limit requests

    public async Task<List<ModrinthProject>> getProjects(IEnumerable<string> projectIds)
    {
        List<string> ids = projectIds.Distinct().ToList();
        if (ids.Count == 0) return new List<ModrinthProject>();
        
        string idsParam = JsonSerializer.Serialize(ids);
        string url = $"{ApiUrl}/project?ids={Uri.EscapeDataString(idsParam)}";
        string json = await getRateLimited(url);

        return JsonSerializer.Deserialize<List<ModrinthProject>>(json)
               ?? throw new Exception("Failed to parse Modrinth projects");
    }

    public async Task<List<ModrinthVersion>> getVersions(IEnumerable<string> versionIds)
    {
        List<string> ids = versionIds.Distinct().ToList();
        if (ids.Count == 0) return new List<ModrinthVersion>();
        
        string idsParam = JsonSerializer.Serialize(ids);
        string url = $"{ApiUrl}/version?ids={Uri.EscapeDataString(idsParam)}";
        string json = await getRateLimited(url);
        
        return JsonSerializer.Deserialize<List<ModrinthVersion>>(json)
               ?? throw new Exception("Failed to parse Modrinth versions");   
    }
    
    // download a version's files

    public async Task<string> downloadVersionFile(ModrinthVersion version, string destDir)
    {
        ModrinthVersionFile file = version.Files.Find(f => f.Primary) ?? version.Files[0];
        string destPath = Path.Combine(destDir, file.Filename);

        if (File.Exists(destPath)) return destPath;

        Directory.CreateDirectory(destDir);
        
        using HttpResponseMessage response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using (FileStream fileStream = new(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await response.Content.CopyToAsync(fileStream);
        }

        if (file.Hashes.TryGetValue("sha1", out string? expectedSha1))
        {
            string actualSha1 = await computeSha1(destPath);
            if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destPath);
                throw new Exception($"Hash mismatch for {file.Filename}: expected {expectedSha1}, got {actualSha1}. May be a corrupted download.");
            }
        }
        return destPath;
    }

    // rate limited version of get
    private async Task<string> getRateLimited(string url)
    {
        await _rateLimiter.waitForSlot();
        
        using HttpResponseMessage response = await _httpClient.GetAsync(url);
        _rateLimiter.updateFromHeaders(response.Headers);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            await Task.Delay(retryAfter);
            return await getRateLimited(url);
        }
        
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    
    // compute sha1 (hash) of a file
    private static async Task<string> computeSha1(string path)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await sha1.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}