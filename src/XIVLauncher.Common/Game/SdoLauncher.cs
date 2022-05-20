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
            var ticket = String.Empty;
            var retryTimes = 30;
            while (retryTimes-->0)
            {
                jsonObj = await LoginAsLauncher("pushMessageLogin.json", new List<string>() { $"pushMsgSessionKey={pushMsgSessionKey}", $"guid={guid}" });
                var error_code = jsonObj["return_code"].Value<int>();
                if (jsonObj["return_code"].Value<int>() == 0 && jsonObj["data"]["nextAction"].Value<int>() == 0)
                {
                    sndaId = jsonObj["data"]["sndaId"].Value<string>();
                    tgt = jsonObj["data"]["tgt"].Value<string>();
                    ticket = jsonObj["data"]["ticket"].Value<string>();
                    break;
                }
                else
                {
                    var failReason = jsonObj["data"]["failReason"].Value<string>();
                    logEvent?.Invoke(false, failReason);
                    if (error_code == -10516808)
                    {
                        Log.Information("等待用户确认...");
                        await Task.Delay(1000).ConfigureAwait(false);           
                        continue;
                    }
                    return null;
                }
            }

            jsonObj = await LoginAsLauncher("getPromotionInfo.json", new List<string>() { $"tgt={tgt}" });
            if (jsonObj["return_code"].Value<int>() != 0)
            {
                logEvent?.Invoke(false, jsonObj["data"]["failReason"].Value<string>());
            }
            logEvent?.Invoke(true, "登陆成功");

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
    }
}
