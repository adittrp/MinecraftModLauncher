using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinecraftModLauncher.Models;

public record Instance(string Name,
    string GameVersion,
    string Loader,
    string Description,
    string? IconUrl,
    List<InstalledMod> Mods,
    // Modrinth modpack-category slugs (e.g. "technology", "magic"). Same slug
    // vocabulary ModrinthService.getCategories() returns, so Library's future
    // category filtering can share these values with no conversion.
    List<string>? Categories = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset UpdatedAt = default
    )
{
    [JsonIgnore]
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}