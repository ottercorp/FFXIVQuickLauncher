using Serilog;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XIVLauncher.Accounts.Cred;

public enum CredType
{
    NoEncryption,
    WindowsCredManager,
    WindowsHello
}

public class CredData
{
    public string PackageName { get; set; }
    public string Account { get; set; }
    public string PasswordProtectedKey { get; set; }
    public string LoginSalt { get; set; }

    [JsonConstructor]
    public CredData(string packageName, string account, string passwordProtectedKey, string loginSalt)
    {
        PackageName = packageName;
        Account = account;
        PasswordProtectedKey = passwordProtectedKey;
        LoginSalt = loginSalt;
    }

    public CredData(string packageName, string filename)
    {
        if (File.Exists(filename))
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                };

                var data = JsonSerializer.Deserialize<CredData>(File.ReadAllText(filename), options);
                if (data == null
                    || string.IsNullOrEmpty(data.PackageName)
                    || string.IsNullOrEmpty(data.Account)
                    || string.IsNullOrEmpty(data.PasswordProtectedKey)
                    || string.IsNullOrEmpty(data.LoginSalt))
                {
                    throw new InvalidDataException("Credential metadata is incomplete.");
                }

                PackageName = data.PackageName;
                Account = data.Account;
                PasswordProtectedKey = data.PasswordProtectedKey;
                LoginSalt = data.LoginSalt;
                Log.Information($"[Cred] Loaded keys from {filename}");
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Cred] Loading keys from {CredentialPath} failed", filename);
                throw new InvalidDataException(
                    $"无法读取账号凭据元数据，原文件未被覆盖：{filename}",
                    ex);
            }
        }

        this.PackageName = packageName;
        this.PasswordProtectedKey = EncryptionHelper.GetRandomBase64String(128);
        this.Account = EncryptionHelper.GetRandomHexString(8);
        this.LoginSalt = EncryptionHelper.GenerateSalt();
        Log.Information($"[Cred] Make new keys");
        var text = JsonSerializer.Serialize<CredData>(this);
        File.WriteAllText(filename, text);
        Log.Information($"[Cred] Save keys from {filename}");
    }
}
