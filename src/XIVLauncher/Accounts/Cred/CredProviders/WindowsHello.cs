using Serilog;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace XIVLauncher.Accounts.Cred.CredProviders;
// https://github.com/timokoessler/2FAGuard/blob/6be544ed50b782493a30be9e9e2dcef719767e40/Guard.Core/Security/WindowsHello.cs

public class WindowsHello : ICredProvider
{
    private CredData Cred { get; init; }
    //private KeyCredentialRetrievalResult CachedCredResult;
    private EncryptionHelper EncryptionHelper;

    public WindowsHello(CredData cred)
    {
        this.Cred = cred;
    }

    public string GetName() => "WindowsHello";
    public string GetDescription() => "使用生物识别身份验证加密，可以使用面部、虹膜或指纹（或 PIN 码）。";

    public async Task<string?> Decrypt(string? text)
    {
        this.EncryptionHelper ??= await GetEncryptionHelper();
        if (text == null)
        {
            return null;
        }
        return this.EncryptionHelper.DecryptString(text);
    }

    public async Task<string?> Encrypt(string? text)
    {
        this.EncryptionHelper ??= await GetEncryptionHelper();
        if (text == null)
        {
            return null;
        }
        return this.EncryptionHelper.EncryptString(text);
    }

    public async Task<bool> IsSupported()
    {
        try
        {
            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch
        {
            return false;
        }
    }

    private string CredName => $"{Cred.PackageName}-{Cred.Account}";
    public async Task Unregister()
    {
        await KeyCredentialManager.DeleteAsync(CredName);
    }

    private async Task<KeyCredentialRetrievalResult> GetCredResult()
    {
        this.FocusSecurityPrompt();

        var credResult = await KeyCredentialManager.OpenAsync(CredName);
        if (credResult.Status == KeyCredentialStatus.NotFound)
        {
            credResult = await KeyCredentialManager.RequestCreateAsync(CredName, KeyCredentialCreationOption.FailIfExists);
        }
        //}

        if (credResult.Status != KeyCredentialStatus.Success)
        {
            throw new Exception(
                $"Failed to authenticate with Windows Hello: {credResult.Status}"
            );
        }
        return credResult;
    }

    private async Task<string> SignChallenge(string challenge, KeyCredentialRetrievalResult credResult)
    {
        ArgumentNullException.ThrowIfNull(credResult.Credential);
        var buffer = CryptographicBuffer.ConvertStringToBinary(
            challenge,
            BinaryStringEncoding.Utf8
        );
        var result = await credResult.Credential.RequestSignAsync(buffer);
        if (result.Status != KeyCredentialStatus.Success)
        {
            throw new Exception($"Failed to sign Windows Hello challenge: {result.Status}");
        }
        var signedResult = CryptographicBuffer.EncodeToBase64String(result.Result);
        if (signedResult == null || signedResult.Length == 0)
        {
            throw new Exception(
                "Failed to register with Windows Hello because the signed challenge is empty"
            );
        }
        return signedResult;
    }

    public async Task<EncryptionHelper> GetEncryptionHelper()
    {
        var credResult = await GetCredResult();
        var signedChallenge = await SignChallenge(Cred.PasswordProtectedKey, credResult);
        return new EncryptionHelper(Encoding.UTF8.GetBytes(signedChallenge), Convert.FromBase64String(Cred.LoginSalt));
    }

    private async void FocusSecurityPrompt()
    {
        const string className = "Credential Dialog Xaml Host";
        const int maxTries = 3;

        try
        {
            for (int currentTry = 0; currentTry < maxTries; currentTry++)
            {
                IntPtr hwnd = FindWindow(className, IntPtr.Zero);
                if (hwnd != IntPtr.Zero)
                {
                    SetForegroundWindow(hwnd);
                    return; // Exit the loop if successfully found and focused the window
                }
                await Task.Delay(500); // Retry after a delay if the window is not found
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Warning("Failed to focus Windows Hello prompt {msg}", ex.Message);
        }
    }

    public async Task ClearCache()
    {
        this.EncryptionHelper = null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, IntPtr ZeroOnly);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
