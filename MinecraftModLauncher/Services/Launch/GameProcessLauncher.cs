using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MinecraftModLauncher.Models;

namespace MinecraftModLauncher.Services.Launch;

// Builds the argument list for the version/instance's game process'
// Logs are tracked in a caller-supplied log sink.

public class GameProcessLauncher
{
    public Process Launch(
        string javaPath,
        string versionId,
        string mainClass,
        JsonElement versionMeta,
        List<string> libraryPaths,
        string clientJarPath,
        string gameDirPath,
        string assetsDirPath,
        MinecraftAccount? account,
        Action<string> onLogLine)
    {
        string classPathSeparator = OperatingSystem.IsWindows() ? ";" : ":";
        var allJars = new List<String>(libraryPaths) { clientJarPath };
        string classpath = string.Join(classPathSeparator, allJars);

        string assetIndex = versionMeta.GetProperty("assetIndex").GetProperty("id").GetString();
        
        var startInfo = new ProcessStartInfo {
                FileName = javaPath,
                WorkingDirectory = gameDirPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        
        // JVM arguments
        startInfo.ArgumentList.Add("-Xmx2G");
        startInfo.ArgumentList.Add("-Xms512M");
        startInfo.ArgumentList.Add($"-Djava.library.path={Path.Combine(gameDirPath, "natives")}");
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(classpath);
        startInfo.ArgumentList.Add(mainClass);

        // Game arguments
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(account?.Username ?? "Player");
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(versionId);
        startInfo.ArgumentList.Add("--gameDir");
        startInfo.ArgumentList.Add(gameDirPath);
        startInfo.ArgumentList.Add("--assetsDir");
        startInfo.ArgumentList.Add(assetsDirPath);
        startInfo.ArgumentList.Add("--assetIndex");
        startInfo.ArgumentList.Add(assetIndex);
        startInfo.ArgumentList.Add("--uuid");
        startInfo.ArgumentList.Add(account?.Uuid ?? Guid.NewGuid().ToString("N"));
        startInfo.ArgumentList.Add("--accessToken");
        startInfo.ArgumentList.Add(account?.AccessToken ?? "0");
        startInfo.ArgumentList.Add("--userType");
        startInfo.ArgumentList.Add(account != null ? "msa" : "legacy");

        Process process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrEmpty(e.Data)) onLogLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrEmpty(e.Data)) onLogLine($"[ERROR] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        return process;
    }
}