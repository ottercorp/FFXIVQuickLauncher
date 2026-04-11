using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using XIVLauncher.Common;
using XIVLauncher.Windows;
using XIVLauncher.Xaml;
using ZipArchive = System.IO.Compression.ZipArchive;

namespace XIVLauncher.Support
{
    public static class PackGenerator
    {
        public static string SavePack()
        {
            var outFile = new FileInfo(Path.Combine(Paths.RoamingPath, $"trouble-{DateTime.Now:yyyyMMddhhmmss}.tspack"));
            using var archive = ZipFile.Open(outFile.FullName, ZipArchiveMode.Create);

            var troubleBytes = Encoding.UTF8.GetBytes(Troubleshooting.GetTroubleshootingJson());
            var troubleEntry = archive.CreateEntry("trouble.json").Open();
            troubleEntry.Write(troubleBytes, 0, troubleBytes.Length);
            troubleEntry.Close();

            var xlLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "output.log"));
            var patcherLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "patcher.log"));
            var dalamudLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "dalamud.log"));
            var dalamudInjectorLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "dalamud.injector.log"));
            var dalamudBootLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "dalamud.boot.log"));  
            var ariaLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "aria.log"));
            var argReaderLogFile = new FileInfo(Path.Combine(Paths.RoamingPath, "argReader.log"));

            AddIfExist(xlLogFile, archive);
            AddIfExist(patcherLogFile, archive);
            AddIfExist(dalamudLogFile, archive);
            AddIfExist(dalamudInjectorLogFile, archive);
            AddIfExist(dalamudBootLogFile, archive);
            AddIfExist(ariaLogFile, archive);
            AddIfExist(argReaderLogFile, archive);

            return outFile.FullName;
        }

        private static void AddIfExist(FileInfo file, ZipArchive zip)
        {
            if (file.Exists)
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var entry = zip.CreateEntry(file.Name);
                using var entryStream = entry.Open();
                stream.CopyTo(entryStream);
                //zip.CreateEntryFromFile(file.FullName, file.Name);
            }
        }

        public static void PackAndShowMessage()
        {
            var packFullName = SavePack();
            // Use "explorer.exe" to open the folder and select the file
            Process.Start("explorer.exe", $"/select,\"{Path.GetFullPath(packFullName)}\"");
            var message = $"日志文件已打包: {packFullName}";
            CustomMessageBox.Show(message, "Troubleshooting", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
