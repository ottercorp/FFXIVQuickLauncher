using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using Serilog;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XIVLauncher.Common.Util;
using System.Net;

namespace XIVLauncher.Common.Dalamud
{
    public class AssetManager
    {
        private const string ASSET_STORE_URL = "https://aonyx.ffxiv.wang/Dalamud/Asset/Meta";

        internal class AssetInfo
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("assets")]
            public IReadOnlyList<Asset> Assets { get; set; }

            [JsonPropertyName("packageUrl")]
            public string PackageUrl { get; set; }

            public class Asset
            {
                [JsonPropertyName("url")]
                public string Url { get; set; }

                [JsonPropertyName("fileName")]
                public string FileName { get; set; }

                [JsonPropertyName("hash")]
                public string Hash { get; set; }
            }
        }

        public static async Task<(DirectoryInfo AssetDir, int Version)> EnsureAssets(DalamudUpdater updater, DirectoryInfo baseDir)
        {
            using var metaClient = new HttpClient(new HttpClientHandler
            {
                // Don't Remove!!!
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromMinutes(4),
            };

            metaClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
            };
            metaClient.DefaultRequestHeaders.Add("User-Agent", $"Wget/1.21.1 (linux-gnu) XL/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            metaClient.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate");

            using var sha1 = SHA1.Create();

            Log.Verbose("[DASSET] Starting asset download");

            var (isRefreshNeeded, info) = await CheckAssetRefreshNeeded(metaClient, baseDir);

            // NOTE(goat): We should use a junction instead of copying assets to a new folder. There is no C# API for junctions in .NET Framework.

            var currentDir = new DirectoryInfo(Path.Combine(baseDir.FullName, info.Version.ToString()));
            var devDir = new DirectoryInfo(Path.Combine(baseDir.FullName, "dev"));

            // // If we don't need a refresh, let's check if all hashes are good
            // if (!isRefreshNeeded)
            // {
            var assetFileDownloadList = new List<AssetInfo.Asset>();

            foreach (var entry in info.Assets)
            {
                var filePath = Path.Combine(currentDir.FullName, entry.FileName);

                if (!File.Exists(filePath)) {
                    Log.Error("[DASSET] {0} not found locally", entry.FileName);
                    assetFileDownloadList.Add(entry);
                    //break;
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Hash))
                    continue;

                try {
                    using var file = File.OpenRead(filePath);
                    var fileHash = sha1.ComputeHash(file);
                    var stringHash = BitConverter.ToString(fileHash).Replace("-", "");

                    if (stringHash != entry.Hash) {
                        Log.Error("[DASSET] {0} has {1}, remote {2}, need refresh", entry.FileName, stringHash, entry.Hash);
                        assetFileDownloadList.Add(entry);
                        //break;
                    }
                }
                catch (Exception ex) {
                    Log.Error(ex, "[DASSET] Could not read asset");
                    assetFileDownloadList.Add(entry);
                    continue;
                }
            }

            foreach (var entry in assetFileDownloadList)
            {
                var oldFilePath = Path.Combine(devDir.FullName, entry.FileName);
                var newFilePath = Path.Combine(assetsDir.FullName, entry.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)!);

                try {
                    if (File.Exists(oldFilePath)) {
                        using var file = File.OpenRead(oldFilePath);
                        var fileHash = sha1.ComputeHash(file);
                        var stringHash = BitConverter.ToString(fileHash).Replace("-", "");
                        if (stringHash == entry.Hash) {
                            Log.Verbose("[DASSET] Get asset from old file: {0}", entry.FileName);
                            File.Copy(oldFilePath,newFilePath,true);
                            isRefreshNeeded = true;
                            continue;
                        }                   
                    }
                }
                catch (Exception ex) {
                    Log.Error(ex, "[DASSET] Could not copy from old asset: {0}",entry.FileName);
                }

                try {
                    Log.Information("[DASSET] Downloading {0} to {1}...", entry.Url, entry.FileName);
                    await updater.DownloadFile(entry.Url, newFilePath, TimeSpan.FromMinutes(4));
                    isRefreshNeeded = true;
                }
                catch (Exception ex) {
                    Log.Error(ex, "[DASSET] Could not download old asset: {0}",entry.FileName);
                }

            }

            if (isRefreshNeeded) {
                try {
                    DeleteAndRecreateDirectory(devDir);
                    CopyFilesRecursively(assetsDir, devDir);
                }
                catch (Exception ex) {
                    Log.Error(ex, "[DASSET] Could not copy to dev dir");
                }
                SetLocalAssetVer(baseDir, info.Version);
            }

            // //Global backup
            // foreach (var entry in assetFileDownloadList)
            // {
            //     PlatformHelpers.DeleteAndRecreateDirectory(currentDir);

            //     // Wait for it to be gone
            //     Thread.Sleep(1000);

            //     var packageUrl = info.PackageUrl;

            //     var tempPath = PlatformHelpers.GetTempFileName();

            //     if (File.Exists(tempPath))
            //         File.Delete(tempPath);

            //     await updater.DownloadFile(packageUrl, tempPath, TimeSpan.FromMinutes(4));

            //     using (var packageStream = File.OpenRead(tempPath))
            //     using (var packageArc = new ZipArchive(packageStream, ZipArchiveMode.Read))
            //     {
            //         packageArc.ExtractToDirectory(currentDir.FullName);
            //     }

            //     try
            //     {
            //         PlatformHelpers.DeleteAndRecreateDirectory(devDir);
            //         PlatformHelpers.CopyFilesRecursively(currentDir, devDir);
            //     }
            //     catch (Exception ex) {
            //         Log.Error(ex, "[DASSET] Could not copy to dev dir");
            //     }
            //     SetLocalAssetVer(baseDir, info.Version);
            // }

            Log.Verbose("[DASSET] Assets OK at {0}", currentDir.FullName);

            try
            {
                CleanUpOld(baseDir, devDir, currentDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DASSET] Could not clean up old assets");
            }

            return (currentDir, info.Version);
        }

        private static string GetAssetVerPath(DirectoryInfo baseDir)
        {
            return Path.Combine(baseDir.FullName, "asset.ver");
        }

        /// <summary>
        ///     Check if an asset update is needed. When this fails, just return false - the route to github
        ///     might be bad, don't wanna just bail out in that case
        /// </summary>
        /// <param name="baseDir">Base directory for assets</param>
        /// <returns>Update state</returns>
        private static async Task<(bool isRefreshNeeded, AssetInfo info)> CheckAssetRefreshNeeded(HttpClient client, DirectoryInfo baseDir)
        {
            var localVerFile = GetAssetVerPath(baseDir);
            var localVer = 0;

            try
            {
                if (File.Exists(localVerFile))
                    localVer = int.Parse(File.ReadAllText(localVerFile));
            }
            catch (Exception ex)
            {
                // This means it'll stay on 0, which will redownload all assets - good by me
                Log.Error(ex, "[DASSET] Could not read asset.ver");
            }

            var remoteVer = JsonSerializer.Deserialize<AssetInfo>(await client.GetStringAsync(ASSET_STORE_URL),new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Log.Verbose("[DASSET] Ver check - local:{0} remote:{1}", localVer, remoteVer.Version);

            var needsUpdate = remoteVer.Version > localVer;

            return (needsUpdate, remoteVer);
        }

        private static void SetLocalAssetVer(DirectoryInfo baseDir, int version)
        {
            try
            {
                var localVerFile = GetAssetVerPath(baseDir);
                File.WriteAllText(localVerFile, version.ToString());
            }
            catch (Exception e)
            {
                Log.Error(e, "[DASSET] Could not write local asset version");
            }
        }

        private static void CleanUpOld(DirectoryInfo baseDir, DirectoryInfo devDir, DirectoryInfo currentDir)
        {
            if (GameHelpers.CheckIsGameOpen())
                return;

            if (!baseDir.Exists)
                return;

            foreach (var toDelete in baseDir.GetDirectories())
            {
                if (toDelete.Name != devDir.Name && toDelete.Name != currentDir.Name)
                {
                    toDelete.Delete(true);
                    Log.Verbose("[DASSET] Cleaned out {Path}", toDelete.FullName);
                }
            }

            Log.Verbose("[DASSET] Finished cleaning");
        }
    }
}