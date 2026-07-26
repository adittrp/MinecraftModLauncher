using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthSearchHit(
    [property: JsonPropertyName("project_id")]
    string ProjectId,
    [property: JsonPropertyName("slug")] 
    string Slug,
    [property: JsonPropertyName("title")] 
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("author")] 
    string Author,
    [property: JsonPropertyName("icon_url")]
    string? IconUrl,
    [property: JsonPropertyName("downloads")]
    long Downloads,
    [property: JsonPropertyName("project_type")] 
    string ProjectType, // mod, modpack, resourcepack, etc.
    [property: JsonPropertyName("versions")]
    List<string> GameVersions
);