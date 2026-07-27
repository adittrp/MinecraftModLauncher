using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftModLauncher.Models;

namespace MinecraftModLauncher.Services;

public class InstanceService
{
    private readonly string _instancesRoot;

    public InstanceService(string instancesRoot)
    {
        _instancesRoot = Path.Combine(instancesRoot, "instances");
    }
    
    private string getInstanceJsonPath(string instanceName) =>
    Path.Combine(_instancesRoot, instanceName, "instance.json");
    
    public string getInstanceModsDir(string instanceName) =>
    Path.Combine(_instancesRoot, instanceName, ".minecraft", "mods");

    public async Task<List<Instance>> loadAllInstances()
    {
        if (!Directory.Exists(_instancesRoot)) return new List<Instance>();

        var instances = new List<Instance>();
        foreach (string dir in Directory.GetDirectories(_instancesRoot))
        {
            string jsonPath = Path.Combine(dir, "instance.json");
            if (!File.Exists(jsonPath)) continue;

            string json = await File.ReadAllTextAsync(jsonPath);
            Instance? instance = JsonSerializer.Deserialize<Instance>(json);
            if (instance != null) instances.Add(instance);
        }
        return instances;
    }

    public async Task<Instance> createInstance(string name, string? iconURL, string gameVersion, string loader,
        string? description = "No description yet.")
    {
        var instance = new Instance(name, gameVersion, loader, description, iconURL, new List<InstalledMod>());
        await saveInstance(instance);
        return instance;
    }

    public async Task saveInstance(Instance instance)
    {
        string jsonPath = getInstanceJsonPath(instance.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        
        string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json);
    }

    public async Task<Instance> addMod(Instance instance, InstalledMod mod)
    {
        List<InstalledMod> updatedMods = instance.Mods
            .Where(m => m.ProjectId != mod.ProjectId)
            .Append(mod)
            .ToList();
        
        Instance updated = instance with { Mods = updatedMods };
        await saveInstance(updated);
        return updated;
    }

    public async Task<Instance> removeMod(Instance instance, string projectId)
    {
        List<InstalledMod> updatedMods = instance.Mods
            .Where(m => m.ProjectId != projectId)
            .ToList();

        Instance updated = instance with { Mods = updatedMods };
        await saveInstance(updated);
        return updated;
    }
}