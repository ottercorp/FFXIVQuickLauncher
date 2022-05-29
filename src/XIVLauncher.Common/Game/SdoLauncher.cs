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

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.Encryption;
using XIVLauncher.Common.Game.Exceptions;
using XIVLauncher.Common.PlatformAbstractions;
using Newtonsoft.Json.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game
{
    public partial class Launcher
    {
        private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");
        private string AreaId = "1";

        public async Task<LoginResult> LoginSdo(string userName, string password, LogEventHandler logEvent = null, bool forceQr = false, bool useCache = false, string tgtcache = null)
        {
            PatchListEntry[] pendingPatches = null;

            OauthLoginResult oauthLoginResult;

            LoginState loginState;


            oauthLoginResult = await OauthLoginSdo(userName, password, logEvent, forceQr, useCache, tgtcache);

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
        private async Task<OauthLoginResult> OauthLoginSdo(string userName, string password, LogEventHandler logEvent, bool forceQR, bool useCache, string tgtcache = null)
        {
            var sndaId = String.Empty;
            var tgt = String.Empty;
            var sessionId = String.Empty;
            // /authen/getGuid.json
            (var dynamicKey, var guid) = await GetGuid();
            //TODO:密码登录？

            //用户名及密码非空,跳过叨鱼部分
            if ((!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password)) || string.IsNullOrEmpty(tgtcache) || forceQR) useCache = false;

            if (useCache)//尝试TGT登录
            {
                try
                {
                    //延长登录时效
                    tgt = await ExtendLoginState(tgtcache);

                    //快速登录
                    (sndaId, tgt) = await FastLogin(tgt, guid);

                    //jsonObj = await LoginAsDaoyu("getLoginUserInfo.json", new List<string>() { $"tgt={tgt}" });
                    //jsonObj = await LoginAsDaoyu("getAccountInfo.json", new List<string>() { $"tgt={tgt}" });
                    //TODO:存一下用户信息?
                }
                catch (OauthLoginException ex)
                {
                    logEvent?.Invoke(SdoLoginState.LoginFail, ex.Message);
                    useCache = false;
                }
            }

            if (!useCache) //手机叨鱼相关
            {
                var pushMsgSessionKey = String.Empty;
                await CancelPushMessageLogin(pushMsgSessionKey, guid);

                (var returnCode, var failReason, var pushMsgSerialNum, pushMsgSessionKey) = await SendPushMessage(userName);
                // 叨鱼已经打开/未打开，其余情况一律扫码
                if ((returnCode == 0|| returnCode == -14001710) && !forceQR) //叨鱼滑动登录
                {
                    if (pushMsgSerialNum == null || pushMsgSessionKey == null)
                    {
                        logEvent?.Invoke(SdoLoginState.LoginFail, failReason);
                        return null;
                    }
                    logEvent?.Invoke(SdoLoginState.WaitingConfirm, $"操作码:{pushMsgSerialNum}");
                    CTS = new CancellationTokenSource();
                    CTS.CancelAfter(30 * 1000);
                    (sndaId, tgt) = await WaitingForSlideOnDaoyuApp(pushMsgSessionKey, pushMsgSerialNum, guid, logEvent, CTS);
                    CTS.Dispose();
                }
                else //扫码
                {
                    var codeKey = await GetQRCode();
                    logEvent?.Invoke(SdoLoginState.GotQRCode, null);
                    CTS = new CancellationTokenSource();
                    CTS.CancelAfter(60 * 1000);
                    (sndaId, tgt) = await WaitingForScanQRCode(codeKey, guid, logEvent, CTS);
                    CTS.Dispose();
                }

                if (File.Exists(qrPath)) File.Delete(qrPath);
            }

            //tgt或ID空白则返回
            if (string.IsNullOrEmpty(tgt) || string.IsNullOrEmpty(sndaId))
            {
                logEvent?.Invoke(SdoLoginState.LoginFail, $"登录失败");
                return null;
            }

            var promotionResult = await GetPromotionInfo(tgt, guid);
            if (promotionResult.ErrorType != 0)
            {
                logEvent?.Invoke(SdoLoginState.LoginFail, promotionResult.Data.FailReason);
                return null;
            }

            sessionId = await SsoLogin(tgt, guid);

            if (!string.IsNullOrEmpty(sessionId)) logEvent?.Invoke(SdoLoginState.LoginSucess, "登陆成功");
            else return null;

            return new OauthLoginResult
            {
                SessionId = sessionId,
                SndaId = sndaId,
                Tgt = tgt,
                MaxExpansion = Constants.MaxExpansion
            };
        }

        private static Lazy<string> _deviceId = new Lazy<string>(() => SdoUtils.GetDeviceId());
        private static string DeviceId => _deviceId.Value;
        private static string CASCID;
        private static string SECURE_CASCID;

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

        private async Task<(string dynamicKey, string guid)> GetGuid()
        {
            var result = await GetJsonAsSdoClient("getGuid.json", new List<string>() { "generateDynamicKey=1" }, SdoClient.Launcher);

            if (result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());
            return (result.Data.DynamicKey, result.Data.Guid);
        }

        #region 快速登陆

        private async Task<string> ExtendLoginState(string tgtcache)
        {
            //延长登录时效
            var result = await GetJsonAsSdoClient("extendLoginState.json", new List<string>() { $"tgt={tgtcache}" }, SdoClient.Daoyu);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            var tgt = result.Data.Tgt;
            if (string.IsNullOrEmpty(tgt))
            {
                throw new OauthLoginException("快速登陆失败");
            }
            else
                return tgt;
        }

        private async Task<(string sndaId, string tgt)> FastLogin(string tgt, string guid)
        {
            //快速登录
            var result = await GetJsonAsSdoClient("fastLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" }, SdoClient.Launcher);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new OauthLoginException(result.Data.FailReason);
            }

            return (result.Data.SndaId, result.Data.Tgt);
        }

        #endregion

        #region 手机APP滑动登陆

        private async Task CancelPushMessageLogin(string pushMsgSessionKey, string guid)
        {
            // /authen/cancelPushMessageLogin.json
            await GetJsonAsSdoClient("cancelPushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" }, SdoClient.Launcher);
        }

        private async Task<(int? returnCode, string failReason, string? pushMsgSerialNum, string? pushMsgSessionKey)> SendPushMessage(string userName)
        {
            // /authen/sendPushMessage.json
            var result = await GetJsonAsSdoClient("sendPushMessage.json", new List<string>() { $"inputUserId={userName}" }, SdoClient.Launcher);
            // ErrorType ReturnCode NextAction FailReason
            // 0         -14001710  0          "请确保已安装叨鱼，并保持联网"
            // 0         -10242296  0          "该账号首次在本设备上登录，不支持一键登录，请使用二维码、动态密码或密码登录"
            // 0         0          0          null
            var pushMsgSerialNum = result.Data.PushMsgSerialNum;
            var pushMsgSessionKey = result.Data.PushMsgSessionKey;
            return (result.ReturnCode, result.Data.FailReason, pushMsgSerialNum, pushMsgSessionKey);
        }

        private async Task<(string sndaId, string tgt)> WaitingForSlideOnDaoyuApp(string pushMsgSessionKey, string pushMsgSerialNum, string guid, LogEventHandler logEvent, CancellationTokenSource cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                // /authen/pushMessageLogin.json
                var result = await GetJsonAsSdoClient("pushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" }, SdoClient.Launcher);
                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    return (result.Data.SndaId, result.Data.Tgt);
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
            return (null, null);
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
            using (var fileStream = File.Create(qrPath))
            {
                fileStream.Write(bytes, 0, bytes.Length);
                fileStream.Close();
                fileStream.Dispose();
            }
            Log.Information($"QRCode下载完成,CodeKey={codeKey}");
            return codeKey;
        }

        private async Task<(string sndaId, string tgt)> WaitingForScanQRCode(string codeKey, string guid, LogEventHandler logEvent, CancellationTokenSource cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                // /authen/pushMessageLogin.json
                var result = await GetJsonAsSdoClient("codeKeyLogin.json", new List<string>() { $"codeKey={codeKey}", $"guid={guid}", $"autoLoginFlag=0", $"autoLoginKeepTime=0", $"maxsize=97" }, SdoClient.Launcher);
                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    return (result.Data.SndaId, result.Data.Tgt);
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
            return (null, null);
        }

        #endregion

        #region 登陆结束

        private async Task<string> SsoLogin(string tgt, string guid)
        {
            // /authen/ssoLogin.json 抓包的ticket=SID
            var result = await GetJsonAsSdoClient("ssoLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" });
            if (result.ReturnCode != 0 || result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());
            else
            {
                return result.Data.Ticket;
            }
        }

        private async Task<SdoLoginResult> GetPromotionInfo(string tgt, string guid)
        {
            // /authen/getPromotion.json 不知道为什么要有,但就是有
            return await GetJsonAsSdoClient("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" });
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
            }
        }

        private HttpRequestMessage GetSdoHttpRequestMessage(HttpMethod method, string endPoint, List<string> para, SdoClient app)
        {
            var appId = app == SdoClient.Launcher ? 100001900 : 991002627;
            var productVersion = app == SdoClient.Launcher ? "2%2e0%2e1%2e4" : "1%2e1%2e8%2e1";
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
            request.Headers.AddWithoutValidation("User-Agent", _userAgent);
            request.Headers.AddWithoutValidation("Host", "cas.sdo.com");
            if (CASCID != null && SECURE_CASCID != null)
            {
                request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}");
            }
            return request;
        }

        private async Task<SdoLoginResult> GetJsonAsSdoClient(string endPoint, List<string> para, SdoClient app = SdoClient.Launcher)
        {
            var request = GetSdoHttpRequestMessage(HttpMethod.Get, endPoint, para, app);

            var response = await this.client.SendAsync(request);
            var reply = await response.Content.ReadAsStringAsync();
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
            if (cookies != null)
            {
                CASCID = (CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("CASCID=")).Split(';')[0] : CASCID;
                SECURE_CASCID = (SECURE_CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("SECURE_CASCID=")).Split(';')[0] : SECURE_CASCID;
            }
            try
            {
                var result = JsonConvert.DeserializeObject<SdoLoginResult>(reply);
                Log.Information($"{endPoint}:ErrorType={result.ErrorType}:ReturnCode={result.ReturnCode}:FailReason:{result.Data.FailReason}");
                return result;
            }
            catch (JsonReaderException ex)
            {
                Log.Error($"Reply from {endPoint} cannot be parsed:{reply}");
                Log.Error(ex.StackTrace);
                throw (ex);
            }
        }

        public object? LaunchGameSdo(IGameRunner runner, string sessionId, string sndaId, string areaId, string lobbyHost, string gmHost, string dbHost,
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
            // 通过文件版本信息，检测是否存在第三方sdologinentry64.dll以及原版sdologinentry64.dll（被重命名为sdologinentry64.sdo.dll）
            var bootPath = Path.Combine(gamePath.FullName, "sdo", "sdologin");
            var entryDll = Path.Combine(bootPath, "sdologinentry64.dll");
            var sdoEntryDll = Path.Combine(bootPath, "sdologinentry64.sdo.dll");
            var xlEntryDll = Path.Combine(Paths.ResourcesPath, "sdologinentry64.dll");
            var entryDllVersion = FileVersionInfo.GetVersionInfo(entryDll);
            var xlEntryDllVersion = FileVersionInfo.GetVersionInfo(xlEntryDll);
            if (File.Exists(entryDll))
            {
                if (entryDllVersion.CompanyName != "ottercorp")
                {
                    Log.Information($"复制EntryDll");
                    File.Copy(entryDll, sdoEntryDll, true);
                    File.Copy(xlEntryDll, entryDll, true);
                }
                else
                {
                    if (GetFileHash(entryDll) != GetFileHash(xlEntryDll))
                    {
                        Log.Information($"xlEntryDll:{entryDll}版本不一致，{entryDllVersion.FileVersion}->{xlEntryDllVersion.FileVersion}");
                        File.Copy(xlEntryDll, entryDll, true);
                    }
                }

            }
            if (File.Exists(sdoEntryDll))
                return;
            else
            {
                throw new BinaryNotPresentException(sdoEntryDll);
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
