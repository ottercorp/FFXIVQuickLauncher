using AriaNet.Attributes;
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
        private volatile HttpListener listener;
        private Dictionary<string, MethodInfo> rpcMethodCache = new();
        public DcTraveler DcTraveler;

        private readonly byte[] kev;
        private readonly byte[] iv;
        private readonly bool useEncrypt;
        public DcTravelListener(DcTraveler dcTraveler, int port, bool useEncrypt = true)
        {
            if (useEncrypt)
            {
                var password = GenerateRandomBase64(32);
                var salt = GenerateRandomBase64(16);
                using var derive = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), 100_000, HashAlgorithmName.SHA256);
                kev = derive.GetBytes(32);
                iv = derive.GetBytes(16);
            }
            this.useEncrypt = useEncrypt;
            this.DcTraveler = dcTraveler ?? throw new ArgumentNullException(nameof(dcTraveler));
            CacheRpcMethods();
            this.listener = new HttpListener();
            this.listener.Prefixes.Add($"http://127.0.0.1:{port}/dctravel/");
            this.listener.Start();
        }
        private static string GenerateRandomBase64(int length)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private string Encrypt(string plainText)
        {
            if (!this.useEncrypt)
            {
                return plainText;
            }
            using var aes = Aes.Create();
            aes.Key = kev;
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

        private string Decrypt(string cipherText)
        {
            if (!this.useEncrypt)
            {
                return cipherText;
            }
            var buffer = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = kev;
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
            this.DcTraveler.KeepAliveCts.Cancel();
            this.DcTraveler?.Logout().Wait();
            if (listener != null)
            {
                listener.Stop();
                listener.Close();
                listener = null;
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
            while (!_listenerCts.Token.IsCancellationRequested)
            {
                try
                {
                    var getContextTask = listener.GetContextAsync();
                    var completedTask = await Task.WhenAny(getContextTask, Task.Delay(-1, _listenerCts.Token));

                    if (completedTask == getContextTask)
                    {
                        var context = await getContextTask;
                        _ = Task.Run(() => ProcessRequest(context));
                    }
                }
                catch (ObjectDisposedException)
                {
                    Log.Information("dc listener is disposed.");
                    break;
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 50 || ex.ErrorCode == 995)
                {
                    Log.Error(ex,"dc listener stop.");
                    break;
                }
                catch (OperationCanceledException)
                {
                    Log.Information("dc listener canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "dc listener occured an exception.");
                }
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
        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string origin = context.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.OutputStream.WriteAsync(
                        Encoding.UTF8.GetBytes("CORS Forbidden"), 0, "CORS Forbidden".Length);
                    context.Response.Close();
                    return;
                }

                if (context.Request.HttpMethod != "POST")
                {
                    context.Response.StatusCode = 405;
                    context.Response.Close();
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                body = this.Decrypt(body);
                var rpcRequest = JsonSerializer.Deserialize<RpcRequest>(body);

                if (!rpcMethodCache.TryGetValue(rpcRequest.Method, out MethodInfo method))
                    throw new Exception("Unknown or unauthorized method");

                var parameters = method.GetParameters();
                if (parameters.Length != rpcRequest.Params.Length)
                    throw new Exception("Parameter count mismatch");

                object[] callParams = new object[parameters.Length];
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

                object result;
                if (method.ReturnType == typeof(Task))
                {
                    var task = (Task)method.Invoke(this.DcTraveler, callParams);
                    await task;
                    result = null;
                }
                else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    dynamic task = method.Invoke(this.DcTraveler, callParams);
                    await task;
                    result = task.Result;
                }
                else
                {
                    result = method.Invoke(this.DcTraveler, callParams);
                }

                var response = new RpcResponse { Result = result, Error = null };
                var responseJson = JsonSerializer.Serialize(response);
                responseJson = this.Encrypt(responseJson);
                var buffer = Encoding.UTF8.GetBytes(responseJson);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                var response = new RpcResponse { Result = null, Error = ex.ToString() };
                var responseJson = JsonSerializer.Serialize(response);

                var buffer = Encoding.UTF8.GetBytes(responseJson);
                context.Response.ContentType = "application/json";
                //context.Response.StatusCode = 200;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
            }
        }
    }
}
