using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.Encryption;
using XIVLauncher.Common.Game.Exceptions;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game
{
    public class GuiLoginType
    {
        public LoginType LoginType { get; set; }
        public string DisplayName { get; set; }

        public static List<GuiLoginType> Get()
        {
            var types = new List<GuiLoginType>
            {
                new GuiLoginType { LoginType = LoginType.SdoSlide, DisplayName = "一键登录" },
                new GuiLoginType { LoginType = LoginType.SdoQrCode, DisplayName = "扫码登录" },
                new GuiLoginType { LoginType = LoginType.SdoStatic, DisplayName = "密码登录" },
                new GuiLoginType { LoginType = LoginType.WeGameToken, DisplayName = "WeGame" }
            };
            return types;
        }
    }
    public enum LoginType
    {
        SdoStatic,
        SdoSlide,
        SdoQrCode,
        WeGameToken,
        // WeGame 抓包合并后，WeGameSid 不再出现在 GUI 上，仅用于"使用SID"模式复用已存 SID（LoginBySid）。
        WeGameSid,
        // 仅在内部使用，不出现在GUI上
        AutoLoginSession,
        // keepLoginKey 免密登录 (/authen/v2/fastInLogin)，仅内部使用
        KeepLoginKeySession
    }

    public partial class Launcher
    {
        public Process? LaunchGameSdo(IGameRunner runner, string sessionId, string sndaId, int dcTravelPort, int dcLoginPort, string areaId, string lobbyHost, string gmHost, string dbHost, string areasInfo,
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
                                  .Append("XL.SndaId", sndaId)
                                  .Append("XL.LobbyHosts", $"{areasInfo}");

            if (dcTravelPort > 0)
                argumentBuilder.Append("XL.DcTraveler", $"{dcTravelPort}");

            if (dcLoginPort > 0)
                argumentBuilder.Append("XL.DcLogin", $"{dcLoginPort}");
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
            {
                Log.Error($"RequestUri:{request.RequestUri}");
                Log.Error($"Content:{request.Content}");
                Log.Error($"Response:{text}");
                throw new InvalidResponseException("Could not get X-Patch-Unique-Id.", text);
            }

            var uid = uidVals.First();

            if (string.IsNullOrEmpty(text))
                return new LoginResult { PendingPatches = null, State = LoginState.Ok, OauthLogin = null };

            Log.Verbose("Game Patching is needed... List:\n{PatchList}", text);

            var pendingPatches = PatchListParser.Parse(text);
            return new LoginResult { PendingPatches = pendingPatches, State = LoginState.NeedsPatchGame, OauthLogin = new OauthLoginResult() };
        }
    }
}
