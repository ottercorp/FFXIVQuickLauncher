using KeySharp;
using Serilog;

namespace XIVLauncher.Core.Accounts.Secrets.Providers;

public class KeychainSecretProvider : ISecretProvider
{
    public const string PACKAGE = "wang.ffxiv.xivlauncher";
    // public const string SERVICE = "SDOID";
    public const string SERVICE_SESSION = "SDOID";

    public bool IsAvailable { get; private set; }

    public KeychainSecretProvider()
    {
        this.IsAvailable = SetDummyAndCheck();
    }

    public bool SetDummyAndCheck()
    {
        /*
         * We need to set a dummy entry here to ensure that libsecret unlocks the keyring.
         * This is a problem with libsecret: http://crbug.com/660005
         */
        try
        {
            const string DUMMY_SVC = "XIVLauncherCN Safe Storage Control";
            const string DUMMY_NAME = "XIVLauncherCN";
            const string DUMMY_PW = "Otter loves you";

            Keyring.SetPassword(PACKAGE, DUMMY_SVC, DUMMY_NAME, DUMMY_PW);

            var saved = Keyring.GetPassword(PACKAGE, DUMMY_SVC, DUMMY_NAME);
            return saved == DUMMY_PW;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not init the keychain");
        }

        return false;
    }

    public string? GetPassword(string accountName)
    {
        return "";
        // try
        // {
        //     return Keyring.GetPassword(PACKAGE, SERVICE, accountName);
        // }
        // catch (KeyringException ex)
        // {
        //     if (ex.Type == ErrorType.NotFound)
        //         return null;
        //
        //     throw;
        // }
    }

    public void SavePassword(string accountName, string password)
    {
        // Keyring.SetPassword(PACKAGE, SERVICE, accountName, password);
    }

    public void DeletePassword(string accountName)
    {
        // Keyring.DeletePassword(PACKAGE, SERVICE, accountName);
    }

    public string? GetAutoLoginSessionKey(string accountName)
    {
        try
        {
            return Keyring.GetPassword(PACKAGE, SERVICE_SESSION, accountName);
        }
        catch (KeyringException ex)
        {
            if (ex.Type == ErrorType.NotFound)
                return null;
            // Get password before storing will make the exception unable to track.
            // throw;
            return null;
        }
    }

    public void SaveAutoLoginSessionKey(string accountName, string sessionKey)
    {
        Keyring.SetPassword(PACKAGE, SERVICE_SESSION, accountName, sessionKey);
    }

    public void DeleteAutoLoginSessionKey(string accountName)
    {
        Keyring.DeletePassword(PACKAGE, SERVICE_SESSION, accountName);
    }
}
