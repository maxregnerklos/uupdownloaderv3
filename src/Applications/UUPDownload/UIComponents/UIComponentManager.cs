using System;
using System.IO;
using System.Threading.Tasks;
using UnifiedUpdatePlatform.Services.Logging;

namespace UUPDownload.UIComponents
{
    public class UIComponentManager
    {
        private readonly string _basePath;
        private readonly UIComponentMerger _merger;
        private readonly UIRegistryManager _registryManager;

        public UIComponentManager(string basePath)
        {
            _basePath = basePath;
            _merger = new UIComponentMerger(basePath);
            _registryManager = new UIRegistryManager(basePath);
        }

        public async Task InitializeAsync()
        {
            await _merger.ValidateComponentsAsync();
            await _registryManager.InitializeRegistryKeysAsync();
        }

        public async Task ApplyUIModificationsAsync()
        {
            try
            {
                Logging.Log("Starting UI component modifications...");
                await _merger.MergeUIComponentsAsync();
                await _registryManager.ApplyRegistryChangesAsync();
                Logging.Log("UI modifications completed successfully");
            }
            catch (Exception ex)
            {
                Logging.Log($"Error applying UI modifications: {ex.Message}");
                throw;
            }
        }
    }
} 