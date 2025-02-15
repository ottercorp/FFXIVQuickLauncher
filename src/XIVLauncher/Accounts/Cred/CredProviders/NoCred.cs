using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XIVLauncher.Accounts.Cred.CredProviders;

internal class NoCred : ICredProvider
{
    public string GetName() => "无加密";
    public string GetDescription() => "";


    public NoCred(CredData cred)
    {
    }

    public async Task ClearCache()
    {
    }

    public async Task<string?> Decrypt(string? text)
    {
        return text;
    }

    public async Task<string?> Encrypt(string? text)
    {
        return text;
    }

    public async Task<bool> IsSupported()
    {
        return true;
    }

    public async Task Unregister()
    {

    }
}
