using System;
using System.IO;

namespace XIVLauncher.Common
{
    public class Paths
    {
        static Paths()
        {
            RoamingPath = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent.FullName, "Roaming");
        }

        public static string RoamingPath { get; private set; }

        public static string ResourcesPath => Path.Combine(AppContext.BaseDirectory, "Resources");

        public static void OverrideRoamingPath(string path)
        {
            RoamingPath = Environment.ExpandEnvironmentVariables(path);
        }
    }
}
