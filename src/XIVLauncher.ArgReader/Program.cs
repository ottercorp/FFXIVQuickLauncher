namespace XIVLauncher.ArgReader;
using FfxivArgLauncher;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using XIVLauncher.Common;
using XIVLauncher.Common.PatcherIpc;
using XIVLauncher.Common.Patching.Rpc.Implementations;

internal class Program
{
    private static SharedMemoryRpc rpc;
    private static ArgReader argReader;

    private static Thread thread;
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
        thread = new Thread(Loop);
        thread.Start();
        Log.Information("Exit");
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

    private static CancellationTokenSource readerCancelToken = new();
    private static void Loop()
    {
        try
        {
            while (!readerCancelToken.IsCancellationRequested)
            {
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ArgReader] loop encountered an error");
        }
    }

    private static void RemoteCallHandler(PatcherIpcEnvelope envelope)
    {
        switch (envelope.OpCode)
        {
            //case PatcherIpcOpCode.Bye:
            //    if ((bool)envelope.Data is true) { 
            //        argReader.KillProcess();
            //    }
            //    Log.Information("[ArgReader] Bye");
            //    readerCancelToken.Cancel();
            //    break;

            case PatcherIpcOpCode.OpenProcess:
                try
                {
                    Log.Information($"[ArgReader] Open process: {envelope.Data}");
                    var processId = (long)envelope.Data;
                    var process = Process.GetProcessById((int)processId);

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
                    var data = argReader.GetLoginData();
                    rpc.SendMessage(new PatcherIpcEnvelope
                    {
                        OpCode = PatcherIpcOpCode.ArgReadOk,
                        Data = data
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
