using System;

namespace MinecraftModLauncher.Models;

public record InstalledMod (
    string ProjectId,
    string VersionId,
    string Title,
    string? IconUrl,
    string VersionNumber,
    string Filename,
    string ProjectType, // "mod | "shader" | "resourcepack"
    DateTimeOffset InstalledAt
    );