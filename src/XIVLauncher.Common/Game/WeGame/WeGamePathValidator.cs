using System.IO;
using Newtonsoft.Json.Linq;

namespace XIVLauncher.Common.Game.WeGame
{
    public static class WeGamePathValidator
    {
        public const int FfxivWeGameGameId = 2000340;

        public static bool IsValidSdologinDir(string sdologinDir)
        {
            if (string.IsNullOrEmpty(sdologinDir)) return false;
            if (!Directory.Exists(sdologinDir)) return false;
            if (!File.Exists(Path.Combine(sdologinDir, "sdologin.exe"))) return false;

            try
            {
                var root = Path.GetFullPath(Path.Combine(sdologinDir, "..", ".."));
                return IsValidGameRoot(root);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsValidGameRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            var marker = Path.Combine(root, "rail_files", "rail_game_identify.json");
            if (!File.Exists(marker)) return false;
            try
            {
                var json = JObject.Parse(File.ReadAllText(marker));
                return (int?)json["game_id"] == FfxivWeGameGameId;
            }
            catch
            {
                return false;
            }
        }

        public static string DeriveSdologinDir(string gameRoot)
            => Path.Combine(gameRoot, "sdo", "sdologin");
    }
}
