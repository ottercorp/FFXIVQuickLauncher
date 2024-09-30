using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common.Util;

#if FLATPAK
#warning THIS IS A FLATPAK BUILD!!!
#endif

namespace XIVLauncher.Common.Unix.Compatibility;

public class CompatibilityTools
{
    private DirectoryInfo toolDirectory;
    private DirectoryInfo dxvkDirectory;
    private DirectoryInfo dxmtDirectory;

    private StreamWriter logWriter;

#if WINE_XIV_ARCH_LINUX
    // private const string WINE_XIV_RELEASE_URL = "https://github.com/goatcorp/wine-xiv-git/releases/download/8.5.r4.g4211bac7/wine-xiv-staging-fsync-git-arch-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_URL = "https://s3.ffxiv.wang/xlcore/deps/wine/arch/wine-xiv-staging-fsync-git-arch-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_NAME = "wine-xiv-staging-fsync-git-8.5.r4.g4211bac7";
#elif WINE_XIV_FEDORA_LINUX
    // private const string WINE_XIV_RELEASE_URL = "https://github.com/goatcorp/wine-xiv-git/releases/download/8.5.r4.g4211bac7/wine-xiv-staging-fsync-git-fedora-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_URL = "https://s3.ffxiv.wang/xlcore/deps/wine/fedora/wine-xiv-staging-fsync-git-fedora-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_NAME = "wine-xiv-staging-fsync-git-8.5.r4.g4211bac7";
#elif WINE_XIV_MACOS
    // Wine from https://softwareupdate.xivmac.com/sites/default/files/update_data/XIV%20on%20Mac4.17.1.tar.xz;
    private const string WINE_XIV_RELEASE_URL = "https://s3.ffxiv.wang/xlcore/deps/wine/osx/xom-4.17.1/wine.tar.gz";
    private const string WINE_XIV_RELEASE_NAME = "wine";
#else
    // private const string WINE_XIV_RELEASE_URL = "https://github.com/goatcorp/wine-xiv-git/releases/download/8.5.r4.g4211bac7/wine-xiv-staging-fsync-git-ubuntu-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_URL = "https://s3.ffxiv.wang/xlcore/deps/wine/ubuntu/wine-xiv-staging-fsync-git-ubuntu-8.5.r4.g4211bac7.tar.xz";
    private const string WINE_XIV_RELEASE_NAME = "wine-xiv-staging-fsync-git-8.5.r4.g4211bac7";
#endif

    private const string SD_WINE_XIV_RELEASE_URL = "https://s3.ffxiv.wang/xlcore/deps/wine/ubuntu/wine-xiv-staging-fsync-git-ubuntu-8.5.r4.g4211bac7.tar.xz";
    private const string SD_WINE_XIV_RELEASE_NAME = "wine-xiv-staging-fsync-git-8.5.r4.g4211bac7";

    public bool IsToolReady { get; private set; }

    public WineSettings Settings { get; private set; }
    public static bool IsSteamDeckHardware => Directory.Exists("/home/deck");

    private string WineBinPath => Settings.StartupType == WineStartupType.Managed
        ? Path.Combine(toolDirectory.FullName, IsSteamDeckHardware ? SD_WINE_XIV_RELEASE_NAME : WINE_XIV_RELEASE_NAME, "bin")
        : Settings.CustomBinPath;

    private string WineLibPath => Settings.StartupType == WineStartupType.Managed
        ? Path.Combine(toolDirectory.FullName, WINE_XIV_RELEASE_NAME, "lib")
        : Path.Combine(Settings.CustomBinPath, "..", "lib");

    // private string MoltenVkPath => Path.Combine(Paths.ResourcesPath, "MoltenVK");
    private string Wine64Path => Path.Combine(WineBinPath, "wine64");
    private string WineServerPath => Path.Combine(WineBinPath, "wineserver");

    public bool IsToolDownloaded => File.Exists(Wine64Path) && Settings.Prefix.Exists;

    private readonly Dxvk.DxvkHudType hudType;
    private readonly bool gamemodeOn;
    private readonly string dxvkAsyncOn;
    private readonly int dxvkFrameLimit;
    private readonly bool dxmtEnabled;
    private readonly bool metalFxEnabled;
    private readonly int metalFxFactor;

    public CompatibilityTools(
        WineSettings wineSettings,
        Dxvk.DxvkHudType hudType,
        bool? gamemodeOn,
        bool? dxvkAsyncOn,
        int dxvkFrameLimit,
        DirectoryInfo toolsFolder,
        bool? dxmtEnabled,
        bool? metalFxEnabled,
        int? metalFxFactor)
    {
        this.Settings = wineSettings;
        this.hudType = hudType;
        this.gamemodeOn = gamemodeOn ?? false;
        this.dxvkAsyncOn = (dxvkAsyncOn ?? false) ? "1" : "0";
        this.dxvkFrameLimit = dxvkFrameLimit;
        this.dxmtEnabled = dxmtEnabled ?? false;
        this.metalFxEnabled = metalFxEnabled ?? false;
        this.metalFxFactor = metalFxFactor ?? 2;

        this.toolDirectory = new DirectoryInfo(Path.Combine(toolsFolder.FullName, "beta"));
        this.dxvkDirectory = new DirectoryInfo(Path.Combine(toolsFolder.FullName, "dxvk"));
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            this.dxmtDirectory = new DirectoryInfo(Path.Combine(toolsFolder.FullName, "dxmt"));
        }

        this.logWriter = new StreamWriter(wineSettings.LogFile.FullName);

        if (wineSettings.StartupType == WineStartupType.Managed)
        {
            if (!this.toolDirectory.Exists)
                this.toolDirectory.Create();

            if (!this.dxvkDirectory.Exists)
                this.dxvkDirectory.Create();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (!this.dxmtDirectory!.Exists)
                    this.dxmtDirectory.Create();
            }
        }

        if (!wineSettings.Prefix.Exists)
            wineSettings.Prefix.Create();
    }

    public async Task EnsureTool(DirectoryInfo tempPath)
    {
        if (!File.Exists(Wine64Path))
        {
            Log.Information("Compatibility tool does not exist, downloading");
            await DownloadTool(tempPath).ConfigureAwait(false);
            Log.Information("Download compatibility tool finished.");
        }

        EnsurePrefix();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (this.dxmtEnabled)
            {
                await Dxmt.InstallDxmt(Settings.Prefix, dxmtDirectory).ConfigureAwait(false);
            }
            else
            {
                await Dxmt.UninstallDxmt(Settings.Prefix, dxmtDirectory).ConfigureAwait(false);
                await Dxvk.InstallDxvk(Settings.Prefix, dxvkDirectory).ConfigureAwait(false);
            }
        }
        else
        {
            await Dxvk.InstallDxvk(Settings.Prefix, dxvkDirectory).ConfigureAwait(false);
        }

        IsToolReady = true;
    }

    private async Task DownloadTool(DirectoryInfo tempPath)
    {
        using var client = new HttpClient();
        var tempFilePath = Path.Combine(tempPath.FullName, $"{Guid.NewGuid()}");

        var wineUrl = IsSteamDeckHardware ? SD_WINE_XIV_RELEASE_URL : WINE_XIV_RELEASE_URL;

        var fileBytes = await client
            .GetByteArrayAsync(wineUrl)
            .ConfigureAwait(false);

        Log.Information("Downloaded wine from {Path}", wineUrl);

        await File.WriteAllBytesAsync(tempFilePath, fileBytes).ConfigureAwait(false);

        Log.Information("Wine saved to {Path}", tempFilePath);

        PlatformHelpers.Untar(tempFilePath, this.toolDirectory.FullName);

        Log.Information("Wine unzipped to {Path}", tempFilePath);

        // if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        // {
        //     var bundledMvkLibPath = Path.Combine(WineLibPath, "libMoltenVK.dylib");
        //     Log.Information("Removing {Path}", bundledMvkLibPath);
        //     if (File.Exists(bundledMvkLibPath))
        //     {
        //         File.Delete(bundledMvkLibPath);
        //         Log.Information("Removed {Path}", bundledMvkLibPath);
        //     }
        // }

        Log.Information("Compatibility tool successfully extracted to {Path}", this.toolDirectory.FullName);

        File.Delete(tempFilePath);
    }

    private void ResetPrefix()
    {
        Settings.Prefix.Refresh();

        if (Settings.Prefix.Exists)
            Settings.Prefix.Delete(true);

        Settings.Prefix.Create();
        EnsurePrefix();
    }

    public void EnsurePrefix()
    {
        RunInPrefix("cmd /c dir %userprofile%/Documents > nul").WaitForExit();
    }

    public Process RunInPrefix(string command, string workingDirectory = "", IDictionary<string, string> environment = null, bool redirectOutput = false, bool writeLog = false, bool wineD3D = false)
    {
        var psi = new ProcessStartInfo(Wine64Path);
        psi.Arguments = command;

        Log.Verbose("Running in prefix: {FileName} {Arguments}", psi.FileName, command);
        return RunInPrefix(psi, workingDirectory, environment, redirectOutput, writeLog, wineD3D);
    }

    public Process RunInPrefix(string[] args, string workingDirectory = "", IDictionary<string, string> environment = null, bool redirectOutput = false, bool writeLog = false, bool wineD3D = false)
    {
        var psi = new ProcessStartInfo(Wine64Path);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        Log.Verbose("Running in prefix: {FileName} {Arguments}", psi.FileName, psi.ArgumentList.Aggregate(string.Empty, (a, b) => a + " " + b));
        return RunInPrefix(psi, workingDirectory, environment, redirectOutput, writeLog, wineD3D);
    }

    private void MergeDictionaries(StringDictionary a, IDictionary<string, string> b)
    {
        if (b is null)
            return;

        foreach (var keyValuePair in b)
        {
            if (a.ContainsKey(keyValuePair.Key))
                a[keyValuePair.Key] = keyValuePair.Value;
            else
                a.Add(keyValuePair.Key, keyValuePair.Value);
        }
    }

    private Process RunInPrefix(ProcessStartInfo psi, string workingDirectory, IDictionary<string, string> environment, bool redirectOutput, bool writeLog, bool wineD3D)
    {
        psi.RedirectStandardOutput = redirectOutput;
        psi.RedirectStandardError = writeLog;
        psi.UseShellExecute = false;
        psi.WorkingDirectory = workingDirectory;

        var wineEnviromentVariables = new Dictionary<string, string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            wineEnviromentVariables.Add("LANG", "en_US"); // need this for Dalamud on non-latin locale
            wineEnviromentVariables.Add("MVK_ALLOW_METAL_FENCES", "1");
            wineEnviromentVariables.Add("MVK_CONFIG_FULL_IMAGE_VIEW_SWIZZLE", "1");
            wineEnviromentVariables.Add("MVK_CONFIG_RESUME_LOST_DEVICE", "1");
            wineEnviromentVariables.Add("DOTNET_EnableWriteXorExecute", "0");
            // DXMT
            wineEnviromentVariables.Add("DXMT_CONFIG", $"d3d11.metalSpatialUpscaleFactor={this.metalFxFactor};d3d11.preferredMaxFrameRate={this.dxvkFrameLimit};");
            wineEnviromentVariables.Add("DXMT_METALFX_SPATIAL_SWAPCHAIN", this.metalFxEnabled ? "1" : "0");
        }

        wineEnviromentVariables.Add("WINEPREFIX", Settings.Prefix.FullName);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            wineEnviromentVariables.Add("WINEDLLOVERRIDES", $"msquic=,mscoree=n,b;d3d9,d3d11,d3d10core,dxgi={(wineD3D ? "b" : "n")}");
        }
        else
        {
            wineEnviromentVariables.Add("WINEDLLOVERRIDES", $"msquic=,mscoree=n,b;d3d11={(wineD3D ? "b" : "n")};dxgi=n,b");
        }

        if (!string.IsNullOrEmpty(Settings.DebugVars))
        {
            wineEnviromentVariables.Add("WINEDEBUG", Settings.DebugVars);
        }

        if (!string.IsNullOrEmpty(Settings.Env))
        {
            var envList = Settings.Env.Split(';');

            foreach (var env in envList)
            {
                var kvList = env.Split('=');
                wineEnviromentVariables.Add(kvList[0], kvList[1]);
            }
        }

        wineEnviromentVariables.Add("XL_WINEONLINUX", "true");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            wineEnviromentVariables.Add("XL_WINEONMAC", "true");
        }

        string ldPreload = Environment.GetEnvironmentVariable("LD_PRELOAD") ?? "";

        string dxvkHud = hudType switch
        {
            Dxvk.DxvkHudType.None => "0",
            Dxvk.DxvkHudType.Fps => "fps",
            Dxvk.DxvkHudType.Full => "full",
            _ => throw new ArgumentOutOfRangeException()
        };

        if (this.gamemodeOn && !ldPreload.Contains("libgamemodeauto.so.0"))
        {
            ldPreload = ldPreload == "" ? "libgamemodeauto.so.0" : ldPreload + ":libgamemodeauto.so.0";
        }

        wineEnviromentVariables.Add("DXVK_HUD", dxvkHud);
        wineEnviromentVariables.Add("DXVK_ASYNC", dxvkAsyncOn);
        wineEnviromentVariables.Add("DXVK_STATE_CACHE_PATH", "C:\\");
        wineEnviromentVariables.Add("DXVK_LOG_PATH", "C:\\");
        wineEnviromentVariables.Add("DXVK_CONFIG_FILE", "C:\\ffxiv_dx11.conf");
        wineEnviromentVariables.Add("WINEESYNC", Settings.EsyncOn);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            wineEnviromentVariables.Add("WINEMSYNC", Settings.MsyncOn);
        }
        else
        {
            wineEnviromentVariables.Add("WINEFSYNC", Settings.FsyncOn);
        }

        if (dxvkFrameLimit != 0)
        {
            wineEnviromentVariables.Add("DXVK_FRAME_RATE", dxvkFrameLimit.ToString());
        }

        wineEnviromentVariables.Add("LD_PRELOAD", ldPreload);

        MergeDictionaries(psi.EnvironmentVariables, wineEnviromentVariables);
        MergeDictionaries(psi.EnvironmentVariables, environment);
        
#if DEBUG
        Log.Debug($"Running in prefix: {psi.FileName} {psi.Arguments}");
        Log.Debug("with wineEnviromentVariables:");
        foreach (string k in psi.EnvironmentVariables.Keys)
        {
            Log.Debug(k + "=" + psi.EnvironmentVariables[k]);
        }
#endif

#if FLATPAK_NOTRIGHTNOW
        psi.FileName = "flatpak-spawn";

        psi.ArgumentList.Insert(0, "--host");
        psi.ArgumentList.Insert(1, Wine64Path);

        foreach (KeyValuePair<string, string> envVar in wineEnviromentVariables)
        {
            psi.ArgumentList.Insert(1, $"--env={envVar.Key}={envVar.Value}");
        }

        if (environment != null)
        {
            foreach (KeyValuePair<string, string> envVar in environment)
            {
                psi.ArgumentList.Insert(1, $"--env=\"{envVar.Key}\"=\"{envVar.Value}\"");
            }
        }
#endif

        Process helperProcess = new();
        helperProcess.StartInfo = psi;
        helperProcess.ErrorDataReceived += new DataReceivedEventHandler((_, errLine) =>
        {
            if (String.IsNullOrEmpty(errLine.Data))
                return;

            try
            {
                logWriter.WriteLine(errLine.Data);
                Console.Error.WriteLine(errLine.Data);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException ||
                                       ex is OverflowException ||
                                       ex is IndexOutOfRangeException)
            {
                // very long wine log lines get chopped off after a (seemingly) arbitrary limit resulting in strings that are not null terminated
                // logWriter.WriteLine("Error writing Wine log line:");
                // logWriter.WriteLine(ex.Message);
            }
        });

        helperProcess.Start();
        if (writeLog)
            helperProcess.BeginErrorReadLine();

        return helperProcess;
    }

    public Int32[] GetProcessIds(string executableName)
    {
        var wineDbg = RunInPrefix("winedbg --command \"info proc\"", redirectOutput: true);
        var output = wineDbg.StandardOutput.ReadToEnd();
        var matchingLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains(executableName));
        return matchingLines.Select(l => int.Parse(l.Substring(1, 8), System.Globalization.NumberStyles.HexNumber)).ToArray();
    }

    public Int32 GetProcessId(string executableName)
    {
        return GetProcessIds(executableName).FirstOrDefault();
    }

    public Int32 GetUnixProcessId(Int32 winePid)
    {
        var wineDbg = RunInPrefix("winedbg --command \"info procmap\"", redirectOutput: true);
        var output = wineDbg.StandardOutput.ReadToEnd();
        if (output.Contains("syntax error\n"))
            return 0;
        var matchingLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(
            l =>
            {
                if (int.TryParse(l.Substring(1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int pid))
                {
                    return pid == winePid;
                }

                return false;
            });
        var unixPids = matchingLines.Select(l => int.Parse(l.Substring(10, 8), System.Globalization.NumberStyles.HexNumber)).ToArray();
        return unixPids.FirstOrDefault();
    }

    public string UnixToWinePath(string unixPath)
    {
        var launchArguments = new string[] { "winepath", "--windows", unixPath };
        var winePath = RunInPrefix(launchArguments, redirectOutput: true);
        var output = winePath.StandardOutput.ReadToEnd();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
    }

    public void AddRegistryKey(string key, string value, string data)
    {
        var args = new string[] { "reg", "add", key, "/v", value, "/d", data, "/f" };
        var wineProcess = RunInPrefix(args);
        wineProcess.WaitForExit();
    }

    public void Kill()
    {
        var psi = new ProcessStartInfo(WineServerPath)
        {
            Arguments = "-k"
        };
        psi.EnvironmentVariables.Add("WINEPREFIX", Settings.Prefix.FullName);

        Process.Start(psi);
    }
}
