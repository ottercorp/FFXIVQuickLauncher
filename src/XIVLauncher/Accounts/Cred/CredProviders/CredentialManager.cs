using System.Threading.Tasks;
using AdysTech.CredentialManager;
using System.Text;
using System.Security.Authentication.ExtendedProtection;
using System.ComponentModel;
using System.Net;
using System;

namespace XIVLauncher.Accounts.Cred.CredProviders;
public class CredentialManager : ICredProvider
{
    public string GetName() => "凭据管理器";
    public string GetDescription() => "使用系统自带的凭据管理器";

    private CredData Cred { get; init; }
    private EncryptionHelper EncryptionHelper;

    private const string ServiceName = "KeyVault";
    public CredentialManager(CredData cred)
    {
        this.Cred = cred;
    }

    public async Task ClearCache()
    {
        this.EncryptionHelper = null;
    }

    public async Task<string?> Decrypt(string? text)
    {
        this.EncryptionHelper ??= GetEncryptionHelper();
        if (text == null)
        {
            return null;
        }
        return this.EncryptionHelper.DecryptString(text);
    }

    public async Task<string?> Encrypt(string? text)
    {
        this.EncryptionHelper ??= GetEncryptionHelper();
        if (text == null)
        {
            return null;
        }
        return this.EncryptionHelper.EncryptString(text);
    }

    public EncryptionHelper GetEncryptionHelper()
    {
        return new EncryptionHelper(Encoding.UTF8.GetBytes(GetPassword()), Convert.FromBase64String(Cred.LoginSalt));
    }

    public string GetPassword()
    {
        var credentials = AdysTech.CredentialManager.CredentialManager.GetCredentials($"{Cred.PackageName}-{Cred.Account}");
        if (credentials != null)
        {
            return credentials.Password;
        }
        try
        {
            AdysTech.CredentialManager.CredentialManager.RemoveCredentials($"{Cred.PackageName}-{Cred.Account}");
        }
        catch (Exception)
        {
            // ignored
        }
        var password = EncryptionHelper.GetRandomHexString(128);
        AdysTech.CredentialManager.CredentialManager.SaveCredentials($"{Cred.PackageName}-{Cred.Account}", new NetworkCredential
        {
            UserName = Cred.Account,
            Password = password
        });
        return password;
    }

    public async Task<bool> IsSupported()
    {
        return true;
    }

    public async Task Unregister()
    {
        AdysTech.CredentialManager.CredentialManager.RemoveCredentials($"{Cred.PackageName}-{Cred.Account}");
    }
}
