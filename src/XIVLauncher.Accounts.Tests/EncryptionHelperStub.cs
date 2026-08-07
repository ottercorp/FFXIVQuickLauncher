using System;
using System.Security.Cryptography;

namespace XIVLauncher.Accounts.Cred
{
    internal static class EncryptionHelper
    {
        public static string GetRandomBase64String(int count)
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(count));
        }

        public static string GetRandomHexString(int count)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(count)).ToLowerInvariant();
        }

        public static string GenerateSalt()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }
    }
}
