using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace XIVLauncher.Common
{
    public class Paths
    {
        static Paths()
        {
            RoamingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");
            CheckPath();
        }

        public static void CheckPath()
        {
            if (!Directory.Exists(Path.Combine(RoamingPath, "addon")))
            {
                Log.Warning($"Moving Roaming to AppData");
                var oldRoamingPath = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent!.FullName, "Roaming");
                if (!Directory.Exists(oldRoamingPath)) return;

                Directory.CreateDirectory(RoamingPath);
                Copy(oldRoamingPath, RoamingPath);
                //Directory.Delete(oldRoamingPath, true);
            }
        }

        private static readonly List<string> Needed = ["addon", "backups", "dalamudAssets", /*"devPlugins",*/ "installedPlugins", "pluginConfigs", "runtime"];

        private static void Copy(string sourcePath, string destPath)
        {
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var dest = Path.Combine(destPath, Path.GetFileName(file));
                File.Copy(file, dest, false);
            }

            foreach (var directory in Directory.GetDirectories(sourcePath))
            {
                if (sourcePath == Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent!.FullName, "Roaming"))
                {
                    if (!Needed.Contains(Path.GetFileName(directory)))
                        continue;
                }

                var destDir = Path.Combine(destPath, Path.GetFileName(directory));
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                Copy(directory, destDir);
            }
        }

        public static string RoamingPath { get; private set; }

        public static string ResourcesPath => Path.Combine(AppContext.BaseDirectory, "Resources");

        public static void OverrideRoamingPath(string path)
        {
            RoamingPath = Environment.ExpandEnvironmentVariables(path);
        }
    }
}
