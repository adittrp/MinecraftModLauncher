using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthProject(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("slug")]
    string Slug,
    [property: JsonPropertyName("title")]
    string Title,
    [property: JsonPropertyName("description")]
    string Description,
    [property: JsonPropertyName("icon_url")]
    string? IconUrl,
    [property: JsonPropertyName("project_type")] string ProjectType
);