using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Unix.Compatibility;

public static class Dxvk
{
#if WINE_XIV_MACOS
    // private const string DXVK_DOWNLOAD = "https://github.com/Gcenx/DXVK-macOS/releases/download/v1.10.3-20230507/dxvk-macOS-async-v1.10.3-20230507.tar.gz";
    private const string DXVK_DOWNLOAD = "https://s3.ffxiv.wang/xlcore/deps/dxvk/osx/dxvk-macOS-async-v1.10.3-20230507.tar.gz";
    private const string DXVK_NAME = "dxvk-macOS-async-v1.10.3-20230507";
#else
    // private const string DXVK_DOWNLOAD = "https://github.com/Sporif/dxvk-async/releases/download/1.10.1/dxvk-async-1.10.1.tar.gz";
    private const string DXVK_DOWNLOAD = "https://s3.ffxiv.wang/xlcore/deps/dxvk/linux/dxvk-async-1.10.1.tar.gz";
    private const string DXVK_NAME = "dxvk-async-1.10.1";
#endif

    public static async Task InstallDxvk(DirectoryInfo prefix, DirectoryInfo installDirectory)
    {
        var dxvkPath = Path.Combine(installDirectory.FullName, DXVK_NAME, "x64");

        if (!Directory.Exists(dxvkPath))
        {
            Log.Information("DXVK does not exist, downloading");
            await DownloadDxvk(installDirectory).ConfigureAwait(false);
        }

        var system32 = Path.Combine(prefix.FullName, "drive_c", "windows", "system32");
        var files = Directory.GetFiles(dxvkPath);

        foreach (string fileName in files)
        {
            File.Copy(fileName, Path.Combine(system32, Path.GetFileName(fileName)), true);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            File.Copy(
                Path.Combine(Paths.ResourcesPath, "ffxiv_dx11.dxvk-cache-base"),
                Path.Combine(prefix.FullName, "drive_c", "ffxiv_dx11.dxvk-cache"),
                true
            );
        }
    }

    private static async Task DownloadDxvk(DirectoryInfo installDirectory)
    {
        using var client = new HttpClient();
        var tempPath = Path.GetTempFileName();

        File.WriteAllBytes(tempPath, await client.GetByteArrayAsync(DXVK_DOWNLOAD));
        PlatformHelpers.Untar(tempPath, installDirectory.FullName);

        File.Delete(tempPath);
    }

    public enum DxvkHudType
    {
        [SettingsDescription("None", "Show nothing")]
        None,

        [SettingsDescription("FPS", "Only show FPS")]
        Fps,

        [SettingsDescription("Full", "Show everything")]
        Full,
    }
}