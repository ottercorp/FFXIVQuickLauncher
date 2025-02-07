using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Documents;
using FfxivArgLauncher;
using Serilog;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.PatcherIpc;
using XIVLauncher.Common.Patching;
using XIVLauncher.Common.Patching.Rpc;
using XIVLauncher.Common.Patching.Rpc.Implementations;

namespace XIVLauncher.Game;

public class RemoteArgReader : IDisposable
{
    private IRpc rpc;

    public enum InstallerState
    {
        NotStarted,
        NotReady,
        Ready,
        Busy,
        Failed
    }

    public InstallerState State { get; private set; } = InstallerState.NotStarted;

    public HashSet<LoginData> Data = new();

    public RemoteArgReader()
    {
    }

    public void Start()
    {
        var rpcName = "XLArgReader" + Guid.NewGuid().ToString();

        Log.Information("[ArgReaderIPC] Starting patcher with '{0}'", rpcName);

        this.rpc = new SharedMemoryRpc(rpcName);
        this.rpc.MessageReceived += RemoteCallHandler;

        var path = Path.Combine(AppContext.BaseDirectory,
            "XIVLauncher.ArgReader.exe");

        var startInfo = new ProcessStartInfo(path);
        startInfo.UseShellExecute = true;

        //Start as admin
        startInfo.Verb = "runas";

        //if (!Debugger.IsAttached)
        //{
        //    startInfo.CreateNoWindow = true;
        //    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        //}

        startInfo.Arguments = $"{rpcName}";

        State = InstallerState.NotReady;

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not launch Args Reader");
            throw new Exception("Start failed.", ex);
        }
    }

    private void RemoteCallHandler(PatcherIpcEnvelope envelope)
    {
        switch (envelope.OpCode)
        {
            case PatcherIpcOpCode.Hello:
                //_client.Initialize(_clientPort);
                Log.Information("[ArgReaderIPC] GOT HELLO");
                State = InstallerState.Ready;
                break;

            case PatcherIpcOpCode.ArgReadOk:
                State = InstallerState.Busy;
                Log.Information($"[ArgReaderIPC] GOT ARGS:{envelope.Data}");
                var data = (LoginData)envelope.Data;
                if (data.IsWegame())
                    this.Data.Add(data);
                break;

            case PatcherIpcOpCode.ArgReadFail:
                Log.Information("[ArgReaderIPC] GOT FAILED");
                State = InstallerState.Failed;
                Stop();
                throw new Exception((string)envelope.Data);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void WaitOnHello()
    {
        for (var i = 0; i < 40; i++)
        {
            if (State == InstallerState.Ready)
                return;

            Thread.Sleep(500);
        }

        throw new Exception("Installer RPC timed out.");
    }

    public void Stop()
    {
        //if (State == InstallerState.NotReady || State == InstallerState.NotStarted || State == InstallerState.Busy)
        //    return;

        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.Bye,
            Data = false,
        });
    }

    public void OpenProcess(int pid)
    {
        State = InstallerState.Busy;
        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.OpenProcess,
            Data = pid
        });
    }

    public void ReadArgs()
    {
        State = InstallerState.Busy;
        this.rpc.SendMessage(new PatcherIpcEnvelope
        {
            OpCode = PatcherIpcOpCode.ReadArgs,
        });
    }

    public void Dispose()
    {
        Stop();
    }
}
