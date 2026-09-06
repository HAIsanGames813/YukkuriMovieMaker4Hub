using System;
using System.IO;
using System.Text.Json;

namespace YukkuriMovieMaker4Hub
{
    public class SettingsManager
    {
        private readonly string _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        
        public AppSettings Load()
        {
            if (!File.Exists(_path)) return new AppSettings();
            try 
            { 
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings(); 
            }
            catch 
            { 
                return new AppSettings(); 
            }
        }

        public void Save(AppSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_path, json);
        }
    }
}
