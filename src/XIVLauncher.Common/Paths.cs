using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace XIVLauncher.Common
{
    public class Paths
    {
        static Paths()
        {
            RoamingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");

            if (!Directory.Exists(RoamingPath)) Directory.CreateDirectory(RoamingPath);

            if (FirstRun)
            {
                FirstRun = false;
#if WIN32
                DeleteLink();
#endif
                CheckPath();
            }
        }

        private static void DeleteLink()
        {
            DeleteSymbolicLink(RoamingPath);

            foreach (var file in Directory.GetFiles(RoamingPath, "*.*", SearchOption.AllDirectories))
            {
                DeleteSymbolicLink(file);
            }

            foreach (var directory in Directory.GetDirectories(RoamingPath, "*", SearchOption.AllDirectories))
            {
                DeleteSymbolicLink(directory);
            }
        }

        public static void CheckPath()
        {
            if (Directory.Exists(Path.Combine(RoamingPath, "addon"))) return;

            var oldRoamingPath = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent!.FullName, "Roaming");
            if (!Directory.Exists(oldRoamingPath)) return;

            Directory.CreateDirectory(RoamingPath);
            Copy(oldRoamingPath, RoamingPath);
        }

        private static readonly List<string> Needed = ["addon", "backups", "dalamudAssets", /*"devPlugins",*/ "installedPlugins", "pluginConfigs", "runtime"];
        private static readonly bool FirstRun = true;

        private static void Copy(string sourcePath, string destPath)
        {
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var dest = Path.Combine(destPath, Path.GetFileName(file));
                File.Copy(file, dest, true);
                File.Delete(file);
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
                Directory.Delete(directory, true);
            }
        }

        public static string RoamingPath { get; private set; }

        public static string ResourcesPath => Path.Combine(AppContext.BaseDirectory, "Resources");

        public static void OverrideRoamingPath(string path)
        {
            RoamingPath = Environment.ExpandEnvironmentVariables(path);
        }

        #region DeleteLink

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, out WIN32_FILE_ATTRIBUTE_DATA fileData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_FILE_ATTRIBUTE_DATA
        {
            public FileAttributes dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
        }

        public static bool IsSymbolicLink(string path)
        {
            if (GetFileAttributesEx(path, 0, out WIN32_FILE_ATTRIBUTE_DATA fileAttributesData))
            {
                return (fileAttributesData.dwFileAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            return false;
        }

        public static void DeleteSymbolicLink(string path)
        {
            if (IsSymbolicLink(path))
            {
                // 检查路径是文件还是目录，然后删除
                if (Directory.Exists(path))
                {
                    // 如果是目录符号链接
                    Directory.Delete(path);
                    //Console.WriteLine($"Symbolic link directory '{path}' was deleted.");
                }
                else if (File.Exists(path))
                {
                    // 如果是文件符号链接
                    File.Delete(path);
                    //Console.WriteLine($"Symbolic link file '{path}' was deleted.");
                }
            }
        }

        #endregion
    }
}
