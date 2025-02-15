using System.Threading.Tasks;

namespace XIVLauncher.Accounts.Cred;

public interface ICredProvider
{
    public string GetName();
    public string GetDescription();

    public Task<bool> IsSupported();
#nullable enable
    public Task<string> Encrypt(string? text);
    public Task<string> Decrypt(string? text);
#nullable disable
    public Task ClearCache();

    public Task Unregister();
}
