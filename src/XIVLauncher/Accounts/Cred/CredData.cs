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
        try
        {
            if (File.Exists(filename))
            {
                var options = new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                };

                var data = JsonSerializer.Deserialize<CredData>(File.ReadAllText(filename), options);
                PackageName = data.PackageName;
                Account = data.Account;
                PasswordProtectedKey = data.PasswordProtectedKey;
                LoginSalt = data.LoginSalt;
                Log.Information($"[Cred] Loaded keys from {filename}");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Cred] Loaded keys from {filename} failed\n{ex}");
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
