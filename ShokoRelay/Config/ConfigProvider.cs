using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NLog;
using Shoko.Plugin.Abstractions;
using ShokoRelay.Helpers;

namespace ShokoRelay.Config
{
    public class ConfigProvider
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static RelayConfig CreateDefaultSettings() => new RelayConfig();

        private readonly string _filePath;
        private readonly object _settingsLock = new();
        private RelayConfig? _settings;

        private static readonly JsonSerializerOptions Options = new() { AllowTrailingCommas = true, WriteIndented = true };

        public ConfigProvider(IApplicationPaths applicationPaths)
        {
            string pluginDir = PluginPaths.PluginDirectory;
            _filePath = Path.Combine(pluginDir, ConfigConstants.ConfigFileName);
            Logger.Info($"Config path: {_filePath}");
        }

        public RelayConfig GetSettings()
        {
            _settings ??= GetSettingsFromFile();
            return _settings;
        }

        public void SaveSettings(RelayConfig settings)
        {
            ValidateSettings(settings);

            var json = JsonSerializer.Serialize(settings, Options);
            lock (_settingsLock)
            {
                File.WriteAllText(_filePath, json);
            }

            _settings = settings;
            Logger.Info("Config saved.");
        }

        private RelayConfig GetSettingsFromFile()
        {
            RelayConfig settings;
            var needsSave = false;

            try
            {
                var contents = File.ReadAllText(_filePath);
                settings = JsonSerializer.Deserialize<RelayConfig>(contents, Options)!;

                if (settings is null)
                {
                    settings = CreateDefaultSettings();
                    needsSave = true;
                    Logger.Warn("Config file empty or invalid JSON, using defaults.");
                }
                else
                {
                    Logger.Info("Config loaded from file.");
                }
            }
            catch (FileNotFoundException)
            {
                settings = CreateDefaultSettings();
                needsSave = true;
                Logger.Info("Config file not found, creating defaults.");
            }
            catch (JsonException ex)
            {
                settings = CreateDefaultSettings();
                needsSave = true;
                Logger.Warn($"Invalid config file, using defaults: {ex.Message}");
            }

            ValidateSettings(settings);

            if (needsSave)
                SaveSettings(settings);

            return settings;
        }

        private static void ValidateSettings(RelayConfig settings)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(settings);

            var isValid = Validator.TryValidateObject(settings, validationContext, validationResults, true);
            if (isValid)
                return;

            foreach (var validationResult in validationResults)
            {
                foreach (var memberName in validationResult.MemberNames)
                {
                    Logger.Error($"Error validating settings for property {memberName}: {validationResult.ErrorMessage}");
                }
            }

            throw new ArgumentException("Error in settings validation");
        }
    }
}
