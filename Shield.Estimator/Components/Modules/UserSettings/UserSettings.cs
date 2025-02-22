//OperSettingsService.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shield.Estimator.Shared.Components.Modules.UserSettings
{
    public class SourceName
    {
        private static readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "settingsOper.json");

        public static async Task SaveItemAsync(string key, string value)
        {
            var settings = await ReadAllItemsFromFile();
            settings[key] = value;
            await WriteAllItemsToFile(settings);
        }
        public static async Task<string?> ReadItemValueByKey(string key)
        {
            if(string.IsNullOrEmpty(key))
            {
                return null;
            }
            var settings = await ReadAllItemsFromFile();
            return settings.TryGetValue(key, out string? value) ? value : null;
        }
        public static async Task DeleteItemByKey(string key)
        {
            var settings = await ReadAllItemsFromFile();
            if (settings.ContainsKey(key))
            {
                settings.Remove(key);
                await WriteAllItemsToFile(settings);
            }
        }

        public static async Task<Dictionary<string, string>> ReadAllItemsFromFile()
        {
            if (!File.Exists(_filePath))
                return new Dictionary<string, string>();

            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }

        private static async Task WriteAllItemsToFile(Dictionary<string, string> settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
    }
}