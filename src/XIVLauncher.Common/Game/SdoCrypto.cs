using System;
using System.Security.Cryptography;
using System.Text;

namespace XIVLauncher.Common.Game
{
    /// <summary>
    /// staticLogin encryptFlag=1 field encryption (new 1.1.344.x scheme), reverse-engineered from
    /// SdoBaseClient.dll / sdologin.exe.
    ///
    /// getGuid?generateDynamicKey=1 hands back a 20-digit dynamicKey. From it we derive an 8-byte DES
    /// key, then DES-CBC(IV="23456789", PKCS7) encrypt inputUserId / password and base64 them. The
    /// server only peels this single DES layer, so the password plaintext inside is the raw password.
    /// Verified byte-for-byte against captures and confirmed working live.
    ///
    /// Note: single-DES here is equivalent to Python's TripleDES with an 8-byte key (K1==K2==K3);
    /// using System.Security.Cryptography.DES avoids the TripleDES "K1==K2==K3" weak-key rejection.
    /// </summary>
    public static class SdoCrypto
    {
        private static readonly byte[] Iv = Encoding.ASCII.GetBytes("23456789");

        // sub_456310 per-byte obfuscation constants (XOR, then swap high/low nibble).
        private static readonly byte[] DeriveXor = { 0xDD, 0xD6, 0xD8, 0xEA, 0xFD, 0xF6, 0xF8, 0xCA };

        /// <summary>
        /// 20-digit dynamicKey -> 8-byte DES key. Reverse of sub_456310 (verified against capture truth):
        /// split into 4 groups of 5 decimal digits, each -> little-endian 16-bit -> 8 bytes, then per byte
        /// XOR {DD,D6,D8,EA,FD,F6,F8,CA} and swap high/low nibbles. e.g. "50654352274243022907" -> "01464589".
        /// Non 20-digit input falls back to the legacy truncate/pad-to-24 strategy.
        /// </summary>
        public static byte[] DeriveDesKey(string dynamicKey)
        {
            if (dynamicKey != null && dynamicKey.Length == 20 && IsAllDigits(dynamicKey))
            {
                var raw = new byte[8];
                for (var i = 0; i < 4; i++)
                {
                    var w = int.Parse(dynamicKey.Substring(i * 5, 5), System.Globalization.CultureInfo.InvariantCulture);
                    raw[i * 2] = (byte)(w & 0xFF);
                    raw[i * 2 + 1] = (byte)((w >> 8) & 0xFF);
                }

                var key = new byte[8];
                for (var i = 0; i < 8; i++)
                {
                    var v = raw[i] ^ DeriveXor[i];
                    key[i] = (byte)(((v << 4) | (v >> 4)) & 0xFF);
                }

                return key;
            }

            // Fallback (isUrl2 / unverified branch): utf8, truncate to 24, pad to 24 with (8 - len%8).
            var kb = Encoding.UTF8.GetBytes(dynamicKey ?? string.Empty);
            if (kb.Length > 24)
                Array.Resize(ref kb, 24);
            if (kb.Length > 0 && kb.Length < 24)
            {
                var pad = (byte)((8 - kb.Length % 8) & 0xFF);
                var padded = new byte[24];
                Array.Copy(kb, padded, kb.Length);
                for (var i = kb.Length; i < 24; i++)
                    padded[i] = pad;
                kb = padded;
            }

            return kb;
        }

        /// <summary>
        /// base64( DES-CBC(deriveKey(dynamicKey), utf8(plain)) ) with IV="23456789" and PKCS7 padding.
        /// This is how staticLogin encryptFlag=1 encodes both inputUserId (account) and password.
        /// May throw CryptographicException for the ~16 DES weak/semi-weak keys; callers should retry
        /// with a fresh dynamicKey in that (astronomically rare) case.
        /// </summary>
        public static string EncryptField(string dynamicKey, string plain)
        {
            using var des = DES.Create();
            des.Mode = CipherMode.CBC;
            des.Padding = PaddingMode.PKCS7;
            des.Key = DeriveDesKey(dynamicKey);
            des.IV = Iv;

            using var encryptor = des.CreateEncryptor();
            var data = Encoding.UTF8.GetBytes(plain ?? string.Empty);
            var cipher = encryptor.TransformFinalBlock(data, 0, data.Length);
            return Convert.ToBase64String(cipher);
        }

        private static bool IsAllDigits(string s)
        {
            foreach (var c in s)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }
    }
}
