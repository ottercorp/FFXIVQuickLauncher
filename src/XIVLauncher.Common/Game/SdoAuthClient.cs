using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

#if NET6_0_OR_GREATER && !WIN32
using System.Net.Security;
#endif

using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Game.Exceptions;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game
{
    /// <summary>
    /// SDO (盛趣) passport authentication client. Owns its own login HttpClient / cookie jar and
    /// speaks to cas.sdo.com (with n1.cas.sdo.com as backup). Extracted out of the Launcher class
    /// so all auth concerns live in one place.
    /// </summary>
    public class SdoAuthClient
    {
        private const int QRCodeExpirationTime = 300 * 1000;// ms
        private const int SlideExpirationTime = 30 * 1000;// ms
        private const int AutoLoginKeepDays = 30;

        // Named SDO return codes that mean "keep polling" (see SdoLoginException.cs for the wider catalogue).
        private const int ReturnCodePushMessageNotConfirmed = -10516808; // 一键登录：用户尚未在叨鱼确认
        private const int ReturnCodeQrCodeNotScanned = -10515805;        // 扫码登录：二维码尚未被扫码确认

        // sdo_login.py USER_AGENT (MSIE 8.0 form). NOTE: the live 1.1.344.45 capture actually sends
        // "Mozilla/5.0 (Windows NT 10.0; Win64; x64; Trident/7.0; rv:11.0) like Gecko sdologin-Launcher".
        private const string UserAgent =
            "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 5.1; Trident/4.0; InfoPath.2; .NET CLR 2.0.50727; " +
            "MS-RTC LM 8; .NET CLR 3.0.04506.648; .NET CLR 3.5.21022; .NET CLR 1.1.4322; " +
            ".NET CLR 3.0.4506.2152; .NET CLR 3.5.30729)";

        private readonly HttpClient loginClient;
        private readonly CookieContainer loginCookies;

        // MAC / local IP / local-GUID are stable for a client's lifetime but individually expensive
        // (P/Invoke adapter lookup, NIC enumeration, WMI disk query). GetSdoHttpRequestMessage runs once
        // per second during QR/slide polling, so cache them per client instead of recomputing each request.
        private readonly Lazy<string> cachedMac = new(SdoUtils.GetMac);
        private readonly Lazy<string> cachedLocalIp = new(SdoUtils.GetLocalIp);
        private readonly Lazy<string> cachedLocalGuid = new(SdoUtils.BuildLocalGuid);

        // runTimeId: one GUID per client instance (32 hex uppercase, no dashes), matching the capture
        // (e.g. 40A2BF590D23471A91B43F9AF0B29112) and sdo_login.py's uuid4().hex.upper() default.
        private readonly string runTimeId = Guid.NewGuid().ToString("N").ToUpperInvariant();

        public SdoAuthClient()
        {
            this.loginCookies = new CookieContainer();

            ServicePointManager.Expect100Continue = false;

#if NET6_0_OR_GREATER && !WIN32
            var sslOptions = new SslClientAuthenticationOptions()
            {
                CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                {
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_CCM_8,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_CCM,
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_CCM_8,
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_CCM,
                    TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384
                })
            };

            var loginHandler = new SocketsHttpHandler
            {
                UseCookies = true,
                CookieContainer = loginCookies,
                SslOptions = sslOptions,
            };
#else
            var loginHandler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = loginCookies,
            };
#endif

            this.loginClient = new HttpClient(loginHandler);
        }

        public Task<Launcher.LoginResult> LoginBySid(string sndaId, string sid)
        {
            // Synchronous: the WeGame SID is already the game session, so there's nothing to await.
            var oath = new Launcher.OauthLoginResult
            {
                SndaId = sndaId,
                SessionId = sid,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.WeGameSid,
            };

            return Task.FromResult(new Launcher.LoginResult
            {
                OauthLogin = oath,
                State = Launcher.LoginState.Ok,
            });
        }

        public async Task<Launcher.LoginResult> LoginBySdoStatic(string account, string password, DcTraveler dcTraveler, Func<byte[], Task<string>> solveCaptcha = null)
        {
            var (guid, dynamicKey) = await this.GetGuid();

            string encryptedUser, encryptedPassword;
            try
            {
                encryptedUser = SdoCrypto.EncryptField(dynamicKey, account);
                encryptedPassword = SdoCrypto.EncryptField(dynamicKey, password);
            }
            catch (CryptographicException)
            {
                // The derived dynamicKey landed on one of the ~16 DES weak/semi-weak keys (~2^-52 per
                // login). A fresh getGuid yields a new key, so retry exactly once.
                (guid, dynamicKey) = await this.GetGuid();
                encryptedUser = SdoCrypto.EncryptField(dynamicKey, account);
                encryptedPassword = SdoCrypto.EncryptField(dynamicKey, password);
            }

            // encryptFlag=1 (new 1.1.344.x path): server peels a single DES layer off inputUserId /
            // password. Those are base64, so their + / = must be URL-encoded. Param order mirrors
            // sdo_login.py password_login; keepLoginFlag=-1 is what the encryptFlag=1 client sends.
            var result = await this.GetJsonAsSdoClient("staticLogin.json", new List<(string, object)>()
            {
                ("checkCodeFlag", 1),
                ("encryptFlag", 1),
                ("inputUserId", encryptedUser),
                ("password", encryptedPassword),
                ("mac", this.cachedLocalGuid.Value),
                ("guid", guid),
                ("inputUserType", 0),
                ("accountDomain", 1),
                ("autoLoginFlag", 0),
                ("autoLoginKeepTime", 0),
                ("supportPic", 2),
                ("scene", "pc_login"),
                ("keepLoginFlag", -1),
            });

            // staticLogin can answer nextAction=8 (graphical captcha). If a solver was supplied, fetch
            // the image, get a code, and resubmit via checkCodeLogin (up to 3 attempts).
            result = await this.ResolveCaptcha(result, guid, solveCaptcha, keepLoginFlag: -1);

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            if (result.Data.NextAction == 8 && !string.IsNullOrEmpty(result.Data.CheckCodeUrl))
            {
                // Still blocked on a captcha: no solver was provided, the user gave up, or attempts ran out.
                throw new SdoLoginException((int)SdoLoginCustomExpectionCode.STATIC_NEED_CAPTCHA, "本次登录需要输入验证码。");
            }

            if (string.IsNullOrEmpty(result.Data.Tgt))
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            Log.Information($"staticLogin.json:{result.Data.SndaId}:{result.Data.Tgt}");

            var sndaId = result.Data.SndaId;
            var tgt = result.Data.Tgt;
            this.WireDcTraveler(dcTraveler, tgt, guid);

            var sessionId = await GetSessionId(tgt, guid);

            var oath = new Launcher.OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                SndaId = sndaId,
                AutoLoginSessionKey = null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoStatic,
            };

            return new Launcher.LoginResult
            {
                OauthLogin = oath,
                State = Launcher.LoginState.Ok,
            };
        }

        public async Task<Launcher.LoginResult> LoginByWeGameToken(string account, string token, bool autoLogin, DcTraveler dcTraveler)
        {
            var (guid, _) = await this.GetGuid();
            var (sndaId, tgt, autoLoginSessionKey) = await ThirdPartyLogin(account, token, autoLogin, AutoLoginKeepDays);
            this.WireDcTraveler(dcTraveler, tgt, guid);
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new Launcher.OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.WeGameToken,
            };
            return new Launcher.LoginResult
            {
                OauthLogin = oath,
                State = Launcher.LoginState.Ok,
            };
        }

        public async Task<Launcher.LoginResult> LoginByScanQrCode(bool autoLogin, CancellationTokenSource cts, Action<byte[]> showQrCode, DcTraveler dcTraveler)
        {
            var (guid, _) = await this.GetGuid();
            // Wait for Scan QrCode
            string? sndaId = null;
            string? tgt = null;
            string? account = null;
            string? keepLoginKey = null;

            while (!cts.IsCancellationRequested)
            {
                var (codeKey, qrCode, expiration) = await this.GetQRCode();
                showQrCode?.Invoke(qrCode);
                (sndaId, tgt, account, keepLoginKey) = await this.WaitingForScanQRCode(codeKey, guid, cts, expiration, AutoLoginKeepDays);
                if (sndaId != null)
                    break;
            }

            var newAccount = await this.GetAccountGroup(tgt, sndaId);
            account = string.IsNullOrEmpty(account) ? newAccount : account;
            string? autoLoginSessionKey = null;
            if (autoLogin)
                (tgt, autoLoginSessionKey) = await AccountGroupLogin(tgt, sndaId, AutoLoginKeepDays);

            this.WireDcTraveler(dcTraveler, tgt, guid);
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new Launcher.OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                KeepLoginKey = autoLogin ? keepLoginKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoQrCode
            };
            return new Launcher.LoginResult
            {
                OauthLogin = oath,
                State = Launcher.LoginState.Ok,
            };
        }

        public async Task<Launcher.LoginResult> LoginBySlide(string account, bool autoLogin, CancellationTokenSource cts, Action<string> showVerificationCode, DcTraveler dcTraveler)
        {
            var (guid, _) = await this.GetGuid();
            // Wait for Slide
            await CancelPushMessageLogin(string.Empty, guid);
            var (pushMsgSerialNum, pushMsgSessionKey, expiration) = await SendPushMessage(account);
            showVerificationCode?.Invoke(pushMsgSerialNum);
            var (sndaId, tgt, autoLoginSessionKey, keepLoginKey) = await WaitingForSlideOnDaoyuApp(pushMsgSessionKey, guid, expiration, cts, autoLogin, AutoLoginKeepDays);
            this.WireDcTraveler(dcTraveler, tgt, guid);
            var sessionId = await GetSessionId(tgt, guid);

            var oath = new Launcher.OauthLoginResult
            {
                SessionId = sessionId,
                InputUserId = account,
                SndaId = sndaId,
                AutoLoginSessionKey = autoLogin ? autoLoginSessionKey : null,
                KeepLoginKey = autoLogin ? keepLoginKey : null,
                MaxExpansion = Constants.MaxExpansion,
                LoginType = LoginType.SdoSlide
            };
            return new Launcher.LoginResult
            {
                OauthLogin = oath,
                State = Launcher.LoginState.Ok,
            };
        }

        public async Task<Launcher.LoginResult> LoginBySessionKey(string account, string autoLoginSessionKey, DcTraveler dcTraveler)
        {
            var (guid, _) = await this.GetGuid();
            //快速登录,刷新SessionKey
            var (sndaId, tgt, newAutoLoginSessionKey) = await UpdateAutoLoginSessionKey(guid, autoLoginSessionKey);

            //快速登录
            var result = await this.GetJsonAsSdoClient("fastLogin.json", new List<(string, object)>() { ("tgt", tgt), ("guid", guid) });

            if (result.ReturnCode != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason, true);
            }

            sndaId = result.Data.SndaId;
            tgt = result.Data.Tgt;

            try
            {
                this.WireDcTraveler(dcTraveler, tgt, guid);
                var sessionId = await GetSessionId(tgt, guid);
                var oath = new Launcher.OauthLoginResult
                {
                    SessionId = sessionId,
                    InputUserId = account,
                    SndaId = sndaId,
                    AutoLoginSessionKey = newAutoLoginSessionKey,
                    MaxExpansion = Constants.MaxExpansion,
                    LoginType = LoginType.AutoLoginSession
                };
                return new Launcher.LoginResult
                {
                    OauthLogin = oath,
                    State = Launcher.LoginState.Ok,
                };
            }
            catch (SdoLoginException sdoEx)
            {
                sdoEx.RemoveAutoLoginSessionKey = true;
                throw;
            }
        }

        private async Task<SdoLoginResult> FastInLogin(string keepLoginKey)
        {
            // /authen/v2/fastInLogin：keepLoginKey 免密登录。v2 端点走 n.cas.sdo.com 且公共参数不带 groupId。
            // 抓包 (bin/out_session.json) 响应 data 含 tgt/ticket/keepLoginKey，但 guid 为 null。
            return await this.GetJsonAsSdoClient("v2/fastInLogin", new List<(string, object)>() { ("keepLoginKey", keepLoginKey) }, v2: true);
        }

        public async Task<Launcher.LoginResult> LoginByKeepLoginKey(string account, string keepLoginKey, DcTraveler dcTraveler)
        {
            // fastInLogin 不回传 guid，故单独 getGuid 供后续 ssoLogin 使用（对齐 sdo_login.py run_qr_login：
            // 登录拿 tgt 后 client.get_guid() 再出票）。
            var (guid, _) = await this.GetGuid();
            var result = await this.FastInLogin(keepLoginKey);

            if (result.ReturnCode != 0)
            {
                var badKey = new SdoLoginException(result.ReturnCode, result.Data.FailReason);
                badKey.RemoveKeepLoginKey = true;
                throw badKey;
            }

            var sndaId = result.Data.SndaId;
            var tgt = result.Data.Tgt;
            // keepLoginKey 会轮换：响应带新值就用新值，否则续用旧值。
            var newKeepLoginKey = string.IsNullOrEmpty(result.Data.KeepLoginKey) ? keepLoginKey : result.Data.KeepLoginKey;

            try
            {
                this.WireDcTraveler(dcTraveler, tgt, guid);

                var sessionId = await GetSessionId(tgt, guid);
                var oath = new Launcher.OauthLoginResult
                {
                    SessionId = sessionId,
                    InputUserId = account,
                    SndaId = sndaId,
                    KeepLoginKey = newKeepLoginKey,
                    AutoLoginSessionKey = null,
                    MaxExpansion = Constants.MaxExpansion,
                    LoginType = LoginType.KeepLoginKeySession
                };
                return new Launcher.LoginResult
                {
                    OauthLogin = oath,
                    State = Launcher.LoginState.Ok,
                };
            }
            catch (SdoLoginException sdoEx)
            {
                sdoEx.RemoveKeepLoginKey = true;
                throw;
            }
        }

        private void WireDcTraveler(DcTraveler dcTraveler, string tgt, string guid)
        {
            if (dcTraveler == null)
                return;

            dcTraveler.RefreshDcTravelSessionIdFunc = () => this.GetDcTravelSessionId(tgt, guid);
            dcTraveler.RefreshGameSessionByGuidFunc = () => this.GetSessionId(tgt, guid);
        }

        public async Task<string> GetSessionId(string tgt, string guid)
        {
            await GetPromotionInfo(tgt);
            return await SsoLogin(tgt, guid);
        }

        public async Task<string> GetDcTravelSessionId(string tgt, string guid)
        {
            await GetPromotionInfo(tgt, "https://ff14bjz.sdo.com/RegionKanTelepo");
            return await SsoLogin(tgt, guid);
        }

        private async Task<(string guid, string dynamicKey)> GetGuid()
        {
            var result = await this.GetJsonAsSdoClient("getGuid.json", new List<(string, object)>() { ("generateDynamicKey", 1) });

            if (result.ErrorType != 0)
                throw new OauthLoginException(result.ToString());

            return (result.Data.Guid, result.Data.DynamicKey);
        }

        private async Task<(string sndaId, string tgt, string autoLoginSessionKey)> UpdateAutoLoginSessionKey(string guid, string autoLoginSessionKey)
        {
            var result = await this.GetJsonAsSdoClient("autoLogin.json", new List<(string, object)>() { ("autoLoginSessionKey", autoLoginSessionKey), ("guid", guid) });
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
            await this.GetJsonAsSdoClient("cancelPushMessageLogin.json", new List<(string, object)>() { ("pushMsgSessionKey", pushMsgSessionKey), ("guid", guid) });
        }

        private async Task<(string pushMsgSerialNum, string pushMsgSessionKey, CancellationTokenSource slideExpiration)> SendPushMessage(string account)
        {
            var slideExpiration = new CancellationTokenSource();
            slideExpiration.CancelAfter(SlideExpirationTime);

            var result = await this.GetJsonAsSdoClient("sendPushMessage.json", new List<(string, object)>() { ("inputUserId", account) });

            // /authen/sendPushMessage.json
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

        private async Task<(string sndaId, string tgt, string AutoLoginSessionKey, string keepLoginKey)> WaitingForSlideOnDaoyuApp(
            string pushMsgSessionKey,
            string guid,
            CancellationTokenSource slideExpiration,
            CancellationTokenSource userCancel,
            bool autoLogin,
            int autoLoginKeepDays
            )
        {
            while (!slideExpiration.IsCancellationRequested && !userCancel.IsCancellationRequested)
            {
                // keepLoginFlag=1 让响应额外下发 keepLoginKey（供 /authen/v2/fastInLogin 免密续期）；仅在勾选自动登录时索取。
                var result = await this.GetJsonAsSdoClient(
                    "pushMessageLogin.json",
                    autoLogin
                        ? new List<(string, object)>() { ("pushMsgSessionKey", pushMsgSessionKey), ("guid", guid), ("autoLoginFlag", 1), ("autoLoginKeepTime", autoLoginKeepDays), ("keepLoginFlag", 1) }
                        : new List<(string, object)>() { ("pushMsgSessionKey", pushMsgSessionKey), ("guid", guid) }
                    );
                switch (result.ReturnCode)
                {
                    case 0:
                        return (result.Data.SndaId, result.Data.Tgt, result.Data.AutoLoginSessionKey, result.Data.KeepLoginKey);
                    case ReturnCodePushMessageNotConfirmed:
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

            var response = await this.SendSdoHttpRequestAsync(HttpMethod.Get, "getCodeKey.json", new List<(string, object)>() { ("maxsize", 89) });
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

        private async Task<(string sndaId, string tgt, string account, string keepLoginKey)> WaitingForScanQRCode(string codeKey, string guid, CancellationTokenSource qrCodeExpiration, CancellationTokenSource userCancel, int autoLoginKeepDays)
        {
            while (!qrCodeExpiration.IsCancellationRequested && !userCancel.IsCancellationRequested)
            {
                // keepLoginFlag=1 让响应额外下发 keepLoginKey（供 /authen/v2/fastInLogin 免密续期）。
                var result = await this.GetJsonAsSdoClient("codeKeyLogin.json",
                                                           new List<(string, object)>() { ("codeKey", codeKey), ("guid", guid), ("autoLoginFlag", 1), ("autoLoginKeepTime", autoLoginKeepDays), ("keepLoginFlag", 1), ("maxsize", 97) });

                if (result.ReturnCode == 0 && result.Data.NextAction == 0)
                {
                    return (result.Data.SndaId, result.Data.Tgt, result.Data.InputUserId, result.Data.KeepLoginKey);
                }

                if (result.ReturnCode == ReturnCodeQrCodeNotScanned)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    continue;
                }
                throw new OauthLoginException(result.Data.FailReason);
            }
            throw new SdoLoginException((int)SdoLoginCustomExpectionCode.SLIDE_TIMEOUT_OR_CANCELED, "登录超时或被取消");
        }

        #endregion

        #region 图形验证码 (nextAction=8)

        private async Task<byte[]> GetCheckCodeImage(string checkCodeUrl)
        {
            // checkCodeUrl is an absolute captcha host URL (session_key baked in), not a cas /authen endpoint,
            // so it goes out through loginClient directly.
            var response = await this.loginClient.GetAsync(checkCodeUrl);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new SdoLoginException((int)response.StatusCode, $"验证码图片下载失败: {response.StatusCode}");
            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task<SdoLoginResult> CheckCodeLogin(string guid, string picCode, int keepLoginFlag = 1)
        {
            // captchaInfo is compact JSON {"picCode":"xxxx"} with a trailing newline (matches capture);
            // password is empty because the server still holds the staticLogin creds keyed by guid.
            var captchaInfo = JsonConvert.SerializeObject(new { picCode }) + "\n";
            return await this.GetJsonAsSdoClient("checkCodeLogin.json", new List<(string, object)>()
            {
                ("guid", guid),
                ("password", ""),
                ("captchaInfo", captchaInfo),
                ("keepLoginFlag", keepLoginFlag),
            });
        }

        private async Task<SdoLoginResult> ResolveCaptcha(SdoLoginResult result, string guid, Func<byte[], Task<string>> solveCaptcha, int keepLoginFlag = 1, int maxRetries = 3)
        {
            var tries = 0;
            while (solveCaptcha != null
                   && result.ReturnCode == 0 && result.ErrorType == 0
                   && result.Data.NextAction == 8
                   && !string.IsNullOrEmpty(result.Data.CheckCodeUrl)
                   && tries < maxRetries)
            {
                tries++;
                var image = await this.GetCheckCodeImage(result.Data.CheckCodeUrl);
                var code = await solveCaptcha(image);
                if (string.IsNullOrEmpty(code))
                    break;
                result = await this.CheckCodeLogin(guid, code, keepLoginFlag);
            }

            return result;
        }

        #endregion

        #region 登陆结束

        private async Task<string> SsoLogin(string tgt, string guid)
        {
            // /authen/ssoLogin.json 抓包的ticket=SID
            // scene=V3Launcher + appId=GameAppId(100001900): 抓包 (DcTraveler.xml, sdologin.exe 1.1.344.45) 用此 scene
            // 从通行证 tgt 换取 FFXIV 游戏票据 (ULS21-...); 同一张票据既是游戏 SID, 也用于 ff14bjz validateTicket。
            var result = await this.GetJsonAsSdoClient("ssoLogin.json", new List<(string, object)>() { ("tgt", tgt), ("guid", guid), ("scene", "V3Launcher") }, tgt, appId: GameAppId);

            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            return result.Data.Ticket;
        }

        private async Task<SdoLoginResult> GetPromotionInfo(string tgt, string serviceUrl = null)
        {
            // /authen/getPromotion.json 激活ticket的登录权限
            var paras = new List<(string, object)>() { ("tgt", tgt) };
            if (serviceUrl != null)
            {
                paras.Add(("serviceUrl", serviceUrl));
            }
            var result = await this.GetJsonAsSdoClient("getPromotionInfo.json", paras, tgt);
            if (result.ReturnCode != 0)
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            return result;
        }

        #endregion

        #region 第三方登录

        private async Task<(string sndaId, string tgt, string key)> ThirdPartyLogin(string thridUserId, string token, bool autoLogin, int autoLoginKeepDays)
        {
            var result = await this.GetJsonAsSdoClient("thirdPartyLogin",
                                                       new List<(string, object)>()
                                                       {
                                                           ("companyid", 310),
                                                           ("islimited", 0),
                                                           ("thridUserId", thridUserId),
                                                           ("token", token),
                                                           ("autoLoginFlag", autoLogin ? 1 : 0),
                                                           ("autoLoginKeepTime", autoLogin ? autoLoginKeepDays : 0),
                                                       },
                                                       appId: GameAppId);

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
            var result = await this.GetJsonAsSdoClient("getAccountGroup", new List<(string, object)>() { ("serviceUrl", "http://www.sdo.com"), ("tgt", tgt) });

            if (result.ReturnCode != 0 || result.ErrorType != 0)
            {
                throw new SdoLoginException(result.ReturnCode, result.Data.FailReason);
            }

            var index = result.Data.SndaIdArray.IndexOf(sndaId);
            if (index < 0)
                throw new SdoLoginException((int)SdoLoginCustomExpectionCode.SCAN_QRCODE_GET_ACCOUNT_FAIL, $"获取用户名失败");

            Log.Information($"getAccountGroup:{string.Join(",", result.Data.SndaIdArray)}");

            return result.Data.AccountArray[index];
        }

        #endregion

        #region AccountGroupLogin

        private async Task<(string tgt, string autoLoginSessionKey)> AccountGroupLogin(string tgt, string sndaId, int autoLoginKeepDays)
        {
            var result = await this.GetJsonAsSdoClient("accountGroupLogin",
                                                       new List<(string, object)>() { ("serviceUrl", "http://www.sdo.com"), ("tgt", tgt), ("sndaId", sndaId), ("autoLoginFlag", 1), ("autoLoginKeepTime", autoLoginKeepDays) });
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

                [JsonProperty("checkCodeUrl")]
                public string CheckCodeUrl;

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

                // ULSKLK-...: 免密登录令牌（keepLoginFlag=1 时随各登录响应下发），供 /authen/v2/fastInLogin 续用。
                [JsonConverter(typeof(MaskMiddleConverter))]
                [JsonProperty("keepLoginKey")]
                public string KeepLoginKey;

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

            public override string ToString() => ToLog();
        }

        // 主/备域名故障转移（复刻二进制 hostName/hostName2 与 sdo_login.py _fetch）：cas.sdo.com(主)
        // 失败 -> n1.cas.sdo.com(备)。触发：连接异常、HTTP 非 200(如 403)、期望 JSON 时正文非 JSON
        // (403 常回 HTML/XML)。成功的备用节点会被“粘住”，后续请求优先走它——与二进制一致。
        private const string PrimaryHost = "cas.sdo.com";
        private const string BackupHost = "n1.cas.sdo.com";
        // /authen/v2/* (fastInLogin/getSystemConfig/...) 走 n.cas.sdo.com（DLL 内写死，见 sdo_login.py API_HOST），
        // 备用同为 n1.cas.sdo.com。普通 /authen/*.json 仍走 cas.sdo.com(主)->n1.cas.sdo.com(备)。
        private const string ApiHost = "n.cas.sdo.com";
        private string preferredHost;

        // 抓包 (bin/HTTPDebuggerSession.xml / DcTraveler.xml / out_session.json) 的双 appId 模型（对齐 sdo_login.py
        // BOX_APP_ID / GAME_APP_ID）：登录态端点 (getGuid/staticLogin/pushMessageLogin/fastInLogin/getPromotionInfo…)
        // 走通行证登录框 appId=791000814；仅 ssoLogin 出 FFXIV 游戏票据时切到游戏 appId=100001900。appIdSite 同 appId。
        private const int BoxAppId = 791000814;
        private const int GameAppId = 100001900;

        private List<string> CandidateHosts(bool v2 = false)
        {
            var ordered = new List<string> { v2 ? ApiHost : PrimaryHost, BackupHost };
            if (this.preferredHost != null && ordered.Contains(this.preferredHost) && ordered[0] != this.preferredHost)
            {
                ordered.Remove(this.preferredHost);
                ordered.Insert(0, this.preferredHost);
            }

            return ordered;
        }

        private void StickPreferredHost(string host)
        {
            // A non-primary host that just worked becomes sticky for later requests (matches the binary).
            if (host != PrimaryHost)
                this.preferredHost = host;
        }

        private static bool LooksJson(string body)
        {
            var trimmed = body?.TrimStart();
            return !string.IsNullOrEmpty(trimmed) && (trimmed[0] == '{' || trimmed[0] == '[');
        }

        // Mirrors sdo_login.py _enc = urllib.parse.quote(value, safe=":"): percent-encode every
        // reserved char (so base64 + / = and captchaInfo's { } " \n are encoded) but keep ':' literal,
        // because deviceId is "mac:cpu:disk" and the capture keeps those colons raw.
        private static string EscapeValue(object value)
        {
            var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            return Uri.EscapeDataString(s).Replace("%3A", ":");
        }

        // Shared main/backup host failover (mirrors the binary's hostName/hostName2 and sdo_login.py _fetch):
        // try each candidate host, skipping on connection error, non-200, or (when readBody) a non-JSON body
        // (a 403 edge page returns HTML/XML). The first host that passes becomes sticky for later requests.
        private async Task<(HttpResponseMessage response, string body)> SendWithHostFailoverAsync(
            HttpMethod method, string endPoint, List<(string, object)> para, string tgt, bool v2, int appId, bool readBody)
        {
            var attempts = new List<string>();
            foreach (var host in this.CandidateHosts(v2))
            {
                HttpResponseMessage response;
                string body = null;
                try
                {
                    response = await this.loginClient.SendAsync(this.GetSdoHttpRequestMessage(method, endPoint, para, host, tgt, v2, appId));
                    if (readBody)
                        body = await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    attempts.Add($"{host}: 连接失败 {ex.GetType().Name}");
                    Log.Warning($"SDO {endPoint} via {host} 连接失败({ex.GetType().Name})，尝试下一个域名");
                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    attempts.Add($"{host}: HTTP {(int)response.StatusCode}");
                    Log.Warning($"SDO {endPoint} via {host} 返回 HTTP {(int)response.StatusCode}，尝试下一个域名");
                    continue;
                }

                if (readBody && !LooksJson(body))
                {
                    // 200 but the body isn't JSON — typically a 403/blocked HTML/XML page from the edge.
                    attempts.Add($"{host}: 正文非JSON({body.Length}B)");
                    Log.Warning($"SDO {endPoint} via {host} 正文非 JSON（疑似拦截页），尝试下一个域名");
                    continue;
                }

                this.StickPreferredHost(host);
                return (response, body);
            }

            throw new SdoLoginException((int)SdoLoginCustomExpectionCode.PASSPORT_ALL_HOSTS_FAILED, $"SDO 通行证主/备域名均失败 [{string.Join("; ", attempts)}]");
        }

        private async Task<HttpResponseMessage> SendSdoHttpRequestAsync(HttpMethod method, string endPoint, List<(string, object)> para, string tgt = null, int appId = BoxAppId)
        {
            // Raw failover for binary endpoints (e.g. the QR/codeKey image) where we also need the
            // response headers. Fails over on connection error or non-200; JSON is not validated here.
            var (response, _) = await this.SendWithHostFailoverAsync(method, endPoint, para, tgt, v2: false, appId: appId, readBody: false);
            return response;
        }

        private HttpRequestMessage GetSdoHttpRequestMessage(HttpMethod method, string endPoint, List<(string Key, object Value)> para, string host, string tgt = null, bool v2 = false, int appId = BoxAppId)
        {
            var mac = this.cachedMac.Value;
            // Common params aligned to sdo_login.py + the 1.1.344.45 capture (bin/HTTPDebuggerSession.xml):
            // groupId=1 (after areaId) and channelId=0 (after runTimeId) are present on /authen/*.json; epIp is the
            // real LAN IP; runTimeId is a per-run GUID; productVersion is the passport-SDK version 1.1.344.45.
            // appId 默认走登录框 BoxAppId；仅 ssoLogin 传入 GameAppId（详见 BoxAppId/GameAppId 注释），appIdSite 同 appId。
            var commonParas = new List<(string, object)>() {
                ("authenSource", 1),
                ("appId", appId),
                ("areaId", 1),
            };
            // /authen/v2/* (fastInLogin 等) 抓包实测不带 groupId（sdo_login.py include_group_id=False）；
            // 其余 /authen/*.json 在 areaId 之后紧跟 groupId=1。
            if (!v2)
                commonParas.Add(("groupId", 1));
            commonParas.AddRange(new List<(string, object)>() {
                ("appIdSite", appId),
                ("locale", "zh_CN"),
                ("productId", 4),
                ("frameType", 1),
                ("endpointOS", 1),
                ("version", 21),
                ("customSecurityLevel", 2),
                ("deviceId", SdoUtils.GetDeviceId()),
                ("thirdLoginExtern", 0),
                ("macId", mac),
                ("epIp", this.cachedLocalIp.Value),
                ("epName", SdoUtils.GetHostName()),
                ("extendInfo", ""),
                ("sdoVersion", ""),
                ("runTimeId", this.runTimeId),
                ("channelId", 0),
                ("productVersion", "1.1.344.45"),
                ("tag", 0),
            });
            // Don't mutate the caller's list: the failover loop calls this once per candidate host.
            var allParams = new List<(string Key, object Value)>(para);
            allParams.AddRange(commonParas);
            var query = string.Join("&", allParams.Select(p => $"{p.Key}={EscapeValue(p.Value)}"));
            var request = new HttpRequestMessage(method, $"https://{host}/authen/{endPoint}?{query}");
            // The real client (bin/HTTPDebuggerSession.xml) sends only Host + User-Agent + Accept on /authen/*.
            // Cache-Control / CASCID / SECURE_CASCID / CAS_LOGIN_STATE do NOT exist in SdoBaseClient.dll, so we
            // don't fabricate them (the server issues CASCID via Set-Cookie itself). Accept: */* is WinINet's default.
            request.Headers.AddWithoutValidation("Host", host);
            request.Headers.AddWithoutValidation("User-Agent", UserAgent);
            request.Headers.AddWithoutValidation("Accept", "*/*");
            if (endPoint is "ssoLogin.json")
            {
                // CASTGC=<tgt> is the only cookie the binary builds, and only for the CAS SSO step (ssoLogin.json).
                request.Headers.AddWithoutValidation("Cookie", $"CASTGC={tgt}");
            }
            return request;
        }


        private async Task<SdoLoginResult> GetJsonAsSdoClient(string endPoint, List<(string, object)> para, string tgt = null, bool v2 = false, int appId = BoxAppId)
        {
            var (_, reply) = await this.SendWithHostFailoverAsync(HttpMethod.Get, endPoint, para, tgt, v2, appId, readBody: true);

            SdoLoginResult result;
            try
            {
                result = JsonConvert.DeserializeObject<SdoLoginResult>(reply) ?? new SdoLoginResult();
            }
            catch (JsonReaderException ex)
            {
                // JSON-looking yet unparseable: a genuine content problem, not a host problem — surface it.
                throw new JsonReaderException($"{ex.Message}\n {reply}");
            }

            // Guarantee Data is non-null so downstream (which reads result.Data.*) never NREs on a sparse response.
            result.Data ??= new SdoLoginResult.SdoLoginData();

            Log.Information($"{endPoint}:ErrorType={result.ErrorType}:ReturnCode={result.ReturnCode}:FailReason:{result.Data.FailReason}:NextAction={result.Data.NextAction}");
            Log.Debug($"GetJsonAsSdoClient({endPoint}):\n{result.ToLog()}");
            return result;
        }
    }
}
