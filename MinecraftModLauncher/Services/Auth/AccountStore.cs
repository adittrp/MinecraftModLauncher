using MinecraftModLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MinecraftModLauncher.Services {
    public class AccountStore {
        private readonly string _filePath;

        public AccountStore(string launcherRoot) {
            _filePath = Path.Combine(launcherRoot, "account.json");
        }

        public async Task Save(MinecraftAccount account) {
            string json = JsonSerializer.Serialize(account, new JsonSerializerOptions {
                WriteIndented = true
            });
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public async Task<MinecraftAccount?> Load() {
            if (!File.Exists(_filePath))
                return null;

            try {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<MinecraftAccount>(json);
            } catch {
                return null;
            }
        }

        public void Delete() {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
    }
}
