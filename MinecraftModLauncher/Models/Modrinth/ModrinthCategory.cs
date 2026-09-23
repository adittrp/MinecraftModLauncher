using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthCategory(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("project_type")]
    string ProjectType,
    [property: JsonPropertyName("header")]
    string Header,
    [property: JsonPropertyName("icon")]
    string? Icon
);
