using System.Collections.Generic;
using System.Text.Json.Serialization;
using MinecraftModLauncher.Services;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthVersion(
    [property: JsonPropertyName("id")] 
    string Id,
    [property: JsonPropertyName("project_id")] 
    string ProjectId,
    [property: JsonPropertyName("version_number")] 
    string VersionNumber,
    [property: JsonPropertyName("game_versions")] 
    List<string> GameVersions,
    [property: JsonPropertyName("loaders")] 
    List<string> Loaders,
    [property: JsonPropertyName("dependencies")] 
    List<ModrinthDependency> Dependencies,
    [property: JsonPropertyName("files")] 
    List<ModrinthVersionFile> Files
);

public record ModrinthVersionFile(
    [property: JsonPropertyName("url")] 
    string Url,
    [property: JsonPropertyName("filename")] 
    string Filename,
    [property: JsonPropertyName("primary")] 
    bool Primary,
    [property: JsonPropertyName("hashes")] 
    Dictionary<string, string> Hashes
);