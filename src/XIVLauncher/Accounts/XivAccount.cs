using AdysTech.CredentialManager;
using Newtonsoft.Json;
using Serilog;
using System;
using System.ComponentModel;
using System.Net;
using System.Threading.Tasks;
using Castle.Core.Internal;
using Newtonsoft.Json.Linq;
using SQLite;
using System.Security.Cryptography;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
namespace XIVLauncher.Accounts
{
    public enum XivAccountType
    {
        Sdo,
        WeGame
        // 注意: 旧版本存在 WeGameSid = 2, 已合并入 WeGame。AccountDatabaseMigrator 会迁移旧记录。
    }

    [Table(AccountDatabaseMigrator.AccountTableName)]
    public class XivAccount : IEquatable<XivAccount>
    {
        //public string Id => $"{UserName}-{UseOtp}-{UseSteamServiceAccount}";

        //public override string ToString() => Id;

        /*
         * 目前有如下几种登录方式:
         * 盛趣账密     LoginAccount Password
         * 叨鱼扫码     LoginAccount AutoLoginSessionKey
         * 叨鱼滑动     LoginAccount AutoLoginSessionKey
         * WeGame抓包   LoginAccount Password(token) / TestSID(使用SID模式)
         *
         * XivAccountType:Sdo    LoginAccount (AutoLoginSessionKey Password)
         * XivAccountType:WeGame LoginAccount + Password(token) 或 TestSID(SID); TestSID 有值即"使用SID"登录 (IsSidLogin)
         */

        [Unique]
        [AutoIncrement]
        [PrimaryKey]
        public int index { get; set; }

        public string Id { get; set; }

        public static XivAccount CreateAccount(XivAccountType accountType, string sndaId, string account = null, string areaName = null, string sessionId = null)
        {
            var newAccount = new XivAccount();
            Debug.Assert(sndaId != null);
            newAccount.AccountType = accountType;
            newAccount.SndaId = sndaId;
            newAccount.LoginAccount = account;
            newAccount.AreaName = areaName;
            newAccount.TestSID = sessionId;
            newAccount.GenerateId();
            return newAccount;
        }

        public void GenerateId()
        {
            this.Id = $"{this.UserName}|{this.AccountType}";
        }

        public string SndaId { get; set; }
        // for Account Manager
        [Ignore]
        public string DisplayName
        {
            get
            {
                if (UserDefinedName is not null)
                    return UserDefinedName;
                return this.UserName;
            }
            private set { }
        }

        // for Input Box
        [Ignore]
        public string UserName
        {
            get
            {
                // 迁移的旧 WeGameSid 记录可能没有 LoginAccount，用 SndaId 兜底。
                return string.IsNullOrEmpty(LoginAccount) ? SndaId : LoginAccount;
            }
            private set { }
        }
        public string UserDefinedName { get; set; }
        public XivAccountType AccountType { get; set; }
        public string LoginAccount { get; set; }

        public string AreaName { get; set; }

        public bool AutoLogin { get; set; }

        // Should be encrypted
        public string AutoLoginSessionKey { get; set; }
        // Should be encrypted. ULSKLK-... 免密登录令牌，走 /authen/v2/fastInLogin 续期（与 AutoLoginSessionKey 并存）。
        public string KeepLoginKey { get; set; }
        public string Password { get; set; }
        public string TestSID { get; set; }
        public string NSessionId { get; set; }

        [Ignore]
        public bool IsWeGame => (this.AccountType != XivAccountType.Sdo);

        // TestSID 有值即"使用SID"登录: 直接复用已换出的 SID (走 LoginBySid); 否则用 Password 里的 token 换票。
        [Ignore]
        public bool IsSidLogin => this.AccountType == XivAccountType.WeGame && !string.IsNullOrEmpty(this.TestSID);
        [Ignore]
        public string ThumbnailUrl { get; set; }
        [Ignore]
        public string ChosenCharacterName { get; set; }
        [Ignore]
        public string ChosenCharacterWorld { get; set; }

        public override int GetHashCode()
        {
            return (UserName, AccountType).GetHashCode();
        }

        public bool Equals(XivAccount other)
        {
            return this.GetHashCode() == other.GetHashCode();
        }

        public string FindCharacterThumb()
        {
            return null;
        }

        private const string URL = "https://xivapi.com/";

        public static async Task<JObject> GetCharacterSearch(string name, string world)
        {
            return await Get("character/search" + $"?name={name}&server={world}");
        }

        public static async Task<dynamic> Get(string endpoint)
        {
            using var client = new WebClient();

            var result = await client.DownloadStringTaskAsync(URL + endpoint);

            var parsedObject = JObject.Parse(result);

            return parsedObject;
        }
    }
}
