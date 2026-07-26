using System.Linq;
using System.Reflection;

namespace MinecraftModLauncher.Services;

public static class BuildInfo
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    public static string GitHubUsername => getMetadata("GitHubUsername", "unknown-fork");
    public static string ProjectName => getMetadata("ProjectName", "MinecraftModLauncher");
    public static string ContactUrl => getMetadata("ContactUrl", "");
    
    public static string Version => _assembly.GetName().Version.ToString(3) ?? "0.0.0";

    private static string getMetadata(string key, string fallback)
    {
        string? value = _assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)
            ?.Value;
            
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}