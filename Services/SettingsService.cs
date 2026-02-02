using System;
using System.IO;
using LazerNi.Models;
using Newtonsoft.Json;

namespace LazerNi.Services
{
    public class SettingsService
    {
        private readonly string _settingsFilePath = "settings.json";
        private readonly LoggingService _logger;

        public AppSettings Settings { get; private set; }

        public SettingsService(LoggingService logger)
        {
            _logger = logger;
            Settings = LoadSettings();
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);

                    if (settings != null)
                    {
                        _logger.Info($"Settings loaded from {_settingsFilePath}");
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load settings: {ex.Message}");
            }

            _logger.Info("Creating default settings");
            return new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                _logger.Info($"Settings saved to {_settingsFilePath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save settings: {ex.Message}");
                throw;
            }
        }
    }
}
