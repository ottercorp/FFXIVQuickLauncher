using System.Text.Json;

namespace XIVLauncher.Core.Accounts.Secrets.Providers;

public class FileSecretProvider : ISecretProvider
{
    private readonly FileInfo configFile;
    private readonly FileInfo configFile2;
    private readonly Dictionary<string, string> savedPasswords;
    private readonly Dictionary<string, string> savedSessionKeys;

    public FileSecretProvider(FileInfo configFile)
    {
        this.configFile = configFile;
        this.configFile2 = new FileInfo(configFile.FullName + ".1");

        if (configFile.Exists)
        {
            this.savedPasswords = JsonSerializer.Deserialize<Dictionary<string, string>>(configFile.OpenText().ReadToEnd())!;
        }

        if (configFile2.Exists)
        {
            this.savedSessionKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(configFile2.OpenText().ReadToEnd())!;
        }

        this.savedPasswords ??= new Dictionary<string, string>();
        this.savedSessionKeys ??= new Dictionary<string, string>();
    }

    public string? GetPassword(string accountName)
    {
        if (this.savedPasswords.TryGetValue(accountName, out var password))
            return password;

        return null;
    }

    public void SavePassword(string accountName, string password)
    {
        if (this.savedPasswords.ContainsKey(accountName))
        {
            this.savedPasswords[accountName] = password;
        }
        else
        {
            this.savedPasswords.Add(accountName, password);
        }

        SavePasswordFile();
    }

    public void DeletePassword(string accountName)
    {
        this.savedPasswords.Remove(accountName);

        SavePasswordFile();
    }

    public string? GetAutoLoginSessionKey(string accountName)
    {
        if (this.savedSessionKeys.TryGetValue(accountName, out var sessionKey))
            return sessionKey;

        return null;
    }

    public void SaveAutoLoginSessionKey(string accountName, string sessionKey)
    {
        if (this.savedSessionKeys.ContainsKey(accountName))
        {
            this.savedSessionKeys[accountName] = sessionKey;
        }
        else
        {
            this.savedSessionKeys.Add(accountName, sessionKey);
        }

        SaveSessionKeyFile();
    }

    public void DeleteAutoLoginSessionKey(string accountName)
    {
        this.savedSessionKeys.Remove(accountName);

        SaveSessionKeyFile();
    }

    private void SavePasswordFile()
    {
        File.WriteAllText(this.configFile.FullName, JsonSerializer.Serialize(this.savedPasswords));
    }

    private void SaveSessionKeyFile()
    {
        File.WriteAllText(this.configFile2.FullName, JsonSerializer.Serialize(this.savedSessionKeys));
    }
}
