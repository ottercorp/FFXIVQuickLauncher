using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Common.Unix.Compatibility;

namespace XIVLauncher.Common.Unix;

public class UnixGameRunner : IGameRunner
{
    private readonly CompatibilityTools compatibility;
    private readonly DalamudLauncher dalamudLauncher;
    private readonly bool dalamudOk;

    public UnixGameRunner(CompatibilityTools compatibility, DalamudLauncher dalamudLauncher, bool dalamudOk)
    {
        this.compatibility = compatibility;
        this.dalamudLauncher = dalamudLauncher;
        this.dalamudOk = dalamudOk;
    }

    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DpiAwareness dpiAwareness)
    {
        var isSteam = environment.ContainsKey("SteamAppId");

        if (isSteam)
        {
            // For Steam, we need to use the compatibility tool's run command
            var steamCompatToolPath = environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"];
            var steamCompatToolCommand = Path.Combine(steamCompatToolPath, "steamclient.so");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = steamCompatToolCommand,
                Arguments = $"\"{path}\" {arguments}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };

            foreach (var envVar in environment)
            {
                processStartInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
            }

            return Process.Start(processStartInfo);
        }

        // Non-Steam launch process
        if (dalamudOk)
        if (dalamudOk)
        {
            return this.dalamudLauncher.Run(new FileInfo(path), arguments, environment);
        }
        else
        {
            return compatibility.RunInPrefix($"\"{path}\" {arguments}", workingDirectory, environment, writeLog: true);
        }
    }
}