using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XIVLauncher.Common.Game;

namespace XIVLauncher.Common.Http;

[AttributeUsage(AttributeTargets.Method)]
public class HttpRpcAttribute : Attribute
{
}

public class DcTravelListener : IAsyncDisposable
{
    private readonly WebApplication _app;
    public readonly DcTraveler DcTraveler;
    private readonly byte[]? _key;
    private readonly byte[]? _iv;
    private readonly bool _useEncrypt;

    private static readonly ConcurrentDictionary<string, MethodInfo> RpcMethodCache = new();

    public DcTravelListener(DcTraveler dcTraveler, int port, bool useEncrypt = true)
    {
        DcTraveler = dcTraveler ?? throw new ArgumentNullException(nameof(dcTraveler));
        _useEncrypt = useEncrypt;

        if (useEncrypt)
        {
            var password = GenerateRandomBase64(32);
            var salt = GenerateRandomBase64(16);
            using var derive = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), 100_000, HashAlgorithmName.SHA256);
            _key = derive.GetBytes(32);
            _iv = derive.GetBytes(16);
        }

        // 构建 Minimal API 应用
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        // 可选：禁用日志（或集成 Serilog）
        builder.Logging.ClearProviders();
        builder.Host.UseSerilog();
        var app = builder.Build();

        // 注册 POST /dctravel
        app.MapPost("/dctravel", async (HttpContext context) =>
        {
            // 拒绝 CORS
            if (!string.IsNullOrEmpty(context.Request.Headers["Origin"]))
                return Results.Text("CORS Forbidden", contentType: "text/plain", statusCode: 403);

            // 读取并解密请求体
            string body;
            try
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var encryptedBody = await reader.ReadToEndAsync();
                body = Decrypt(encryptedBody);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to decrypt request body");
                return Results.Json(new RpcResponse { Error = "Decryption failed" });
            }

            // 反序列化 RPC 请求
            RpcRequest? rpcRequest;
            try
            {
                rpcRequest = JsonSerializer.Deserialize<RpcRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Invalid JSON in RPC request");
                return Results.Json(new RpcResponse { Error = "Invalid JSON" });
            }

            if (rpcRequest?.Method == null)
                return Results.Json(new RpcResponse { Error = "Missing method" });

            // 查找方法
            var method = GetRpcMethod(rpcRequest.Method);
            if (method == null)
                return Results.Json(new RpcResponse { Error = "Unknown or unauthorized method" });

            // 准备参数
            var parameters = method.GetParameters();
            var paramValues = rpcRequest.Params ?? Array.Empty<object>();
            if (parameters.Length != paramValues.Length)
                return Results.Json(new RpcResponse { Error = "Parameter count mismatch" });

            var callParams = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var paramValue = paramValues[i];

                try
                {
                    if (paramValue is JsonElement je)
                        callParams[i] = je.Deserialize(paramType,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    else
                        callParams[i] = Convert.ChangeType(paramValue, paramType);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to convert parameter {Index} to {Type}", i, paramType);
                    return Results.Json(new RpcResponse { Error = $"Parameter {i} conversion failed" });
                }
            }

            // 调用方法
            try
            {
                object? result;
                var returnType = method.ReturnType;

                if (returnType == typeof(Task))
                {
                    var task = (Task)method.Invoke(DcTraveler, callParams)!;
                    await task;
                    result = null;
                }
                else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    dynamic task = method.Invoke(DcTraveler, callParams)!;
                    await task;
                    result = task.Result;
                }
                else
                {
                    result = method.Invoke(DcTraveler, callParams);
                }

                var response = new RpcResponse { Result = result };
                var jsonResponse = JsonSerializer.Serialize(response);
                var encryptedResponse = Encrypt(jsonResponse);
                return Results.Text(encryptedResponse, "application/json");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error invoking RPC method {Method}", rpcRequest.Method);
                return Results.Json(new RpcResponse { Error = ex.ToString() });
            }
        });

        _app = app;
    }

    private static string GenerateRandomBase64(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string Encrypt(string plainText)
    {
        if (!_useEncrypt) return plainText;
        if (_key == null || _iv == null) throw new InvalidOperationException("Key not initialized");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    private string Decrypt(string cipherText)
    {
        if (!_useEncrypt) return cipherText;
        if (_key == null || _iv == null) throw new InvalidOperationException("Key not initialized");

        var buffer = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        using var ms = new MemoryStream(buffer);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static MethodInfo? GetRpcMethod(string methodName)
    {
        return RpcMethodCache.GetOrAdd(methodName, name =>
        {
            var method = typeof(DcTraveler)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == name && m.GetCustomAttribute<HttpRpcAttribute>() != null);

            return method ?? throw new KeyNotFoundException();
        });
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        Log.Information("DcTravelListener started on http://127.0.0.1:{Port}/dctravel", _app.Urls.FirstOrDefault()?.Split(':').Last());
    }

    public async Task StopAsync()
    {
        DcTraveler.KeepAliveCts.Cancel();
        try
        {
            await (DcTraveler.Logout() ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during DcTraveler logout");
        }

        await _app.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_app is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            await _app.DisposeAsync();
    }

    // DTOs
    public class RpcRequest
    {
        public string? Method { get; set; }
        public object[]? Params { get; set; }
    }

    public class RpcResponse
    {
        public object? Result { get; set; }
        public string? Error { get; set; }
    }
}
