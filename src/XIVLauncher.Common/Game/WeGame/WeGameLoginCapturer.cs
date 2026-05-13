using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using StreamJsonRpc;

namespace XIVLauncher.Common.Game.WeGame
{
    public sealed class WeGameLoginCapturer
    {
        private const string PipeName = "ApkalluCaller";
        private const int FfxivWeGameGameId = 2000340;

        public async Task<(string userid, string token)> CaptureAsync(
            string sdologinDir,
            CancellationToken ct,
            IProgress<string> progress)
        {
            progress?.Report("正在部署 version.dll...");
            DeployVersionDll(sdologinDir);

            progress?.Report("正在等待 WeGame 登录...");

            var tcs = new TaskCompletionSource<(string, string)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (IOException ex)
            {
                throw new WeGameCapturePipeBusyException(ex);
            }

            await using (pipe.ConfigureAwait(false))
            await using (ct.Register(() =>
                         {
                             tcs.TrySetCanceled(ct);
                             try { pipe.Dispose(); } catch { /* swallow on disposal race */ }
                         }).ConfigureAwait(false))
            {
                TryLaunchWeGame(progress);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var formatter = new SystemTextJsonFormatter();
                var handler = new NewLineDelimitedMessageHandler(pipe, pipe, formatter);
                using var rpc = new JsonRpc(handler, new RpcHandler(tcs));
                rpc.StartListening();

                var result = await tcs.Task.ConfigureAwait(false);
                progress?.Report("已捕获登录信息, 正在登录盛趣...");

                // Wait for the client to receive the response and close its end of
                // the pipe before we dispose anything; otherwise rpc.Dispose() races
                // the in-flight response write and the Rust side reads 0 bytes
                // ("empty response (peer closed)"). 2s is generous — sdologin closes
                // its IpcStream immediately after reading the response.
                try
                {
                    await rpc.Completion.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
                catch
                {
                    // Timeout, cancellation, or rpc fault: response was almost
                    // certainly flushed by now; fall through to dispose.
                }

                return result;
            }
        }

        private static void TryLaunchWeGame(IProgress<string> progress)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"wegame://StartFor={FfxivWeGameGameId}",
                    UseShellExecute = true,
                });
                progress?.Report("已请求启动 WeGame, 请在 WeGame 中登录最终幻想 14...");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "wegame:// 唤起失败");
                progress?.Report("自动唤起 WeGame 失败, 请手动启动 WeGame 并登录最终幻想 14...");
            }
        }

        public static void DeployVersionDll(string sdologinDir)
        {
            var src = Path.Combine(Paths.ResourcesPath, "version.dll");

            if (!File.Exists(src))
                throw new FileNotFoundException("找不到 version.dll 资源", src);

            var dst = Path.Combine(sdologinDir, "version.dll");

            if (File.Exists(dst) && HashEquals(src, dst))
            {
                Log.Information("version.dll 已最新, 跳过部署");
                return;
            }

            try
            {
                File.Copy(src, dst, overwrite: true);
                Log.Information("version.dll 已部署: {Dst}", dst);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new VersionDllPermissionDeniedException(src, dst, ex);
            }
            catch (IOException ex) when (IsAccessDenied(ex))
            {
                throw new VersionDllPermissionDeniedException(src, dst, ex);
            }
        }

        public static bool HashEquals(string a, string b)
        {
            using var sha = SHA256.Create();
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            return sha.ComputeHash(fa).SequenceEqual(sha.ComputeHash(fb));
        }

        private static bool IsAccessDenied(IOException ex)
        {
            const int errAccessDenied = 5;
            const int errSharingViolation = 32;
            var code = ex.HResult & 0xFFFF;
            return code == errAccessDenied || code == errSharingViolation;
        }

        private sealed class RpcHandler
        {
            private readonly TaskCompletionSource<(string, string)> _tcs;
            public RpcHandler(TaskCompletionSource<(string, string)> tcs) => _tcs = tcs;

            [JsonRpcMethod("login.captured")]
            public string LoginCaptured(string userid, string token)
            {
                _tcs.TrySetResult((userid, token));
                return "ok";
            }
        }
    }

    public sealed class VersionDllPermissionDeniedException : Exception
    {
        public string Source { get; }
        public string Destination { get; }

        public VersionDllPermissionDeniedException(string src, string dst, Exception inner)
            : base($"无权限写入 {dst}", inner)
        {
            Source = src;
            Destination = dst;
        }
    }

    public sealed class WeGameCapturePipeBusyException : Exception
    {
        public WeGameCapturePipeBusyException(Exception inner)
            : base("命名管道 ApkalluCaller 已被另一个进程占用", inner)
        {
        }
    }
}
