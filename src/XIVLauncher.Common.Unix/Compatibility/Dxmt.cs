using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Unix.Compatibility;

public static class Dxmt
{
    private const string DXMT_DOWNLOAD = "https://s3.ffxiv.wang/xlcore/deps/dxmt/xom-4.17.1/dxmt.tar.gz";
    private const string DXMT_NAME = "dxmt";

    public static async Task InstallDxmt(DirectoryInfo prefix, DirectoryInfo installDirectory)
    {
        var dxmtPath = Path.Combine(installDirectory.FullName, DXMT_NAME);

        if (!Directory.Exists(dxmtPath))
        {
            Log.Information("DXMT does not exist, downloading");
            await DownloadDxmt(installDirectory).ConfigureAwait(false);
        }

        var system32 = Path.Combine(prefix.FullName, "drive_c", "windows", "system32");
        var files = Directory.GetFiles(dxmtPath);

        Log.Information("Extracting DXMT files");
        foreach (string fileName in files)
        {
            var destFile = Path.Combine(system32, Path.GetFileName(fileName));
            if (File.Exists(destFile) && !File.Exists(destFile + ".bck"))
            {
                Log.Information($"Backing up {destFile}");
                File.Move(destFile, destFile + ".bck");
            }
            File.Copy(fileName, Path.Combine(system32, Path.GetFileName(fileName)), true);
        }
    }
    
    public static async Task UninstallDxmt(DirectoryInfo prefix, DirectoryInfo installDirectory)
    {
        var dxmtPath = Path.Combine(installDirectory.FullName, DXMT_NAME);

        if (!Directory.Exists(dxmtPath))
        {
            Log.Information("DXMT does not exist, downloading");
            await DownloadDxmt(installDirectory).ConfigureAwait(false);
        }

        var system32 = Path.Combine(prefix.FullName, "drive_c", "windows", "system32");
        var files = Directory.GetFiles(dxmtPath);

        Log.Information("Extracting DXMT files");
        foreach (string fileName in files)
        {
            var destFile = Path.Combine(system32, Path.GetFileName(fileName));
            if (File.Exists(destFile + ".bck"))
            {
                Log.Information($"Restoring {destFile}");
                File.Move(destFile + ".bck", destFile, true);
            }
        }
    }

    

    private static async Task DownloadDxmt(DirectoryInfo installDirectory)
    {
        using var client = new HttpClient();
        var tempPath = PlatformHelpers.GetTempFileName();

        File.WriteAllBytes(tempPath, await client.GetByteArrayAsync(DXMT_DOWNLOAD));
        PlatformHelpers.Untar(tempPath, installDirectory.FullName);

        File.Delete(tempPath);
    }

}
