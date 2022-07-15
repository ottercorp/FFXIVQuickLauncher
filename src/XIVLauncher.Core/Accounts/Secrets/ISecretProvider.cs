namespace XIVLauncher.Core.Accounts.Secrets;

public interface ISecretProvider
{
    public string? GetPassword(string accountName);
    public void SavePassword(string accountName, string password);
    public string? GetAutoLoginSessionKey(string accountName);
    public void SaveAutoLoginSessionKey(string accountName, string sessionKey);

    public void DeletePassword(string accountName);
    public void DeleteAutoLoginSessionKey(string accountName);
}
