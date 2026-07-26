using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthDependency(
    [property: JsonPropertyName("project_id")]
    string? ProjectId,
    [property: JsonPropertyName("version_id")]
    string? VersionId,
    [property: JsonPropertyName("dependency_type")]
    string DependencyType // "required" or "optional"
);