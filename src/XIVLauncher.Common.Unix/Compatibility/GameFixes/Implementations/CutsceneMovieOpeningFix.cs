using System.IO;
using System.Text.RegularExpressions;
using Serilog;

namespace XIVLauncher.Common.Unix.Compatibility.GameFixes.Implementations;

public class CutsceneMovieOpeningFix : GameFix
{
    DirectoryInfo GameDirectory { get; set; }
    public CutsceneMovieOpeningFix(DirectoryInfo gameDirectory, DirectoryInfo configDirectory, DirectoryInfo winePrefixDirectory, DirectoryInfo tempDirectory)
        : base(gameDirectory, configDirectory, winePrefixDirectory, tempDirectory)
    {
        GameDirectory = gameDirectory;
    }
    
    public override string LoadingTitle => "正在更改 FFXIV.cfg...";

    public override void Apply()
    {
        var ffxivCfgPath = Path.Combine(GameDirectory.FullName, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn", "FFXIV.cfg");
        if (!File.Exists(ffxivCfgPath))
        {
            Log.Error("FFXIV.cfg file not found: " + ffxivCfgPath);
            return;
        }
        // replace CutsceneMovieOpening	0 with CutsceneMovieOpening	1
        var text = File.ReadAllText(ffxivCfgPath);
        var replacedText = Regex.Replace(text, @"CutsceneMovieOpening\s+0", "CutsceneMovieOpening\t1");
        File.WriteAllText(ffxivCfgPath, replacedText);
        Log.Debug("FFXIV.cfg fixed: " + ffxivCfgPath);
    }

}
