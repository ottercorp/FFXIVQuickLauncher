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
        WeGame,
        WeGameSid
    }

    public class XivAccount : IEquatable<XivAccount>
    {
        //public string Id => $"{UserName}-{UseOtp}-{UseSteamServiceAccount}";

        //public override string ToString() => Id;

        /*
         * 目前有如下几种登录方式:
         * 盛趣账密     LoginAccount Password
         * 叨鱼扫码     LoginAccount AutoLoginSessionKey
         * 叨鱼滑动     LoginAccount AutoLoginSessionKey
         * WG手动抓包   ThirdLoginAccount  Token
         * WG抓SID     SndaId&AreaId SessionID
         * 
         * SndaId XivAccountType:WeGame ThirdLoginAccount (AutoLoginSessionKey)
         * SndaId XivAccountType:WeGameSid AreaName (SessionID)
         * SndaId XivAccountType:Sdo LoginAccount (AutoLoginSessionKey Password)
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
            switch (accountType)
            {
                case XivAccountType.WeGameSid:
                    Debug.Assert(areaName != null);
                    Debug.Assert(account == null);
                    newAccount.SndaId = sndaId;
                    newAccount.AreaName = areaName;
                    newAccount.TestSID = sessionId;
                    break;
                case XivAccountType.Sdo:
                case XivAccountType.WeGame:
                    Debug.Assert(account != null);
                    newAccount.LoginAccount = account;
                    break;
            }
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
                if (AccountType == XivAccountType.Sdo || AccountType == XivAccountType.WeGame)
                    return LoginAccount;
                else
                    return SndaId.ToString();
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
        public string Password { get; set; }
        public string TestSID { get; set; }

        [Ignore]
        public bool IsWeGame => (this.AccountType != XivAccountType.Sdo);
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
