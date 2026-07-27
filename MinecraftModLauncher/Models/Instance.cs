using System.Collections.Generic;

namespace MinecraftModLauncher.Models;

public record Instance(string Name,
    string GameVersion,
    string Loader,
    string Description,
    string? IconUrl,
    List<InstalledMod> Mods
    );