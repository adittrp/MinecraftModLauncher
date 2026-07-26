using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models.Modrinth;

public record ModrinthSearchResult(
    [property: JsonPropertyName("hits")] 
    List<ModrinthSearchHit> Hits,
    [property: JsonPropertyName("total_hits")] 
    int TotalHits
);