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
        private string pushMsgSessionKey = "";
        private string areaId = "1";

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

            // /authen/getGuid.json
            var jsonObj = await LoginAsLauncher("getGuid.json", new List<string>() { "generateDynamicKey=1" });
            if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                throw new OauthLoginException(jsonObj.ToString());
            var dynamicKey = jsonObj["data"]["dynamicKey"].Value<string>();
            var guid = jsonObj["data"]["guid"].Value<string>();

            //TODO:密码登录？

            //用户名及密码非空,跳过叨鱼部分
            if ((!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password)) || string.IsNullOrEmpty(tgtcache) || forceQR) useCache = false;

            if (useCache)//尝试TGT登录
            {
                //延长登录时效
                jsonObj = await LoginAsDaoyu("extendLoginState.json", new List<string>() { $"tgt={tgtcache}" });

                if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                {
                    var failReason = jsonObj["data"]["failReason"].Value<string>();
                    //logEvent?.Invoke(SdoLoginState.LoginFail, failReason);
                    useCache = false;
                }

                if (jsonObj["data"]["tgt"] != null) tgt = jsonObj["data"]["tgt"].Value<string>();

                if (string.IsNullOrEmpty(tgt)) useCache = false;

                if (useCache)
                {
                    //快速登录
                    jsonObj = await LoginAsLauncher("fastLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" });

                    if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                    {
                        var failReason = jsonObj["data"]["failReason"].Value<string>();
                        logEvent?.Invoke(SdoLoginState.LoginFail, failReason);
                        return null;
                    }

                    if (jsonObj["data"]["sndaId"] != null) sndaId = jsonObj["data"]["sndaId"].Value<string>();
                    if (jsonObj["data"]["tgt"] != null) tgt = jsonObj["data"]["tgt"].Value<string>();

                    jsonObj = await LoginAsDaoyu("getLoginUserInfo.json", new List<string>() { $"tgt={tgt}" });
                    jsonObj = await LoginAsDaoyu("getAccountInfo.json", new List<string>() { $"tgt={tgt}" });
                    //TODO:存一下用户信息?
                }

                if (string.IsNullOrEmpty(tgt) || string.IsNullOrEmpty(sndaId))
                {
                    //logEvent?.Invoke(SdoLoginState.LoginFail, $"登录失败");
                    tgt = string.Empty;
                    sndaId = string.Empty;
                    useCache = false;
                }
            }

            if (!useCache) //手机叨鱼相关
            {
                // /authen/cancelPushMessageLogin.json
                await LoginAsLauncher("cancelPushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" });

                // /authen/sendPushMessage.json
                jsonObj = await LoginAsLauncher("sendPushMessage.json", new List<string>() { $"inputUserId={userName}" });

                
                var retryTimes = 60;
                var returnCode = jsonObj["return_code"].Value<int>();

                if (returnCode == 0 && !forceQR) //叨鱼滑动登录
                {
                    if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                    {
                        var failReason = jsonObj["data"]["failReason"].Value<string>();
                        logEvent?.Invoke(SdoLoginState.LoginFail, failReason);
                        return null;
                    }
                    var pushMsgSerialNum = jsonObj["data"]["pushMsgSerialNum"].Value<string>();
                    pushMsgSessionKey = jsonObj["data"]["pushMsgSessionKey"].Value<string>();
                    logEvent?.Invoke(SdoLoginState.WaitingConfirm, $"操作码:{pushMsgSerialNum}");

                    // /authen/pushMessageLogin.json
                    CTS = new CancellationTokenSource();
                    while (retryTimes-- > 0 && !CTS.IsCancellationRequested)
                    {
                        jsonObj = await LoginAsLauncher("pushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" });
                        var error_code = jsonObj["return_code"].Value<int>();

                        if (jsonObj["return_code"].Value<int>() == 0 && jsonObj["data"]["nextAction"].Value<int>() == 0)
                        {
                            sndaId = jsonObj["data"]["sndaId"].Value<string>();
                            tgt = jsonObj["data"]["tgt"].Value<string>();
                            break;
                        }
                        else
                        {
                            var failReason = jsonObj["data"]["failReason"].Value<string>();
                            if (failReason == "用户未确认") failReason = "等待用户确认...";
                            logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, $"确认码:{pushMsgSerialNum},{failReason}");

                            if (error_code == -10516808)
                            {
                                //Log.Information("等待用户确认...");
                                await Task.Delay(1000).ConfigureAwait(false);
                                continue;
                            }

                            return null;
                        }
                    }
                }
                else //扫码
                {
                    // /authen/getCodeKey.json
                    var codeKey = await GetQRCode("getCodeKey.json", new List<string>() { $"maxsize=97", $"authenSource=1" });
                    logEvent?.Invoke(SdoLoginState.GotQRCode, null);
                    // /authen/codeKeyLogin.json
                    CTS = new CancellationTokenSource();

                    while (retryTimes-- > 0 && !CTS.IsCancellationRequested)
                    {
                        jsonObj = await LoginAsLauncher("codeKeyLogin.json", new List<string>() { $"codeKey={codeKey}", $"guid={guid}", $"autoLoginFlag=0", $"autoLoginKeepTime=0", $"maxsize=97" });
                        var error_code = jsonObj["return_code"].Value<int>();

                        if (jsonObj["return_code"].Value<int>() == 0 && jsonObj["data"]["nextAction"].Value<int>() == 0)
                        {
                            sndaId = jsonObj["data"]["sndaId"].Value<string>();
                            tgt = jsonObj["data"]["tgt"].Value<string>();
                            break;
                        }
                        else
                        {
                            var failReason = jsonObj["data"]["failReason"].Value<string>();
                            logEvent?.Invoke(SdoLoginState.WaitingScanQRCode, failReason);

                            if (error_code == -10515805)
                            {
                                //Log.Information("等待用户扫码...");
                                await Task.Delay(1000).ConfigureAwait(false);
                                continue;
                            }

                            return null;
                        }
                    }
                }

                //超时 tgt或ID空白则返回
                if (retryTimes <= 0)
                {
                    logEvent?.Invoke(SdoLoginState.OutTime, $"登录超时");
                    return null;
                }
                if (string.IsNullOrEmpty(tgt) || string.IsNullOrEmpty(sndaId))
                {
                    logEvent?.Invoke(SdoLoginState.LoginFail, $"登录失败");
                    return null;
                }

                if (File.Exists(qrPath)) File.Delete(qrPath);
            }

            // /authen/getPromotion.json 不知道为什么要有,但就是有
            jsonObj = await LoginAsLauncher("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" });
            if (jsonObj["return_code"].Value<int>() != 0)
            {
                logEvent?.Invoke(SdoLoginState.LoginFail, jsonObj["data"]["failReason"].Value<string>());
                return null;
            }

            // /authen/ssoLogin.json 抓包的ticket=SID
            var ticket = string.Empty;
            jsonObj = await LoginAsLauncher("ssoLogin.json", new List<string>() { $"tgt={tgt}", $"guid={guid}" });
            if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                throw new OauthLoginException(jsonObj.ToString());
            else
            {
                ticket = jsonObj["data"]["ticket"].Value<string>();
            }
            if (!string.IsNullOrEmpty(ticket)) logEvent?.Invoke(SdoLoginState.LoginSucess, "登陆成功");
            else return null;

            var result = new OauthLoginResult
            {
                SessionId = ticket,
                SndaId = sndaId,
                Tgt = tgt,
                MaxExpansion = Constants.MaxExpansion
            };
            
            return result;
        }

        private static string deviceId;
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
        public async Task<JObject> LoginAsLauncher(string endPoint, List<string> para)
        {
            if (deviceId == null)
                deviceId = SdoUtils.GetDeviceId();
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add("appId=100001900");
            commonParas.Add($"areaId={areaId}");
            commonParas.Add("appIdSite=100001900");
            commonParas.Add("locale=zh_CN");
            commonParas.Add("productId=4");
            commonParas.Add("frameType=1");
            commonParas.Add("endpointOS=1");
            commonParas.Add("version=21");
            commonParas.Add("customSecurityLevel=2");
            commonParas.Add($"deviceId={deviceId}");
            commonParas.Add($"thirdLoginExtern=0");
            commonParas.Add($"productVersion=2%2e0%2e1%2e4");
            commonParas.Add($"tag=0");
            para.AddRange(commonParas);
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            request.Headers.AddWithoutValidation("User-Agent", _userAgent);
            request.Headers.AddWithoutValidation("Host", "cas.sdo.com");
            if (CASCID != null && SECURE_CASCID != null)
            {
                request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}");
            }
            var response = await this.client.SendAsync(request);
            var reply = await response.Content.ReadAsStringAsync();
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
            if (cookies != null)
            {
                CASCID = (CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("CASCID=")).Split(';')[0] : CASCID;
                SECURE_CASCID = (SECURE_CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("SECURE_CASCID=")).Split(';')[0] : SECURE_CASCID;
            }
            var result = (JObject)JsonConvert.DeserializeObject(reply);
            Log.Information($"{endPoint}:ErrorCode={result["return_code"]?.Value<int>()}:FailReason:{result["data"]["failReason"]?.Value<string>()}");

            return result;
        }

        public async Task<string> GetQRCode(string endPoint, List<string> para)
        {
            if (deviceId == null)
                deviceId = SdoUtils.GetDeviceId();
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add("appId=100001900");
            commonParas.Add($"areaId={areaId}");
            commonParas.Add("appIdSite=100001900");
            commonParas.Add("locale=zh_CN");
            commonParas.Add("productId=4");
            commonParas.Add("frameType=1");
            commonParas.Add("endpointOS=1");
            commonParas.Add("version=21");
            commonParas.Add("customSecurityLevel=2");
            commonParas.Add($"deviceId={deviceId}");
            commonParas.Add($"thirdLoginExtern=0");
            commonParas.Add($"productVersion=2%2e0%2e1%2e4");
            commonParas.Add($"tag=0");
            para.AddRange(commonParas);
            var request = (HttpWebRequest)WebRequest.Create($"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            request.Method = "GET";
            request.CookieContainer = new CookieContainer(10);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            var codeKey = response.Cookies[0].Value;
            var stream = response.GetResponseStream();
            if (File.Exists(qrPath)) File.Delete(qrPath);
            using (var fileStream = File.Create(qrPath))
            {
                //stream.Seek(0, SeekOrigin.Begin);
                stream.CopyTo(fileStream);
                fileStream.Close();
                fileStream.Dispose();
            }
            Log.Information($"QRCode下载完成,CodeKey={codeKey}");
            return codeKey;
        }

        public async Task<JObject> LoginAsDaoyu(string endPoint, List<string> para)
        {
            if (deviceId == null)
                deviceId = SdoUtils.GetDeviceId();
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add("appId=991002627");
            commonParas.Add("areaId=7");
            commonParas.Add("appIdSite=991002627");
            commonParas.Add("locale=zh_CN");
            commonParas.Add("productId=4");
            commonParas.Add("frameType=1");
            commonParas.Add("endpointOS=1");
            commonParas.Add("version=21");
            commonParas.Add("customSecurityLevel=2");
            commonParas.Add($"deviceId={deviceId}");
            commonParas.Add($"thirdLoginExtern=0");
            commonParas.Add($"productVersion=1%2e1%2e8%2e1");
            commonParas.Add($"tag=0");
            para.AddRange(commonParas);
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://cas.sdo.com/authen/{endPoint}?{string.Join("&", para)}");
            request.Headers.AddWithoutValidation("User-Agent", _userAgent);
            request.Headers.AddWithoutValidation("Host", "cas.sdo.com");
            if (CASCID != null && SECURE_CASCID != null)
            {
                request.Headers.AddWithoutValidation("Cookie", $"CASCID={CASCID}; SECURE_CASCID={SECURE_CASCID}");
            }
            var response = await this.client.SendAsync(request);
            var reply = await response.Content.ReadAsStringAsync();
            var cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
            if (cookies != null)
            {
                CASCID = (CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("CASCID=")).Split(';')[0] : CASCID;
                SECURE_CASCID = (SECURE_CASCID == null) ? cookies.FirstOrDefault(x => x.StartsWith("SECURE_CASCID=")).Split(';')[0] : SECURE_CASCID;
            }
            var result = (JObject)JsonConvert.DeserializeObject(reply);
            Log.Information($"{endPoint}:ErrorCode={result["return_code"]?.Value<int>()}:FailReason:{result["data"]["failReason"]?.Value<string>()}");

            return result;
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
