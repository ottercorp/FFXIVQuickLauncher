using AriaNet.Attributes;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Serilog;
using SharedMemory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XIVLauncher.Common.Game;

namespace XIVLauncher.Common.Http
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HttpRpcAttribute : Attribute
    {
    }

    public class DcTravelListener
    {
        private readonly CancellationTokenSource _listenerCts = new();
        private WebServer webServer;
        private Dictionary<string, MethodInfo> rpcMethodCache = new();
        public DcTraveler DcTraveler;

        private readonly byte[] key;
        private readonly byte[] iv;
        private readonly bool useEncrypt;

        public DcTravelListener(DcTraveler dcTraveler, int port, bool useEncrypt = true)
        {
            if (useEncrypt)
            {
                var password = GenerateRandomBase64(32);
                var salt = GenerateRandomBase64(16);
                using var derive = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), 100_000, HashAlgorithmName.SHA256);
                key = derive.GetBytes(32);
                iv = derive.GetBytes(16);
            }

            this.useEncrypt = useEncrypt;
            this.DcTraveler = dcTraveler ?? throw new ArgumentNullException(nameof(dcTraveler));
            CacheRpcMethods();

            webServer = new WebServer(o => o
                    .WithUrlPrefix($"http://127.0.0.1:{port}")
                    .WithMode(HttpListenerMode.EmbedIO))
                    .WithWebApi("/dctravel", m => m.WithController(() => new RpcController(this)));
        }

        private static string GenerateRandomBase64(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        internal string Encrypt(string plainText)
        {
            if (!useEncrypt)
            {
                return plainText;
            }

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        internal string Decrypt(string cipherText)
        {
            if (!useEncrypt)
            {
                return cipherText;
            }

            var buffer = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);

            return sr.ReadToEnd();
        }

        public void Stop()
        {
            _listenerCts.Cancel();
            DcTraveler.KeepAliveCts.Cancel();
            DcTraveler?.Logout().Wait();

            if (webServer != null)
            {
                webServer.Dispose();
                webServer = null;
            }
        }

        private void CacheRpcMethods()
        {
            var methods = typeof(DcTraveler)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<HttpRpcAttribute>() != null);

            foreach (var method in methods)
            {
                rpcMethodCache[method.Name] = method;
            }
        }

        public async Task StartAsync()
        {
            try
            {
                await webServer.RunAsync(_listenerCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("dc listener canceled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "dc listener occurred an exception.");
            }
        }

        public class RpcRequest
        {
            public string Method { get; set; }
            public object[] Params { get; set; }
        }

        public class RpcResponse
        {
            public object Result { get; set; }
            public string Error { get; set; }
        }

        // EmbedIo控制器处理RPC请求
        private class RpcController : WebApiController
        {
            private readonly DcTravelListener _listener;

            public RpcController(DcTravelListener listener)
            {
                _listener = listener;
            }

            [Route(HttpVerbs.Post, "/")]
            public async Task ProcessRequest()
            {
                try
                {
                    // 检查CORS
                    var origin = Request.Headers["Origin"];
                    if (!string.IsNullOrEmpty(origin))
                    {
                        Response.StatusCode = 403;
                        await Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("CORS Forbidden"));
                        return;
                    }

                    // 读取并解密请求体
                    using var reader = new StreamReader(Request.InputStream, Request.ContentEncoding);
                    var body = await reader.ReadToEndAsync();
                    body = _listener.Decrypt(body);

                    var rpcRequest = JsonSerializer.Deserialize<RpcRequest>(body);

                    if (!_listener.rpcMethodCache.TryGetValue(rpcRequest.Method, out var method))
                        throw new Exception("Unknown or unauthorized method");

                    // 处理参数
                    var parameters = method.GetParameters();
                    if (parameters.Length != rpcRequest.Params.Length)
                        throw new Exception("Parameter count mismatch");

                    var callParams = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var paramType = parameters[i].ParameterType;
                        if (rpcRequest.Params[i] is JsonElement je)
                        {
                            callParams[i] = je.Deserialize(paramType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                        else
                        {
                            callParams[i] = Convert.ChangeType(rpcRequest.Params[i], paramType);
                        }
                    }

                    // 调用方法
                    object result;
                    if (method.ReturnType == typeof(Task))
                    {
                        var task = (Task)method.Invoke(_listener.DcTraveler, callParams);
                        await task;
                        result = null;
                    }
                    else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        dynamic task = method.Invoke(_listener.DcTraveler, callParams);
                        await task;
                        result = task.Result;
                    }
                    else
                    {
                        result = method.Invoke(_listener.DcTraveler, callParams);
                    }

                    // 准备响应
                    var response = new RpcResponse { Result = result, Error = null };
                    var responseJson = JsonSerializer.Serialize(response);
                    responseJson = _listener.Encrypt(responseJson);

                    Response.ContentType = "application/json";
                    await Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                }
                catch (Exception ex)
                {
                    var response = new RpcResponse { Result = null, Error = ex.ToString() };
                    var responseJson = JsonSerializer.Serialize(response);

                    Response.ContentType = "application/json";
                    Response.StatusCode = 200; // 保持200状态码，在响应体中传递错误信息
                    await Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
                }
            }
        }
    }
}
