/*
 * Copyright (c) Gustave Monce and Contributors
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnifiedUpdatePlatform.Media.Creator.Planning;
using UnifiedUpdatePlatform.Services.Composition.Database;
using UnifiedUpdatePlatform.Services.Temp;
using UnifiedUpdatePlatform.Services.WindowsUpdate;

namespace UUPDownload
{
    public static class BuildTargets
    {
        public class EditionPlanningWithLanguage
        {
            public List<EditionTarget> EditionTargets;
            public string LanguageCode;
        }

        public static async Task<EditionPlanningWithLanguage> GetTargetedPlanAsync(this UpdateData update, string LanguageCode)
        {
            HashSet<CompDB> compDBs = await update.GetCompDBsAsync();
            Package editionPackPkg = compDBs.GetEditionPackFromCompDBs();

            string editionPkg = await update.DownloadFileFromDigestAsync(editionPackPkg.Payload.PayloadItem.First(x => !x.Path.EndsWith(".psf")).PayloadHash);
            return await update.GetTargetedPlanAsync(LanguageCode, editionPkg);
        }

        public static async Task<EditionPlanningWithLanguage> GetTargetedPlanAsync(this UpdateData update, string LanguageCode, string editionPkg)
        {
            HashSet<CompDB> compDBs = await update.GetCompDBsAsync();
            if (string.IsNullOrEmpty(editionPkg))
            {
                return null;
            }

            _ = ConversionPlanBuilder.GetTargetedPlan(compDBs, editionPkg, LanguageCode, true, out List<EditionTarget> targets, new TempManager(), null);
            return new EditionPlanningWithLanguage() { EditionTargets = targets, LanguageCode = LanguageCode };
        }

        public static void PrintAvailablePlan(this List<EditionTarget> targets)
        {
            foreach (EditionTarget target in targets)
            {
                foreach (string str in ConversionPlanBuilder.PrintEditionTarget(target))
                {
                    Logging.Log(str);
                }
            }
        }

        public class UIComponentMerger
        {
            private readonly string win11UupPath;
            private readonly string win10XUupPath;
            private readonly Dictionary<string, string> uiComponentMap;

            public UIComponentMerger(string win11Path, string win10XPath)
            {
                win11UupPath = win11Path;
                win10XUupPath = win10XPath;
                uiComponentMap = InitializeComponentMap();
            }

            private Dictionary<string, string> InitializeComponentMap()
            {
                return new Dictionary<string, string>
                {
                    { "StartMenuExperienceHost", "Windows.UI.StartMenu" },
                    { "ShellExperienceHost", "Windows.UI.Shell" },
                    { "SystemSettings", "Windows.UI.Settings" },
                    { "TaskbarComponents", "Windows.UI.Taskbar" }
                };
            }

            public async Task MergeUIComponentsAsync()
            {
                foreach (var component in uiComponentMap)
                {
                    await MergeComponentAsync(component.Key, component.Value);
                }
            }

            private async Task MergeComponentAsync(string componentName, string targetPackage)
            {
                try
                {
                    // Verify source components exist
                    string sourcePath = Path.Combine(win10XUupPath, "Components", componentName);
                    if (!Directory.Exists(sourcePath))
                    {
                        Logger.Log($"Warning: Source component {componentName} not found");
                        return;
                    }

                    // Create backup of Windows 11 components
                    string targetPath = Path.Combine(win11UupPath, "Components", componentName);
                    if (Directory.Exists(targetPath))
                    {
                        string backupPath = targetPath + ".backup";
                        Directory.Move(targetPath, backupPath);
                    }

                    // Copy Windows 10X components
                    await Task.Run(() => Directory.Copy(sourcePath, targetPath, true));
                    
                    // Update component manifest
                    await UpdateComponentManifest(componentName, targetPackage);
                    
                    Logger.Log($"Successfully merged {componentName}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error merging {componentName}: {ex.Message}");
                    throw;
                }
            }

            private async Task UpdateComponentManifest(string componentName, string packageName)
            {
                string manifestPath = Path.Combine(win11UupPath, "Manifests", $"{componentName}.manifest");
                
                if (File.Exists(manifestPath))
                {
                    var manifest = await File.ReadAllTextAsync(manifestPath);
                    
                    // Update package references
                    manifest = manifest.Replace(
                        $"<Package>{packageName}</Package>",
                        $"<Package>{packageName}_10X</Package>"
                    );
                    
                    await File.WriteAllTextAsync(manifestPath, manifest);
                }
            }
        }

        public async Task MergeWindows10XUIAsync(string buildPath)
        {
            try
            {
                Logger.Log("Starting Windows 10X UI merge process...");

                // Validate paths and create merger instance
                string win11Path = Path.Combine(buildPath, "Windows11");
                string win10XPath = Path.Combine(buildPath, "Windows10X");

                if (!Directory.Exists(win11Path) || !Directory.Exists(win10XPath))
                {
                    throw new DirectoryNotFoundException("Required UUP directories not found");
                }

                var merger = new UIComponentMerger(win11Path, win10XPath);
                await merger.MergeUIComponentsAsync();

                // Update registry modifications
                await ApplyRegistryModificationsAsync(win11Path);

                Logger.Log("Windows 10X UI merge completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during Windows 10X UI merge: {ex.Message}");
                throw;
            }
        }

        private async Task ApplyRegistryModificationsAsync(string buildPath)
        {
            string registryPath = Path.Combine(buildPath, "Registry");
            
            var registryModifications = new Dictionary<string, string>
            {
                { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl=1" },
                { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "EnableFloatingTaskbar=1" },
                { @"SOFTWARE\Microsoft\Windows\DWM", "EnableRoundedCorners=1" }
            };

            foreach (var mod in registryModifications)
            {
                await File.AppendAllTextAsync(
                    Path.Combine(registryPath, "Registry.dat"),
                    $"[{mod.Key}]\n{mod.Value}\n\n"
                );
            }
        }
    }
}
