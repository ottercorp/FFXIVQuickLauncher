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

namespace XIVLauncher.Common.Game
{
    public partial class Launcher
    {
        private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");
        private string AreaId = "1";
        private const int autoLoginKeepTime = 30;

        private static string userName = String.Empty;
        private static string password;
        private static LogEventHandler logEvent = null;
        private static bool forceQR = false;
        private static bool autoLogin = false;
        private static string autoLoginSessionKey = null;

        public async Task<LoginResult> LoginSdo(string _userName, string _password, LogEventHandler _logEvent = null, bool _forceQr = false, bool _autoLogin = false,
                                                string _autoLoginSessionKey = null)
        {
            PatchListEntry[] pendingPatches = null;

            OauthLoginResult oauthLoginResult;

            LoginState loginState;

            {
                userName = _userName;
                password = _password;
                logEvent = _logEvent;
                forceQR = _forceQr;
                autoLogin = _autoLogin;
                autoLoginSessionKey = _autoLoginSessionKey;
            }

            oauthLoginResult = await OauthLoginSdo();

            if (oauthLoginResult != null)
                loginState = LoginState.Ok;
            else loginState = LoginState.NeedRetry;

            return new LoginResult
            {
                PendingPatches = pendingPatches,
                OauthLogin = oauthLoginResult,
                State = loginState
            };
        }

        public delegate void LogEventHandler(SdoLoginState state, string logMsg);

        private static CancellationTokenSource CTS;

        public enum SdoLoginState
        {
            GotQRCode,
            WaitingScanQRCode,
            LoginSucess,
            LoginFail,
            WaitingConfirm,
            OutTime
        }

        private string sndaId = string.Empty;
        private string tgt = String.Empty;
        private string sessionId = String.Empty;
        private string dynamicKey = null;
        private string guid = null;
        private bool fastLogin => autoLogin && !string.IsNullOrEmpty(autoLoginSessionKey) && !forceQR;

        private async Task<OauthLoginResult> OauthLoginSdo()
        {
            await GetGuid();

            //尝试快速登录
            if (fastLogin)
            {
                await FastLogin();
            }

            //未成功自动登录
            if (string.IsNullOrEmpty(tgt))
            {
                //尝试密码登录
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password) && !fastLogin)
                {
                    //TODO:Wegay login
                    //long number;

                    //if (long.TryParse(userName, out number) && number is >20000000000 or < 9999999999)
                    //{
                    //    (sndaId, tgt, autoLoginSessionKey) = await ThirdPartyLogin(userName, password, autoLogin);
                    //}
                    //else

                    await StaticLogin();
                }

                //尝试手机叨鱼相关方式登录
                if (string.IsNullOrEmpty(tgt))
                {
                    var pushMsgSessionKey = String.Empty;

                    //非强制扫码,尝试滑动登录
                    if (!forceQR)
                    {
                        await CancelPushMessageLogin(pushMsgSessionKey, guid);
                        (var returnCode, var failReason, var pushMsgSerialNum, pushMsgSessionKey) = await SendPushMessage();

                        if (returnCode == 0 || returnCode == -14001710)
                        {
                            if (pushMsgSerialNum == null || pushMsgSessionKey == null)
                            {
                                logEvent?.Invoke(SdoLoginState.LoginFail, failReason);
                                return null;
                            }

                            logEvent?.Invoke(SdoLoginState.WaitingConfirm, $"操作码:{pushMsgSerialNum}");
                            CTS = new CancellationTokenSource();
                            CTS.CancelAfter(30 * 1000);
                            (sndaId, tgt, autoLoginSessionKey) = await WaitingForSlideOnDaoyuApp(pushMsgSessionKey, pushMsgSerialNum, guid, logEvent, CTS, autoLogin);
                            CTS.Dispose();
                        }
                    }

                    //滑动失败,扫码
                    if (string.IsNullOrEmpty(tgt))
                    {
                        var codeKey = await GetQRCode();
                        logEvent?.Invoke(SdoLoginState.GotQRCode, null);
                        CTS = new CancellationTokenSource();
                        CTS.CancelAfter(60 * 1000);
                        await WaitingForScanQRCode(codeKey, CTS);
                        CTS.Dispose();

                        if (!string.IsNullOrEmpty(sndaId) && !string.IsNullOrEmpty(tgt))
                        {
                            var account = await GetAccountGroup();
                            userName = string.IsNullOrEmpty(account) ? userName : account;
                            if (autoLogin) await AccountGroupLogin();
                        }
                    }

                    if (File.Exists(qrPath)) File.Delete(qrPath);
                }
            }

            //tgt或ID空白则登录失败
            if (string.IsNullOrEmpty(tgt) || string.IsNullOrEmpty(sndaId))
            {
                logEvent?.Invoke(SdoLoginState.LoginFail, $"登录失败");
                return null;
            }

            #region 登录后部分

            var promotionResult = await GetPromotionInfo();

            if (promotionResult.ErrorType != 0)
            {
                logEvent?.Invoke(SdoLoginState.LoginFail, promotionResult.Data.FailReason);
                return null;
            }

            sessionId = await SsoLogin(tgt, guid);

            if (!string.IsNullOrEmpty(sessionId)) logEvent?.Invoke(SdoLoginState.LoginSucess, "登陆成功");
            else return null;

            #endregion

            return new OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = userName,
                Password = string.IsNullOrEmpty(password) ? string.Empty : password,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                MaxExpansion = Constants.MaxExpansion
            };
        }

        private static Lazy<string> _deviceId = new Lazy<string>(() => SdoUtils.GetDeviceId());
        private static string DeviceId => _deviceId.Value;
        private static string CASCID;
        private static string SECURE_CASCID;
        private static string CODEKEY;
        private static string CODEKEY_COUNT;

        public void CancelLogin()
        {
            if (CTS != null)
            {
                Log.Information("取消登陆");
                CTS.Cancel();
            }
        }

        enum SdoClient
        {
            Daoyu,
            Launcher
        }

        private async Task GetGuid()
        {
            var result = await GetJsonAsSdoClient("getGuid.json", new List<string>() { "generateDynamicKey=1" }, SdoClient.Launcher);

            if (result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());

            dynamicKey = result.Data.DynamicKey;
            guid = result.Data.Guid;
        }

        #region 刷新AutoLoginSessionKey

        private async Task UpdateAutoLoginSessionKey()
        {
            var result = await GetJsonAsSdoClient("autoLogin.json", new List<string>() { $"autoLoginSessionKey={autoLoginSessionKey}", $"guid={guid}" }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0) result.Data.AutoLoginSessionKey = null;
            if (result.ReturnCode == -10386010)
                throw new OauthLoginException(result.Data.FailReason);
            Log.Information($"LoginSessionKey Updated, {(result.Data.AutoLoginMaxAge / 3600f):F1} hours left");

            autoLoginSessionKey = result.Data.AutoLoginSessionKey;
            tgt = result.Data.Tgt;
            sndaId = result.Data.SndaId;
        }

        #endregion

        #region 快速登陆

        //private async Task<string> ExtendLoginState(string tgtcache)
        //{
        //    //延长登录时效
        //    var result = await GetJsonAsSdoClient("extendLoginState.json", new List<string>() { $"tgt={tgtcache}" }, SdoClient.Daoyu);

        //    if (result.ReturnCode != 0 || result.ErrorType != 0)
        //    {
        //        throw new OauthLoginException(result.Data.FailReason);
        //    }

        //    var tgt = result.Data.Tgt;
        //    if (string.IsNullOrEmpty(tgt))
        //    {
        //        throw new OauthLoginException("快速登陆失败");
        //    }
        //    else
        //        return tgt;
        //}

        private async Task FastLogin()
        {
            //快速登录,刷新SessionKey

            await UpdateAutoLoginSessionKey();

            if (string.IsNullOrEmpty(autoLoginSessionKey)) return;
            //快速登录
            var result = await GetJsonAsSdoClient("fastLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            sndaId = result.Data.SndaId;
            tgt = result.Data.Tgt;
        }

        #endregion

        #region 手机APP滑动登陆

        private async Task CancelPushMessageLogin(string pushMsgSessionKey, string guid)
        {
            // /authen/cancelPushMessageLogin.json
            await GetJsonAsSdoClient("cancelPushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" }, SdoClient.Launcher);
        }

        private async Task<(int? returnCode, string failReason, string? pushMsgSerialNum, string? pushMsgSessionKey)> SendPushMessage()
        {
            // /authen/sendPushMessage.json
            var result = await GetJsonAsSdoClient("sendPushMessage.json", new List<string>() { $"inputUserId={userName}" }, SdoClient.Launcher);

            // ErrorType ReturnCode NextAction FailReason
            // 0         -14001710  0          "请确保已安装叨鱼，并保持联网"
            // 0         -10242296  0          "该账号首次在本设备上登录，不支持一键登录，请使用二维码、动态密码或密码登录"
            // 0
            // 0         0          0          null
            // 0         10516808:FailReason: 用户未确认
            if (result.ReturnCode != -14001710 && result.ReturnCode != 0 & result.ReturnCode != -10242296)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            var pushMsgSerialNum = result.Data.PushMsgSerialNum;
            var pushMsgSessionKey = result.Data.PushMsgSessionKey;
            return (result.ReturnCode, result.Data.FailReason, pushMsgSerialNum, pushMsgSessionKey);
        }

        private async Task<(string sndaId, string tgt, string AutoLoginSessionKey)> WaitingForSlideOnDaoyuApp(string pushMsgSessionKey, string pushMsgSerialNum, string guid, LogEventHandler logEvent,
                                                                                                              CancellationTokenSource cancellation, bool autoLogin = false)
        {
            while (!cancellation.IsCancellationRequested)
            {
                // /authen/pushMessageLogin.json
                var result = await GetJsonAsSdoClient("pushMessageLogin.json", autoLogin
                    ? new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}", "autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepTime}" }
                    : new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" }, SdoClient.Launcher);

                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    if (autoLogin) return (result.Data.SndaId, result.Data.Tgt, result.Data.AutoLoginSessionKey);
                    return (result.Data.SndaId, result.Data.Tgt, null);
                }
                else
                {
                    logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, result.Data.FailReason);

                    if (result.ReturnCode == -10516808)
                    {
                        logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, $"等待用户确认\n确认码:{pushMsgSerialNum}");
                        await Task.Delay(1000).ConfigureAwait(false);
                        continue;
                    }

                    throw new OauthLoginException(result.Data.FailReason);
                }
            }

            logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, "登陆超时或被取消");
            return (null, null, null);
        }

        #endregion

        #region 扫码登陆

        private async Task<string> GetQRCode()
        {
            // /authen/getCodeKey.json
            var request = GetSdoHttpRequestMessage(HttpMethod.Get, "getCodeKey.json", new List<string>() { $"maxsize=97", $"authenSource=1" }, SdoClient.Launcher);
            var response = await this.client.SendAsync(request);
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
            var codeKey = cookies.FirstOrDefault(x => x.StartsWith("CODEKEY=")).Split(';')[0];
            codeKey = codeKey.Split('=')[1];
            var bytes = await response.Content.ReadAsByteArrayAsync();

            if (File.Exists(qrPath)) File.Delete(qrPath);
            var qrDir = qrPath.Replace("QR.png", "");

            if (!Directory.Exists(qrDir))
            {
                Directory.CreateDirectory(qrDir);
            }

            using (var fileStream = File.Create(qrPath))
            {
                fileStream.Write(bytes, 0, bytes.Length);
                fileStream.Close();
                fileStream.Dispose();
            }

            Log.Information($"QRCode下载完成,CodeKey={codeKey}");
            return codeKey;
        }

        private async Task WaitingForScanQRCode(string codeKey, CancellationTokenSource cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                var result = await GetJsonAsSdoClient("codeKeyLogin.json",
                    new List<string>() { $"codeKey={codeKey}", $"guid={guid}", $"autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepTime}", $"maxsize=97" }, SdoClient.Launcher);

                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    sndaId = result.Data.SndaId;
                    tgt = result.Data.Tgt;
                    userName = result.Data.InputUserId;
                }
                else
                {
                    logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, result.Data.FailReason);

                    if (result.ReturnCode == -10515805)
                    {
                        logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, "等待用户扫码...");
                        await Task.Delay(1000).ConfigureAwait(false);
                        continue;
                    }

                    throw new OauthLoginException(result.Data.FailReason);
                }
            }

            logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, "登陆超时或被取消");
        }

        #endregion

        #region 登陆结束

        private async Task<string> SsoLogin(string tgt, string guid)
        {
            // /authen/ssoLogin.json 抓包的ticket=SID
            var result = await GetJsonAsSdoClient("ssoLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" }, SdoClient.Launcher, tgt);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());
            else
            {
                return result.Data.Ticket;
            }
        }

        private async Task<SdoLoginResult> GetPromotionInfo()
        {
            // /authen/getPromotion.json 不知道为什么要有,但就是有
            return await GetJsonAsSdoClient("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" }, SdoClient.Launcher, tgt);
        }

        #endregion

        #region 密码登录

        private async Task StaticLogin()
        {
            var macAddress = SdoUtils.GetMac();
            //密码登录
            var result = await GetJsonAsSdoClient("staticLogin.json", new List<string>()
            {
                "checkCodeFlag=1", "encryptFlag=0", $"inputUserId={userName}", $"password={password}", $"mac={macAddress}", $"guid={guid}",
                "inputUserType=0&accountDomain=1&autoLoginFlag=0&autoLoginKeepTime=0&supportPic=2"
            }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            Log.Information($"staticLogin.json:{result.Data.SndaId}:{result.Data.Tgt}");

            sndaId = result.Data.SndaId;
            tgt = result.Data.Tgt;
        }

        #endregion

        #region 第三方登录

        private async Task ThirdPartyLogin(string thridUserId, string token, bool autoLogin = false)
        {
            Log.Error($"TOKEN:{token}");
            //第三方登录
            var result = await GetJsonAsSdoClient("thirdPartyLogin",
                new List<string>()
                {
                    "companyid=310", "islimited=0", $"thridUserId={thridUserId}", $"token={token}",
                    autoLogin ? $"autoLoginFlag=1&autoLoginKeepTime={autoLoginKeepTime}" : "autoLoginFlag=0&autoLoginKeepTime=0"
                }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            Log.Information($"thirdPartyLogin:{result.Data.SndaId}:{result.Data.Tgt}");

            sndaId = result.Data.SndaId;
            tgt = result.Data.Tgt;
            autoLoginSessionKey = result.Data.AutoLoginSessionKey;
        }

        #endregion

        #region AccountGroup

        private async Task<string> GetAccountGroup()
        {
            var result = await GetJsonAsSdoClient("getAccountGroup", new List<string>() { "serviceUrl=http%3A%2F%2Fwww.sdo.com", $"tgt={tgt}" }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            if (!result.Data.SndaIdArray.Contains(sndaId)) throw new OauthLoginException($"获取用户名失败");

            Log.Information($"getAccountGroup:{string.Join(",", result.Data.SndaIdArray)}");

            return result.Data.SndaIdArray.Contains(sndaId) ? result.Data.AccountArray[result.Data.SndaIdArray.IndexOf(sndaId)] : null;
        }

        #endregion

        #region AccountGroupLogin

        private async Task AccountGroupLogin()
        {
            var result = await GetJsonAsSdoClient("accountGroupLogin",
                new List<string>() { "serviceUrl=http%3A%2F%2Fwww.sdo.com", $"tgt={tgt}", $"sndaId={sndaId}", "autoLoginFlag=1", $"autoLoginKeepTime={autoLoginKeepTime}" }, SdoClient.Launcher);
            Log.Information($"accountGroupLogin:AutoLoginMaxAge:{result.Data.AutoLoginMaxAge}");

            if (result.ReturnCode == 0 && result.Data.NextAction == 0)
            {
                tgt = result.Data.Tgt;
                autoLoginSessionKey = result.Data.AutoLoginSessionKey;
            }
            else
            {
                throw new OauthLoginException(result.Data.FailReason);
            }
        }

        #endregion

        private class SdoLoginResult
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

                [JsonProperty("ticket")]
                public string Ticket;

                [JsonProperty("sndaId")]
                public string SndaId;

                [JsonProperty("tgt")]
                public string Tgt;

                [JsonProperty("autoLoginSessionKey")]
                public string AutoLoginSessionKey;

                [JsonProperty("autoLoginMaxAge")]
                public int AutoLoginMaxAge;

                [JsonProperty("inputUserId")]
                public string InputUserId;

                [JsonProperty("accountArray")]
                public List<string> AccountArray;

                [JsonProperty("sndaIdArray")]
                public List<string> SndaIdArray;
            }
        }

        private HttpRequestMessage GetSdoHttpRequestMessage(HttpMethod method, string endPoint, List<string> para, SdoClient app, string tgt = null)
        {
            var appId = app == SdoClient.Launcher ? 100001900 : 991002627;
            var productVersion = app == SdoClient.Launcher ? "2%2e0%2e1%2e4" : "1%2e1%2e8%2e1";
            AreaId = app == SdoClient.Launcher ? "1" : "7";
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add($"appId={appId}");
            commonParas.Add($"areaId={AreaId}");
            commonParas.Add($"appIdSite={appId}");
            commonParas.Add("locale=zh_CN");
            commonParas.Add("productId=4");
            commonParas.Add("frameType=1");
            commonParas.Add("endpointOS=1");
            commonParas.Add("version=21");
            commonParas.Add("customSecurityLevel=2");
            commonParas.Add($"deviceId={DeviceId}");
            commonParas.Add($"thirdLoginExtern=0");
            commonParas.Add($"productVersion={productVersion}");
            commonParas.Add($"tag=0");
            para.AddRange(commonParas);
            var request = new HttpRequestMessage(method, $"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            //Log.Information($"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            request.Headers.AddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.AddWithoutValidation("User-Agent", _userAgent);
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

        private async Task<SdoLoginResult> GetJsonAsSdoClient(string endPoint, List<string> para, SdoClient app = SdoClient.Launcher, string tgt = null)
        {
            var request = GetSdoHttpRequestMessage(HttpMethod.Get, endPoint, para, app);

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

            try
            {
                var result = JsonConvert.DeserializeObject<SdoLoginResult>(reply);
                Log.Information($"{endPoint}:ErrorType={result.ErrorType}:ReturnCode={result.ReturnCode}:FailReason:{result.Data.FailReason}:NextAction={result.Data.NextAction}");
                //Log.Information($"Reply:{reply}");
                return result;
            }
            catch (JsonReaderException ex)
            {
                Log.Error($"Reply from {endPoint} cannot be parsed:{reply}");
                Log.Error(ex.StackTrace);
                //throw (ex);
            }

            return null;
        }

        public Process? LaunchGameSdo(IGameRunner runner, string sessionId, string sndaId, string areaId, string lobbyHost, string gmHost, string dbHost,
                                      string additionalArguments, DirectoryInfo gamePath, bool isDx11, bool encryptArguments, DpiAwareness dpiAwareness)
        {
            Log.Information(
                $"XivGame::LaunchGame(args:{additionalArguments})");
            EnsureLoginEntry(gamePath);
            var exePath = Path.Combine(gamePath.FullName, "game", "ffxiv_dx11.exe");
            if (!isDx11)
                exePath = Path.Combine(gamePath.FullName, "game", "ffxiv.exe");

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
            return new LoginResult { PendingPatches = pendingPatches, State = LoginState.NeedsPatchGame, OauthLogin = null };
        }
    }
}
