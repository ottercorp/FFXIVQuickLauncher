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

namespace XIVLauncher.Common.Game
{
    public partial class Launcher
    {
        public async Task<LoginResult> LoginSdo(string userName, LogEventHandler logEvent = null)
        {
            PatchListEntry[] pendingPatches = null;

            OauthLoginResult oauthLoginResult;

            LoginState loginState;


            oauthLoginResult = await OauthLoginSdo(userName, logEvent);

            if (oauthLoginResult != null)
                loginState = LoginState.Ok;
            else loginState = LoginState.NoService;

            return new LoginResult
            {
                PendingPatches = pendingPatches,
                OauthLogin = oauthLoginResult,
                State = loginState,
                //UniqueId = uid
            };
        }

        public delegate void LogEventHandler(bool? isSucceed, string logMsg);
        private async Task<OauthLoginResult> OauthLoginSdo(string userName, LogEventHandler logEvent)
        {
            // /authen/getGuid.json
            var jsonObj = await LoginAsLauncher("getGuid.json", new List<string>() { "generateDynamicKey=1" });
            if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
                throw new OauthLoginException(jsonObj.ToString());

            var dynamicKey = jsonObj["data"]["dynamicKey"].Value<string>();
            var guid = jsonObj["data"]["guid"].Value<string>();

            // /authen/cancelPushMessageLogin.json
            await LoginAsLauncher("cancelPushMessageLogin.json", new List<string>() { "pushMsgSessionKey=", $"guid={guid}" });

            // /authen/sendPushMessage.json
            jsonObj = await LoginAsLauncher("sendPushMessage.json", new List<string>() { $"inputUserId={userName}" });
            if (jsonObj["return_code"].Value<int>() != 0 || jsonObj["error_type"].Value<int>() != 0)
            {
                if (jsonObj["return_code"].Value<int>() == -10242296)
                {
                    string[] IDs = deviceId.Split(':');
                    // if the disk serial in sdoLogin is empty
                    if (IDs.Length == 3 && IDs[2].Length != 0)
                    {
                        IDs[2] = "";
                        deviceId = string.Join(":", IDs);
                        await OauthLoginSdo(userName, logEvent);
                        return null;
                    }
                }
                var failReason = jsonObj["data"]["failReason"].Value<string>();
                logEvent?.Invoke(false, failReason);
                return null;
            }

            var pushMsgSerialNum = jsonObj["data"]["pushMsgSerialNum"].Value<string>();
            var pushMsgSessionKey = jsonObj["data"]["pushMsgSessionKey"].Value<string>();
            logEvent?.Invoke(null, $"操作码:{pushMsgSerialNum}");

            // /authen/pushMessageLogin.json
            var sndaId = String.Empty;
            var tgt = String.Empty;
            var retryTimes = 30;
            while (retryTimes-- > 0)
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
                    logEvent?.Invoke(false, $"确认码:{pushMsgSerialNum},{failReason}");
                    if (error_code == -10516808)
                    {
                        Log.Information("等待用户确认...");
                        await Task.Delay(1000).ConfigureAwait(false);
                        continue;
                    }
                    return null;
                }
            }
            //超时 tgt或ID空白则返回
            if (retryTimes <= 0)
            {
                logEvent?.Invoke(false, $"登录超时");
                return null;
            }
            if (string.IsNullOrEmpty(tgt) || string.IsNullOrEmpty(sndaId))
            {
                logEvent?.Invoke(false, $"登录失败");
                return null;
            }

            // /authen/getPromotion.json 不知道为什么要有,但就是有
            jsonObj = await LoginAsLauncher("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" });
            if (jsonObj["return_code"].Value<int>() != 0)
            {
                logEvent?.Invoke(false, jsonObj["data"]["failReason"].Value<string>());
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
            if (!string.IsNullOrEmpty(ticket)) logEvent?.Invoke(true, "登陆成功");
            else return null;

            return new OauthLoginResult
            {
                SessionId = ticket,
                SndaId = sndaId,
            };
        }

        private static string deviceId;
        public async Task<JObject> LoginAsLauncher(string endPoint, List<string> para)
        {
            if (deviceId == null)
                deviceId = SdoUtils.GetDeviceId();
            var commonParas = new List<string>();
            commonParas.Add("authenSource=1");
            commonParas.Add("appId=100001900");
            commonParas.Add("areaId=7");
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
            var response = await this.client.SendAsync(request);
            var reply = await response.Content.ReadAsStringAsync();
            return (JObject)JsonConvert.DeserializeObject(reply);
        }

        public void EnsureLoginEntry() {
            // 通过文件版本信息，检测是否存在第三方sdologinentry64.dll以及原版sdologinentry64.dll（被重命名为sdologinentry64.sdo.dll）
            //throw new NotImplementedException();
        }
        public object? LaunchGameSdo(IGameRunner runner, string sessionId, string sndaId, string areaId, string lobbyHost, string gmHost, string dbHost,
             string additionalArguments, DirectoryInfo gamePath, bool isDx11, bool encryptArguments, DpiAwareness dpiAwareness)
        {
            Log.Information(
                $"XivGame::LaunchGame(args:{additionalArguments})");
            EnsureLoginEntry();
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
    }
}
