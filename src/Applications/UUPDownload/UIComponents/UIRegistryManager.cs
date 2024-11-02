using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace UUPDownload.UIComponents
{
    public class UIRegistryManager
    {
        private readonly string _registryPath;
        private readonly Dictionary<string, Dictionary<string, string>> _registryModifications;

        public UIRegistryManager(string basePath)
        {
            _registryPath = Path.Combine(basePath, "Registry");
            _registryModifications = InitializeRegistryMods();
        }

        private Dictionary<string, Dictionary<string, string>> InitializeRegistryMods()
        {
            return new Dictionary<string, Dictionary<string, string>>
            {
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    new Dictionary<string, string>
                    {
                        { "TaskbarAl", "1" },
                        { "TaskbarStyle", "2" },
                        { "EnableFloatingTaskbar", "1" }
                    }
                },
                {
                    @"SOFTWARE\Microsoft\Windows\DWM",
                    new Dictionary<string, string>
                    {
                        { "EnableRoundedCorners", "1" },
                        { "EnableBlurBehind", "1" }
                    }
                }
            };
        }

        public async Task InitializeRegistryKeysAsync()
        {
            if (!Directory.Exists(_registryPath))
            {
                Directory.CreateDirectory(_registryPath);
            }
        }

        public async Task ApplyRegistryChangesAsync()
        {
            string registryFile = Path.Combine(_registryPath, "UICustomizations.reg");
            using StreamWriter writer = new(registryFile);

            await writer.WriteLineAsync("Windows Registry Editor Version 5.00");
            
            foreach (var (key, values) in _registryModifications)
            {
                await writer.WriteLineAsync($"\n[HKEY_LOCAL_MACHINE\\{key}]");
                foreach (var (name, value) in values)
                {
                    await writer.WriteLineAsync($"\"{name}\"=dword:{value}");
                }
            }
        }
    }
} 