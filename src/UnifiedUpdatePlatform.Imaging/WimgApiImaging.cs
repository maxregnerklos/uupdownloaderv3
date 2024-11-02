﻿using ManagedWimLib;
using Microsoft.Wim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnifiedUpdatePlatform.Services.Temp;

namespace UnifiedUpdatePlatform.Services.Imaging
{
    public class WimgApiImaging : IImaging
    {
        public bool AddFileToImage(string wimFile, int imageIndex, string fileToAdd, string destination, IImaging.ProgressCallback progressCallback = null)
        {
            return false;
        }

        public bool UpdateFilesInImage(string wimFile, int imageIndex, IEnumerable<(string fileToAdd, string destination)> fileList, IImaging.ProgressCallback progressCallback = null)
        {
            return false;
        }

        public bool DeleteFileFromImage(string wimFile, int imageIndex, string fileToRemove, IImaging.ProgressCallback progressCallback = null)
        {
            return false;
        }

        public bool EnumerateFiles(string wimFile, int imageIndex, string path, out string[] entries)
        {
            entries = null;
            return false;
        }

        public bool RenameFileInImage(string wimFile, int imageIndex, string sourceFilePath, string destinationFilePath, IImaging.ProgressCallback progressCallback = null)
        {
            return false;
        }

        private readonly string[] ExclusionList = new[]
        {
            "$ntfs.log",
            "hiberfil.sys",
            "pagefile.sys",
            "swapfile.sys",
            "System Volume Information"
        };

        private string[] BuildExclusionList(string WorkingDirectory = null)
        {
            return WorkingDirectory == null
                ? Array.Empty<string>()
                : ExclusionList.Select(excludedItem => Path.Combine(WorkingDirectory.EndsWith(":") ? WorkingDirectory + @"\" : WorkingDirectory, excludedItem)).ToArray();
        }

        private WimMessageCallback GetWimMessageCallback(string title, string WorkingDirectory = null, IImaging.ProgressCallback progressCallback = null)
        {
            int directoriesScanned = 0;
            int filesScanned = 0;

            string[] fittingExclusionList = BuildExclusionList(WorkingDirectory);

            WimMessageResult callback(WimMessageType messageType, object message, object userData)
            {
                switch (messageType)
                {
                    case WimMessageType.Process:
                        {
                            if (!string.IsNullOrEmpty(WorkingDirectory))
                            {
                                WimMessageProcess processMessage = (WimMessageProcess)message;

                                foreach (string excludedItem in fittingExclusionList)
                                {
                                    if (processMessage.Path.StartsWith(excludedItem, StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        processMessage.Process = false;
                                        break;
                                    }
                                }
                            }
                            break;
                        }
                    case WimMessageType.Progress:
                        {
                            WimMessageProgress progressMessage = (WimMessageProgress)message;
                            progressCallback?.Invoke($"{title} (Estimated time remaining: {progressMessage.EstimatedTimeRemaining})", progressMessage.PercentComplete, false);
                            break;
                        }
                    case WimMessageType.Scanning:
                        {
                            WimMessageScanning scanningMessage = (WimMessageScanning)message;

                            int threshold = 1000;

                            switch (scanningMessage.CountType)
                            {
                                case WimMessageScanningType.Directories:
                                    {
                                        if (scanningMessage.Count >= directoriesScanned + threshold)
                                        {
                                            directoriesScanned = scanningMessage.Count;
                                            progressCallback?.Invoke($"Scanning objects ({filesScanned} files, {directoriesScanned} directories scanned)", 0, true);
                                        }
                                        break;
                                    }
                                case WimMessageScanningType.Files:
                                    {
                                        if (scanningMessage.Count >= filesScanned + threshold)
                                        {
                                            filesScanned = scanningMessage.Count;
                                            progressCallback?.Invoke($"Scanning objects ({filesScanned} files, {directoriesScanned} directories scanned)", 0, true);
                                        }
                                        break;
                                    }
                            }

                            break;
                        }
                    case WimMessageType.Error:
                        {
                            WimMessageError errorMessage = (WimMessageError)message;

                            progressCallback?.Invoke("[ERROR] Win32ErrorCode: 0x" + errorMessage.Win32ErrorCode.ToString("X"), 0, true);
                            progressCallback?.Invoke("[ERROR] Path: " + errorMessage.Path, 0, true);
                            break;
                        }
                }

                return WimMessageResult.Success;
            }

            return callback;
        }

        public bool ApplyImage(string wimFile, int imageIndex, string OutputDirectory, IEnumerable<string> referenceWIMs = null, bool PreserveACL = true, IImaging.ProgressCallback progressCallback = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            string title = $"Applying {wimFile.Split(Path.DirectorySeparatorChar).Last()} - Index {imageIndex}";
            try
            {
                WimMessageCallback callback2 = GetWimMessageCallback(title, OutputDirectory, progressCallback);

                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);

                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Read,
                    WimCreationDisposition.OpenAlways,
                    originChunked ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                    WimCompressionType.None);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                //
                _ = WimgApi.RegisterMessageCallback(wimHandle, callback2);

                using WimHandle wimImageHandle = WimgApi.LoadImage(wimHandle, imageIndex);

                if (referenceWIMs != null)
                {
                    foreach (string referenceWim in referenceWIMs)
                    {
                        WimgApi.SetReferenceFile(wimHandle, referenceWim, WimSetReferenceMode.Append, originChunked ? WimSetReferenceOptions.Chunked : WimSetReferenceOptions.None);
                    }
                }

                try
                {
                    WimgApi.ApplyImage(wimImageHandle, OutputDirectory, !PreserveACL ? WimApplyImageOptions.DisableFileAcl | WimApplyImageOptions.DisableDirectoryAcl : WimApplyImageOptions.None);
                }
                catch (Exception e)
                {
                    progressCallback?.Invoke("[EXCEPTION] Message: " + e.Message, 0, true);
                    progressCallback?.Invoke("[EXCEPTION] StackTrace: " + e.StackTrace, 0, true);
                    return false;
                }
                finally
                {
                    // Be sure to unregister the callback method
                    //
                    WimgApi.UnregisterMessageCallback(wimHandle, callback2);
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool CaptureImage(
            string wimFile,
            string imageName,
            string imageDescription,
            string imageFlag,
            string InputDirectory,
            TempManager tempManager,
            string imageDisplayName = null,
            string imageDisplayDescription = null,
            WimCompressionType compressionType = WimCompressionType.Lzx,
            IImaging.ProgressCallback progressCallback = null,
            int UpdateFrom = -1,
            bool PreserveACL = true)
        {
            /*string title = $"Creating {imageName} ({wimFile.Split(Path.DirectorySeparatorChar).Last()})";
            try
            {
                WimMessageCallback callback2 = GetWimMessageCallback(title, InputDirectory, progressCallback);

                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Write,
                    WimCreationDisposition.OpenAlways,
                    compressionType == WimCompressionType.Lzms ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                    compressionType);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                //
                WimgApi.RegisterMessageCallback(wimHandle, callback2);

                try
                {
                    using WimHandle imagehandle = WimgApi.CaptureImage(wimHandle, InputDirectory, !PreserveACL ? (WimCaptureImageOptions.DisableFileAcl | WimCaptureImageOptions.DisableDirectoryAcl) : WimCaptureImageOptions.None);
                    string wiminfo = WimgApi.GetImageInformationAsString(imagehandle);
                    WIMInformationXML.IMAGE wiminfoclass = WIMInformationXML.DeserializeIMAGE(wiminfo);
                    if (!string.IsNullOrEmpty(imageFlag))
                        wiminfoclass.FLAGS = imageFlag;
                    if (!string.IsNullOrEmpty(imageName))
                        wiminfoclass.NAME = imageName;
                    if (!string.IsNullOrEmpty(imageDescription))
                        wiminfoclass.DESCRIPTION = imageDescription;
                    if (!string.IsNullOrEmpty(imageDisplayName))
                        wiminfoclass.DISPLAYNAME = imageDisplayName;
                    if (!string.IsNullOrEmpty(imageDisplayDescription))
                        wiminfoclass.DISPLAYDESCRIPTION = imageDisplayDescription;

                    wiminfo = WIMInformationXML.SerializeIMAGE(wiminfoclass);
                    WimgApi.SetImageInformation(imagehandle, wiminfo);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    // Be sure to unregister the callback method
                    //
                    WimgApi.UnregisterMessageCallback(wimHandle, callback2);
                }
            }
            catch
            {
                return false;
            }
            return true;*/
            return false;
        }

        public bool ExportImage(string wimFile, string destinationWimFile, int imageIndex, IEnumerable<string> referenceWIMs = null, WimCompressionType compressionType = WimCompressionType.Lzx, IImaging.ProgressCallback progressCallback = null, ExportFlags exportFlags = ExportFlags.None)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            string title = $"Exporting {wimFile.Split(Path.DirectorySeparatorChar).Last()} - Index {imageIndex}";
            try
            {
                WimMessageCallback callback2 = GetWimMessageCallback(title, null, progressCallback);

                bool originChunked = wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase);

                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Read,
                    WimCreationDisposition.OpenAlways,
                    originChunked ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                    WimCompressionType.None);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                //
                _ = WimgApi.RegisterMessageCallback(wimHandle, callback2);

                using WimHandle wimImageHandle = WimgApi.LoadImage(wimHandle, imageIndex);

                if (referenceWIMs != null)
                {
                    foreach (string referenceWim in referenceWIMs)
                    {
                        WimgApi.SetReferenceFile(wimHandle, referenceWim, WimSetReferenceMode.Append, originChunked ? WimSetReferenceOptions.Chunked : WimSetReferenceOptions.None);
                    }
                }

                using WimHandle dstWimHandle = WimgApi.CreateFile(
                    destinationWimFile,
                    WimFileAccess.Write,
                    File.Exists(destinationWimFile) ? WimCreationDisposition.OpenExisting : WimCreationDisposition.CreateNew,
                    compressionType == WimCompressionType.Lzms ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                    compressionType);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(dstWimHandle, Path.GetTempPath());

                // Register a method to be called while actions are performed by WIMGAPi for this .wim file
                //
                _ = WimgApi.RegisterMessageCallback(dstWimHandle, callback2);

                try
                {
                    WimgApi.ExportImage(wimImageHandle, dstWimHandle, WimExportImageOptions.AllowDuplicates);
                }
                catch (Exception e)
                {
                    progressCallback?.Invoke("[EXCEPTION] Message: " + e.Message, 0, true);
                    progressCallback?.Invoke("[EXCEPTION] StackTrace: " + e.StackTrace, 0, true);
                    return false;
                }
                finally
                {
                    // Be sure to unregister the callback method
                    //
                    WimgApi.UnregisterMessageCallback(wimHandle, callback2);
                }
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool ExtractFileFromImage(string wimFile, int imageIndex, string fileToExtract, string destination)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                            wimFile,
                            WimFileAccess.Read,
                            WimCreationDisposition.OpenExisting,
                            wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase) ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                            WimCompressionType.None);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                using WimHandle wimImageHandle = WimgApi.LoadImage(wimHandle, imageIndex);
                WimgApi.ExtractImagePath(wimImageHandle, fileToExtract, destination);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool GetWIMImageInformation(string wimFile, int imageIndex, out WIMInformationXML.IMAGE image)
        {
            image = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Read,
                    WimCreationDisposition.OpenExisting,
                    WimCreateFileOptions.Chunked,
                    WimCompressionType.None);
                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                using WimHandle imageHandle = WimgApi.LoadImage(wimHandle, imageIndex);
                string wiminfo = WimgApi.GetImageInformationAsString(imageHandle);
                image = WIMInformationXML.DeserializeIMAGE(wiminfo);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool GetWIMInformation(string wimFile, out WIMInformationXML.WIM wim)
        {
            wim = null;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Read,
                    WimCreationDisposition.OpenExisting,
                    WimCreateFileOptions.Chunked,
                    WimCompressionType.None);
                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                string wiminfo = WimgApi.GetImageInformationAsString(wimHandle);
                wim = WIMInformationXML.DeserializeWIM(wiminfo);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool MarkImageAsBootable(string wimFile, int imageIndex)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                            wimFile,
                            WimFileAccess.Write,
                            WimCreationDisposition.OpenExisting,
                            wimFile.EndsWith(".esd", StringComparison.InvariantCultureIgnoreCase) ? WimCreateFileOptions.Chunked : WimCreateFileOptions.None,
                            WimCompressionType.None);

                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                WimgApi.SetBootImage(wimHandle, imageIndex);
            }
            catch
            {
                return false;
            }
            return true;
        }

        public bool SetWIMImageInformation(string wimFile, int imageIndex, WIMInformationXML.IMAGE image)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                using WimHandle wimHandle = WimgApi.CreateFile(
                    wimFile,
                    WimFileAccess.Write,
                    WimCreationDisposition.OpenExisting,
                    WimCreateFileOptions.Chunked,
                    WimCompressionType.None);
                // Always set a temporary path
                //
                WimgApi.SetTemporaryPath(wimHandle, Path.GetTempPath());

                using WimHandle imageHandle = WimgApi.LoadImage(wimHandle, imageIndex);
                string img = WIMInformationXML.SerializeIMAGE(image);
                WimgApi.SetImageInformation(imageHandle, img);
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}
