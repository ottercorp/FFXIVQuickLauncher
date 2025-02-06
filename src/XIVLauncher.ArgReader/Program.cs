namespace XIVLauncher.ArgReader;
using Serilog;
using Serilog.Events;
using System.Runtime.CompilerServices;
using XIVLauncher.Common;
using XIVLauncher.Common.Patching;
using XIVLauncher.Common.Patching.Rpc.Implementations;
using FfxivArgLauncher;
using System.Threading.Channels;
using XIVLauncher.Common.PatcherIpc;
using System.Diagnostics;

internal class Program
{
    private static SharedMemoryRpc rpc;
    private static ArgReader argReader;

    static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
                     .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Fatal)
                     .WriteTo.File(Path.Combine(Paths.RoamingPath, "argReader.log"))
                     .WriteTo.Debug()
                     .MinimumLevel.Verbose()
                     .CreateLogger();

        if (args.Length != 1) {
            Log.Error($"[ArgReader] Error args");
            Environment.Exit(-1);
        }

        InitRpc(args[0]);
    }

    private static void InitRpc(string channelName) {
        rpc = new SharedMemoryRpc(channelName);
        rpc.MessageReceived += RemoteCallHandler;

        Log.Information("[ArgReader] IPC connected");

        rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.Hello,
            Data = DateTime.Now
        });

        Log.Information("[ArgReader] sent hello");
    }

    private static void RemoteCallHandler(PatcherIpcEnvelope envelope)
    {
        switch (envelope.OpCode)
        {
            case PatcherIpcOpCode.Bye:

                Log.Information("[ArgReader] Bye");
                break;

            case PatcherIpcOpCode.OpenProcess:
                try
                {
                    Log.Information($"[ArgReader] Open process: {envelope.Data}");
                    var processId = int.Parse((string)envelope.Data);
                    var process = Process.GetProcessById(processId);
                    argReader = new ArgReader(process);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Open process failed");
                    rpc.SendMessage(new PatcherIpcEnvelope
                    {
                        OpCode = PatcherIpcOpCode.ArgReadFail,
                        Data = ex.ToString()
                    });
                }
                break;

            case PatcherIpcOpCode.ReadArgs:
                try
                {
                    Log.Information($"[ArgReader] Read Args");
                    var args = argReader.GetArgs();

                    rpc.SendMessage(new PatcherIpcEnvelope
                    {
                        OpCode = PatcherIpcOpCode.ArgReadOk,
                        Data = args
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Open process failed");
                    rpc.SendMessage(new PatcherIpcEnvelope
                    {
                        OpCode = PatcherIpcOpCode.ArgReadFail,
                        Data = ex.ToString()
                    });
                }
                break;
        }
    }
}
