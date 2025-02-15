using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FfxivArgLauncher;
using Serilog;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.PatcherIpc;
using XIVLauncher.Common.Patching.Rpc;
using XIVLauncher.Common.Patching.Rpc.Implementations;

namespace XIVLauncher.Game;

public class RemoteArgReader : IDisposable
{
    private IRpc rpc;

    public enum ReaderState
    {
        NotStarted,
        NotReady,
        Ready,
        Busy,
        Failed,
        Finish
    }

    public ReaderState State { get; private set; } = ReaderState.NotStarted;

    private LoginData Data;

    public RemoteArgReader()
    {
    }

    private Process process;
    public async Task Start()
    {
        var rpcName = "XLArgReader" + Guid.NewGuid().ToString();

        Log.Information("[ArgReaderIPC] Starting patcher with '{0}'", rpcName);

        this.rpc = new SharedMemoryRpc(rpcName);
        this.rpc.MessageReceived += RemoteCallHandler;

        var path = Path.Combine(AppContext.BaseDirectory, "XIVLauncher.ArgReader.exe");

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"{rpcName}"
        };

        if (!Debugger.IsAttached)
        {
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        State = ReaderState.NotReady;

        try
        {
            this.process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not launch Args Reader");
            throw new Exception("Start failed.", ex);
        }

        await WaitOn(ReaderState.Finish);
        Log.Information("[ArgReaderIPC] Start");
    }

    private void RemoteCallHandler(PatcherIpcEnvelope envelope)
    {
        Log.Information("[ArgReaderIPC] Received message with OpCode: {0}", envelope.OpCode);

        switch (envelope.OpCode)
        {
            case PatcherIpcOpCode.Hello:
                Log.Information("[ArgReaderIPC] GOT HELLO");
                State = ReaderState.Finish;
                break;

            case PatcherIpcOpCode.ArgReadOk:
                Log.Information($"[ArgReaderIPC] GOT ARGS: {envelope.Data}");
                this.Data = (LoginData)envelope.Data;
                State = ReaderState.Finish;
                break;

            case PatcherIpcOpCode.ArgReadFail:
                Log.Information("[ArgReaderIPC] GOT FAILED");
                State = ReaderState.Failed;
                Stop(false);
                throw new Exception((string)envelope.Data);

            default:
                Log.Error("[ArgReaderIPC] Received unknown OpCode: {0}", envelope.OpCode);
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task WaitOn(ReaderState state, int wait = 40)
    {
        Log.Information("[ArgReaderIPC] Waiting for state: {0}", state);
        await Task.Run(() =>
        {
            for (var i = 0; i < wait; i++)
            {
                if (State == state)
                {
                    Log.Information("[ArgReaderIPC] Desired state reached: {0}", state);
                    return;
                }

                Thread.Sleep(500);
            }
            Log.Error("[ArgReaderIPC] Reader RPC timed out.");
            throw new Exception("[ArgReaderIPC] Reader RPC timed out.");
        });
    }

    public void Stop(bool killProcess)
    {
        Log.Information("[ArgReaderIPC] Stopping RPC with killProcess: {0}", killProcess);
        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.Bye,
            Data = killProcess
        });
        Task.Run(() =>
        {
            Thread.Sleep(1000);
            try
            {
                process?.Kill();
            }
            catch { }
        });
    }

    public async Task OpenProcess(int pid)
    {
        Log.Information("[ArgReaderIPC] Opening process with PID: {0}", pid);
        State = ReaderState.Busy;
        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.OpenProcess,
            Data = pid
        });

        await WaitOn(ReaderState.Finish);
        Log.Information($"[ArgReaderIPC] OpenProcess: {pid}");
    }

    public async Task<LoginData> ReadArgs()
    {
        Log.Information("[ArgReaderIPC] Reading arguments");
        State = ReaderState.Busy;
        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.ReadArgs
        });

        await WaitOn(ReaderState.Finish);
        Log.Information($"[ArgReaderIPC] ReadArgs: {Data}");
        return Data;
    }

    public void Dispose()
    {
        Log.Information("[ArgReaderIPC] Disposing");
        this.rpc.MessageReceived -= RemoteCallHandler;
    }
}
