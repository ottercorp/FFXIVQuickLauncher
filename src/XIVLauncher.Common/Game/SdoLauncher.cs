using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;

#if NET6_0_OR_GREATER && !WIN32
using System.Net.Security;
#endif

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.Encryption;
using XIVLauncher.Common.Game.Exceptions;
using XIVLauncher.Common.PlatformAbstractions;
using System.Threading;
using XIVLauncher.Common.Util;
using System.Security.Principal;

namespace XIVLauncher.Common.Game
{
    public class GuiLoginType
    {
        public LoginType LoginType { get; set; }
        public string DisplayName { get; set; }

        public static List<GuiLoginType> Get(bool showWeGameToken = false)
        {
            var types = new List<GuiLoginType>
            {
                new GuiLoginType { LoginType = LoginType.SdoSlide, DisplayName = "一键登录" },
                new GuiLoginType { LoginType = LoginType.SdoQrCode, DisplayName = "扫码登录" },
                new GuiLoginType { LoginType = LoginType.SdoStatic, DisplayName = "密码登录" },
                new GuiLoginType { LoginType = LoginType.WeGameSid, DisplayName = "WeGame SID"}
            };
            if (showWeGameToken)
            {
                types.Add(new GuiLoginType { LoginType = LoginType.WeGameToken, DisplayName = "WeGame抓包" });
            }
            return types;
        }
    }
    public enum LoginType
    {
        SdoStatic,
        SdoSlide,
        SdoQrCode,
        WeGameToken,
        WeGameSid,
        AutoLoginSession
    }

    public partial class Launcher
    {
        //private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");
        //private string AreaId = "1";
        //private const int autoLoginKeepTime = 30;

        //private static string userName = String.Empty;
        //private static string password = String.Empty;
        //private static LogEventHandler logEvent = null;
        //private static bool forceQR = false;
        //private static bool autoLogin = false;
        //private static string autoLoginSessionKey = null;

        private const int QRCodeExpirationTime = 300 * 1000;// ms
        private const int SlideExpirationTime = 30 * 1000;// ms
        private const int AutoLoginKeepDays = 30;

        public async Task<LoginResult> LoginBySid(string sndaId, string sid)
        {
            var oath = new OauthLoginResult
            {
                SndaId = sndaId,
                SessionId = sid,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.WeGameSid,
            };

            return new LoginResult
            {
                OauthLogin = oath,
                State = LoginState.Ok,
            };
        }

        public async Task<LoginResult> LoginBySdoStatic(string account, string password)
        {
            var guid = await this.GetGuid();
            var macAddress = SdoUtils.GetMacAddress();
            //密码登录
            var result = await this.GetJsonAsSdoClient("staticLogin.json", new List<string>()
            {
                "checkCodeFlag=1", "encryptFlag=0", $"inputUserId={account}", $"password={password}", $"mac={macAddress}", $"guid={guid}",
                "inputUserType=0&accountDomain=1&autoLoginFlag=0&autoLoginKeepTime=0&supportPic=2"
            });

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            if (string.IsNullOrEmpty(result.Data.Tgt))
            {
                throw new SdoLoginException((int)SdoLoginCustomExpectionCode.STATIC_NEED_CAPTCHA, $"本次登录需要输入验证码,目前暂不支持,请使用其他登录方式登录。");
            }

            Log.Information($"staticLogin.json:{result.Data.SndaId}:{result.Data.Tgt}");

            var sndaId = result.Data.SndaId;
            var tgt = result.Data.Tgt;
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                //Password = password,
                SndaId = sndaId,
                AutoLoginSessionKey = null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoStatic,
            };

            return new LoginResult
            {
                OauthLogin = oath,
                State = LoginState.Ok,
            };
        }

        public async Task<LoginResult> LoginByWeGameToken(string account, string token, bool autoLogin)
        {
            var guid = await this.GetGuid();
            var (sndaId, tgt, autoLoginSessionKey) = await ThirdPartyLogin(account, token, autoLogin, AutoLoginKeepDays);
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                //Password = password,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.WeGameToken,
            };
            return new LoginResult
            {
                OauthLogin = oath,
                State = LoginState.Ok,
            };
        }

        public async Task<LoginResult> LoginByScanQrCode(bool autoLogin, CancellationTokenSource cts, Action<byte[]> showQrCode)
        {
            var guid = await this.GetGuid();
            // Wait for Scan QrCode
            string? sndaId = null;
            string? tgt = null;
            string? account = null;

            while (!cts.IsCancellationRequested)
            {
                var (codeKey, qrCode, expiration) = await this.GetQRCode();
                showQrCode?.Invoke(qrCode);
                (sndaId, tgt, account) = await this.WaitingForScanQRCode(codeKey, guid, cts, expiration, AutoLoginKeepDays);
                if (sndaId != null)
                    break;
            }

            var newAccount = await this.GetAccountGroup(tgt, sndaId);
            account = string.IsNullOrEmpty(account) ? newAccount : account;
            string? autoLoginSessionKey = null;
            if (autoLogin)
                (tgt, autoLoginSessionKey) = await AccountGroupLogin(tgt, sndaId, AutoLoginKeepDays);

            var sessionId = await GetSessionId(tgt, guid);

            var oath = new OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                //Password = password,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoQrCode
            };
            return new LoginResult
            {
                OauthLogin = oath,
                State = LoginState.Ok,
            };
        }

        public async Task<LoginResult> LoginBySlide(string account, bool autoLogin, CancellationTokenSource cts, Action<string> showVerificationCode)
        {
            var guid = await this.GetGuid();
            // Wait for Slide
            await CancelPushMessageLogin(string.Empty, guid);
            var (pushMsgSerialNum, pushMsgSessionKey, expiration) = await SendPushMessage(account);
            showVerificationCode?.Invoke(pushMsgSerialNum);
            var (sndaId, tgt, autoLoginSessionKey) = await WaitingForSlideOnDaoyuApp(pushMsgSessionKey, pushMsgSerialNum, guid, expiration, cts, autoLogin, AutoLoginKeepDays);
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                //Password = password,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoSlide
            };
            return new LoginResult
            {
                OauthLogin = oath,
                State = LoginState.Ok,
            };
        }

        public async Task<LoginResult> LoginBySessionKey(string account, string autoLoginSessionKey)
        {
            var guid = await this.GetGuid();
            //快速登录,刷新SessionKey
            var (sndaId, tgt, newAutoLoginSessionKey) = await UpdateAutoLoginSessionKey(guid, autoLoginSessionKey);

            //快速登录
            var result = await this.GetJsonAsSdoClient("fastLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" });

            if (result.ReturnCode != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason, true);
            }

            sndaId = result.Data.SndaId;
            tgt = result.Data.Tgt;

            try
            {
                var sessionId = await GetSessionId(tgt, guid);
                var oath = new OauthLoginResult
                {
                    SessionId = sessionId,
                    InputUserId = account,
                    //Password = password,
                    SndaId = sndaId,
                    AutoLoginSessionKey = newAutoLoginSessionKey,
                    MaxExpansion = Constants.MaxExpansion,
                    LoginType = LoginType.AutoLoginSession
                };
                return new LoginResult
                {
                    OauthLogin = oath,
                    State = LoginState.Ok,
                };
            }
            catch (Exception ex)
            {
                if (ex is SdoLoginException sdoEx)
                {
                    sdoEx.RemoveAutoLoginSessionKey = true;
                    throw sdoEx;
                }
                else
                {
                    throw;
                }
            }

        }

        private async Task<string> GetSessionId(string tgt, string guid)
        {
            var promotionResult = await GetPromotionInfo(tgt);

            return await SsoLogin(tgt, guid);
        }


        public enum SdoLoginState
        {
            GotQRCode,
            WaitingScanQRCode,
            LoginSucess,
            LoginFail,
            WaitingConfirm,
            OutTime
        }

        private async Task<string> GetGuid()
        {
            var result = await this.GetJsonAsSdoClient("getGuid.json", new List<string>() { "generateDynamicKey=1" });

            if (result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());

            var dynamicKey = result.Data.DynamicKey;
            return result.Data.Guid;
        }

        private async Task<(string sndaId, string tgt, string autoLoginSessionKey)> UpdateAutoLoginSessionKey(string guid, string autoLoginSessionKey)
        {
            var result = await this.GetJsonAsSdoClient("autoLogin.json", new List<string>() { $"autoLoginSessionKey={autoLoginSessionKey}", $"guid={guid}" });
            //-10515005 "对不起，自动登录已失效，请重新登录"
            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason, true);
            Log.Information($"LoginSessionKey Updated, {(result.Data.AutoLoginMaxAge / 3600f):F1} hours left");
            autoLoginSessionKey = result.Data.AutoLoginSessionKey;
            var tgt = result.Data.Tgt;
            var sndaId = result.Data.SndaId;
            return (sndaId, tgt, autoLoginSessionKey);
        }

        #region 手机APP滑动登陆

        private async Task CancelPushMessageLogin(string pushMsgSessionKey, string guid)
        {
            // /authen/cancelPushMessageLogin.json
            await this.GetJsonAsSdoClient("cancelPushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" });
        }

        private async Task<(string pushMsgSerialNum, string pushMsgSessionKey, CancellationTokenSource slideExpiration)> SendPushMessage(string account)
        {
            var slideExpiration = new CancellationTokenSource();
            slideExpiration.CancelAfter(SlideExpirationTime);

            var result = await this.GetJsonAsSdoClient("sendPushMessage.json", new List<string>() { $"inputUserId={account}" });

            ///authen/sendPushMessage.json
            //-14001710 请确保已安装叨鱼，并保持联网
            //-1602726  该账号首次在本设备上登录，不支持一键登录，请使用二维码登录
            //-10516808 用户未确认
            if (result.ReturnCode != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }
            var pushMsgSerialNum = result.Data.PushMsgSerialNum;
            var pushMsgSessionKey = result.Data.PushMsgSessionKey;
            return (pushMsgSerialNum, pushMsgSessionKey, slideExpiration);
        }

        private async Task<(string sndaId, string tgt, string AutoLoginSessionKey)> WaitingForSlideOnDaoyuApp(
            string pushMsgSessionKey,
            string pushMsgSerialNum,
            string guid,
            CancellationTokenSource slideExpiration,
            CancellationTokenSource userCancel,
            bool autoLogin,
            int autoLoginKeepDays
            )
        {
            while (!slideExpiration.IsCancellationRequested && !userCancel.IsCancellationRequested)
            {
                var result = await this.GetJsonAsSdoClient(
                    "pushMessageLogin.json",
                    autoLogin
                        ? new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}", "autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepDays}" }
                        : new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" }
                    );
                switch (result.ReturnCode)
                {
                    case 0:
                        return (result.Data.SndaId, result.Data.Tgt, result.Data.AutoLoginSessionKey);
                    case -10516808:
                        //-10516808 用户未确认
                        await Task.Delay(1000).ConfigureAwait(false);
                        continue;
                    default:
                        throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
                }
            }
            throw new SdoLoginException((int)SdoLoginCustomExpectionCode.SLIDE_TIMEOUT_OR_CANCELED, "登录超时或被取消");
        }

        #endregion

        #region 扫码登陆

        private async Task<(string codeKey, byte[] qrCode, CancellationTokenSource cts)> GetQRCode()
        {
            // /authen/getCodeKey.json
            var qrCodeExpiration = new CancellationTokenSource();
            qrCodeExpiration.CancelAfter(QRCodeExpirationTime);

            var request = this.GetSdoHttpRequestMessage(HttpMethod.Get, "getCodeKey.json", new List<string>() { $"maxsize=89" });
            var response = await this.client.SendAsync(request);
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
            var codeKey = cookies.FirstOrDefault(x => x.StartsWith("CODEKEY="))?.Split(';')[0];
            codeKey = codeKey?.Split('=')[1];
            if (string.IsNullOrEmpty(codeKey))
            {
                throw new OauthLoginException("QRCode下载失败");
            }
            var bytes = await response.Content.ReadAsByteArrayAsync();

            Log.Information($"QRCode下载完成,CodeKey={codeKey}");
            return (codeKey, bytes, qrCodeExpiration);
        }

        private async Task<(string sndaId, string tgt, string account)> WaitingForScanQRCode(string codeKey, string guid, CancellationTokenSource qrCodeExpiration, CancellationTokenSource userCancel, int autoLoginKeepDays)
        {
            while (!qrCodeExpiration.IsCancellationRequested && !userCancel.IsCancellationRequested)
            {
                var result = await this.GetJsonAsSdoClient("codeKeyLogin.json",
                                                           new List<string>() { $"codeKey={codeKey}", $"guid={guid}", $"autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepDays}", $"maxsize=97" });

                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    return (result.Data.SndaId, result.Data.Tgt, result.Data.InputUserId);
                }

                if (result.ReturnCode == -10515805)
                {
                    //logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, "等待用户扫码...");
                    await Task.Delay(1000).ConfigureAwait(false);
                    continue;
                }
                throw new OauthLoginException(result.Data.FailReason);
            }
            throw new SdoLoginException((int)SdoLoginCustomExpectionCode.SLIDE_TIMEOUT_OR_CANCELED, "登录超时或被取消");
        }

        #endregion

        #region 登陆结束

        private async Task<string> SsoLogin(string tgt, string guid)
        {
            // /authen/ssoLogin.json 抓包的ticket=SID
            var result = await this.GetJsonAsSdoClient("ssoLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" }, tgt);

            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            return result.Data.Ticket;
        }

        private async Task<SdoLoginResult> GetPromotionInfo(string tgt)
        {
            // /authen/getPromotion.json 不知道为什么要有,但就是有
            var result = await this.GetJsonAsSdoClient("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" }, tgt);
            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            return result;
        }

        #endregion

        #region 第三方登录

        private async Task<(string sndaId, string tgt, string key)> ThirdPartyLogin(string thridUserId, string token, bool autoLogin, int autoLoginKeepDays)
        {
            //Log.Error($"TOKEN:{token}");
            //第三方登录
            var result = await this.GetJsonAsSdoClient("thirdPartyLogin",
                                                       new List<string>()
                                                       {
                                                           "companyid=310", "islimited=0", $"thridUserId={thridUserId}", $"token={token}",
                                                           autoLogin ? $"autoLoginFlag=1&autoLoginKeepTime={autoLoginKeepDays}" : "autoLoginFlag=0&autoLoginKeepTime=0"
                                                       });

            if (result.ReturnCode != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            Log.Information($"thirdPartyLogin:{result.Data.SndaId}:{result.Data.Tgt}");

            return (result.Data.SndaId, result.Data.Tgt, result.Data.AutoLoginSessionKey);

        }

        #endregion

        #region AccountGroup

        private async Task<string> GetAccountGroup(string tgt, string sndaId)
        {
            var result = await this.GetJsonAsSdoClient("getAccountGroup", new List<string>() { "serviceUrl=http%3A%2F%2Fwww.sdo.com", $"tgt={tgt}" });

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            if (!result.Data.SndaIdArray.Contains(sndaId))
                throw new SdoLoginException((int)SdoLoginCustomExpectionCode.SCAN_QRCODE_GET_ACCOUNT_FAIL, $"获取用户名失败");

            Log.Information($"getAccountGroup:{string.Join(",", result.Data.SndaIdArray)}");

            return result.Data.SndaIdArray.Contains(sndaId) ? result.Data.AccountArray[result.Data.SndaIdArray.IndexOf(sndaId)] : null;
        }

        #endregion

        #region AccountGroupLogin

        private async Task<(string tgt, string autoLoginSessionKey)> AccountGroupLogin(string tgt, string sndaId, int autoLoginKeepDays)
        {
            var result = await this.GetJsonAsSdoClient("accountGroupLogin",
                                                       new List<string>() { "serviceUrl=http%3A%2F%2Fwww.sdo.com", $"tgt={tgt}", $"sndaId={sndaId}", "autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepDays}" });
            Log.Information($"accountGroupLogin:AutoLoginMaxAge:{result.Data.AutoLoginMaxAge}");

            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            return (result.Data.Tgt, result.Data.AutoLoginSessionKey);
        }

        #endregion

        public class SdoLoginResult
        {
            [JsonProperty("error_type")]
            public int ErrorType;

            [JsonProperty("return_code")]
            public int ReturnCode;

            [JsonProperty("data")]
            public SdoLoginData Data;

            public class SdoLoginData
            {
                [JsonProperty("failReason")]
                public string FailReason;

                [JsonProperty("nextAction")]
                public int NextAction;

                [JsonProperty("guid")]
                public string Guid;

                [JsonProperty("pushMsgSerialNum")]
                public string PushMsgSerialNum;

                [JsonProperty("pushMsgSessionKey")]
                public string PushMsgSessionKey;

                [JsonProperty("dynamicKey")]
                public string DynamicKey;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("ticket")]
                public string Ticket;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("sndaId")]
                public string SndaId;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("tgt")]
                public string Tgt;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("autoLoginSessionKey")]
                public string AutoLoginSessionKey;

                [JsonProperty("autoLoginMaxAge")]
                public int AutoLoginMaxAge;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("inputUserId")]
                public string InputUserId;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("accountArray")]
                public List<string> AccountArray;

                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("sndaIdArray")]
                public List<string> SndaIdArray;
            }

            public string ToLog()
            {
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                return JsonConvert.SerializeObject(this, Formatting.Indented, settings);
            }
        }

        private HttpRequestMessage GetSdoHttpRequestMessage(HttpMethod method, string endPoint, List<string> para, string tgt = null)
        {
            var mac = SdoUtils.GetMac();
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add($"appId=100001900");
            commonParas.Add($"areaId=1");
            commonParas.Add($"appIdSite=100001900");
            commonParas.Add("locale=zh_CN");
            commonParas.Add("productId=4");
            commonParas.Add("frameType=1");
            commonParas.Add("endpointOS=1");
            commonParas.Add("version=21");
            commonParas.Add("customSecurityLevel=2");
            commonParas.Add($"deviceId={SdoUtils.GetDeviceId()}");
            commonParas.Add($"thirdLoginExtern=0");
            commonParas.Add($"productVersion=2%2e0%2e1%2e13");
            commonParas.Add($"macId={mac}");
            commonParas.Add($"tag=0");
            para.AddRange(commonParas);
            var request = new HttpRequestMessage(method, $"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            //Log.Information($"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            request.Headers.AddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.AddWithoutValidation("User-Agent", userAgent);
            request.Headers.AddWithoutValidation("Host", "cas.sdo.com");

            if (CASCID != null && SECURE_CASCID != null)
            {
                if (endPoint is "ssoLogin.json" or "getPromotionInfo.json")
                {
                    request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}; CASTGC={tgt}; CAS_LOGIN_STATE=1");
                    //Log.Information($"Added Cookie:CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}; CASTGC=***; CAS_LOGIN_STATE=1");
                }
                else if (CODEKEY != null) request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}; CODEKEY={CODEKEY}; CODEKEY_COUNT={CODEKEY_COUNT}");
                else request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}");
            }

            return request;
        }

        private static string CASCID;
        private static string SECURE_CASCID;
        private static string CODEKEY;
        private static string CODEKEY_COUNT;

        private async Task<SdoLoginResult> GetJsonAsSdoClient(string endPoint, List<string> para, string tgt = null)
        {
            var request = this.GetSdoHttpRequestMessage(HttpMethod.Get, endPoint, para, tgt);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;
            var response = await this.client.SendAsync(request);
            var reply = await response.Content.ReadAsStringAsync();
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;

            if (cookies != null)
            {
                CASCID = (CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("CASCID=")).Split(';')[0] : CASCID;
                SECURE_CASCID = (SECURE_CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("SECURE_CASCID=")).Split(';')[0] : SECURE_CASCID;

                if (cookies.Count(x => x.StartsWith("CODEKEY=")) > 0)
                {
                    CODEKEY = (CODEKEY != cookies.FirstOrDefault(x => x.StartsWith("CODEKEY=")).Split(';')[0]) ? cookies.FirstOrDefault(x => x.StartsWith("CODEKEY=")).Split(';')[0] : CODEKEY;
                    CODEKEY_COUNT = cookies.FirstOrDefault(x => x.StartsWith("CODEKEY_COUNT=")).Split(';')[0];
                }
            }

            var result = JsonConvert.DeserializeObject<SdoLoginResult>(reply);
            Log.Information($"{endPoint}:ErrorType={result.ErrorType}:ReturnCode={result.ReturnCode}:FailReason:{result.Data.FailReason}:NextAction={result.Data.NextAction}");
            Log.Debug($"GetJsonAsSdoClient({endPoint}):\n{result.ToLog()}");
            return result;
        }

        public Process? LaunchGameSdo(IGameRunner runner, string sessionId, string sndaId, string areaId, string lobbyHost, string gmHost, string dbHost,
                                      string additionalArguments, DirectoryInfo gamePath, bool encryptArguments, DpiAwareness dpiAwareness)
        {
            Log.Information(
                $"XivGame::LaunchGame(args:{additionalArguments})");
            //EnsureLoginEntry(gamePath);
            var exePath = Path.Combine(gamePath.FullName, "game", "ffxiv_dx11.exe");

            var environment = new Dictionary<string, string>();

            var argumentBuilder = new ArgumentBuilder()
                                  .Append("-AppID", "100001900")
                                  .Append("-AreaID", areaId)
                                  .Append("Dev.LobbyHost01", lobbyHost)
                                  .Append("Dev.LobbyPort01", "54994")
                                  .Append("Dev.GMServerHost", gmHost)
                                  .Append("Dev.SaveDataBankHost", dbHost)
                                  .Append("resetConfig", "0")
                                  .Append("DEV.MaxEntitledExpansionID", "1")
                                  .Append("DEV.TestSID", sessionId)
                                  .Append("XL.SndaId", sndaId);

            // This is a bit of a hack; ideally additionalArguments would be a dictionary or some KeyValue structure
            if (!string.IsNullOrEmpty(additionalArguments))
            {
                var regex = new Regex(@"\s*(?<key>[^=]+)\s*=\s*(?<value>[^\s]+)\s*", RegexOptions.Compiled);
                foreach (Match match in regex.Matches(additionalArguments))
                    argumentBuilder.Append(match.Groups["key"].Value, match.Groups["value"].Value);
            }

            if (!File.Exists(exePath))
                throw new BinaryNotPresentException(exePath);

            var workingDir = Path.Combine(gamePath.FullName, "game");

            var arguments = encryptArguments
                                ? argumentBuilder.BuildEncrypted()
                                : argumentBuilder.Build();

            return runner.Start(exePath, workingDir, arguments, environment, dpiAwareness);
        }

        public void EnsureLoginEntry(DirectoryInfo gamePath)
        {
            var bootPath = Path.Combine(gamePath.FullName, "sdo", "sdologin");
            var entryDll = Path.Combine(bootPath, "sdologinentry64.dll");
            var xlEntryDll = Path.Combine(Paths.ResourcesPath, "sdologinentry64.dll");

            if (!File.Exists(xlEntryDll))
            {
                xlEntryDll = Path.Combine(Paths.ResourcesPath, "binaries", "sdologinentry64.dll");
            }

            try
            {
                if (!Directory.Exists(bootPath))
                {
                    // 没有sdo文件夹的纯净客户端
                    Directory.CreateDirectory(bootPath);
                }

                if (!File.Exists(entryDll))
                {
                    Log.Information($"未发现sdologinentry64.dll,将复制${xlEntryDll}");
                    File.Copy(xlEntryDll, entryDll, true);
                }
                else
                {
                    if (FileVersionInfo.GetVersionInfo(entryDll).CompanyName == "ottercorp")
                    {
                        if (GetFileHash(entryDll) != GetFileHash(xlEntryDll))
                        {
                            Log.Information($"xlEntryDll:{entryDll}版本不一致,替换sdologinentry64.dll");
                            File.Copy(xlEntryDll, entryDll, true);
                        }
                        else
                        {
                            Log.Information($"sdologinentry64.dll校验成功");
                            return;
                        }
                    }
                    else
                    {
                        // 备份盛趣的sdologinentry64.dll 为 sdologinentry64.sdo.dll
                        Log.Information($"检测到sdologinentry64.dll不是ottercorp版本,备份原文件并替换");
                        File.Copy(entryDll, Path.Combine(bootPath, "sdologinentry64.sdo.dll"), true);
                        File.Copy(xlEntryDll, entryDll, true);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"未能复制{xlEntryDll}至{entryDll}\n请检查程序是否有{entryDll}的写入权限,或者{gamePath.FullName}目录下的游戏正在运行。\n{ex.Message}");
            }
        }

        public async Task<LoginResult> CheckGameUpdate(SdoArea area, DirectoryInfo gamePath, bool forceBaseVersion)
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"http://{area.AreaPatch}/http/win32/shanda_release_chs_game/{(forceBaseVersion ? Constants.BASE_GAME_VERSION : Repository.Ffxiv.GetVer(gamePath))}");

            request.Headers.AddWithoutValidation("X-Hash-Check", "enabled");
            request.Headers.AddWithoutValidation("User-Agent", Constants.PatcherUserAgent);

            EnsureVersionSanity(gamePath, Constants.MaxExpansion);
            request.Content = new StringContent(GetVersionReport(gamePath, Constants.MaxExpansion, forceBaseVersion));

            var resp = await this.client.SendAsync(request);
            var text = await resp.Content.ReadAsStringAsync();

            // Conflict indicates that boot needs to update, we do not get a patch list or a unique ID to download patches with in this case
            if (resp.StatusCode == HttpStatusCode.Conflict)
                return new LoginResult { PendingPatches = null, State = LoginState.NeedsPatchBoot, OauthLogin = null };

            if (!resp.Headers.TryGetValues("X-Patch-Unique-Id", out var uidVals))
                throw new InvalidResponseException("Could not get X-Patch-Unique-Id.", text);

            var uid = uidVals.First();

            if (string.IsNullOrEmpty(text))
                return new LoginResult { PendingPatches = null, State = LoginState.Ok, OauthLogin = null };

            Log.Verbose("Game Patching is needed... List:\n{PatchList}", text);

            var pendingPatches = PatchListParser.Parse(text);
            return new LoginResult { PendingPatches = pendingPatches, State = LoginState.NeedsPatchGame, OauthLogin = new OauthLoginResult() };
        }
    }
}
