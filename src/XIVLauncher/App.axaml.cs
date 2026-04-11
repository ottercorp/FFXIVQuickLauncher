using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using CheapLoc;
using CommandLine;
using Config.Net;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using Velopack;
using XIVLauncher.Accounts;
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Support;
using XIVLauncher.Common.Util;
using XIVLauncher.Common.Windows;
using XIVLauncher.PlatformAbstractions;
using XIVLauncher.Settings;
using XIVLauncher.Settings.Parsers;
using XIVLauncher.Support;
using XIVLauncher.Windows;
using XIVLauncher.Xaml;

namespace XIVLauncher
{
    public partial class App : Application
    {
        public class CmdLineOptions
        {
            [CommandLine.Option("dalamud-runner-override", Required = false, HelpText = "Path to a folder to override the dalamud runner with.")]
            public string RunnerOverride { get; set; }

            [CommandLine.Option("roamingPath", Required = false, HelpText = "Path to a folder to override the roaming path for XL with.")]
            public string RoamingPath { get; set; }

            [CommandLine.Option("noautologin", Required = false, HelpText = "Disable autologin.")]
            public bool NoAutoLogin { get; set; }

            [CommandLine.Option("gen-localizable", Required = false, HelpText = "Generate localizable files.")]
            public bool DoGenerateLocalizables { get; set; }

            [CommandLine.Option("gen-integrity", Required = false, HelpText = "Generate integrity files. Provide a game path.")]
            public string DoGenerateIntegrity { get; set; }

            [CommandLine.Option("account", Required = false, HelpText = "Account name to use.")]
            public string AccountName { get; set; }

            [CommandLine.Option("steamticket", Required = false, HelpText = "Steam ticket to use.")]
            public string SteamTicket { get; set; }

            [CommandLine.Option("clientlang", Required = false, HelpText = "Client language to use.")]
            public ClientLanguage? ClientLanguage { get; set; }

            [CommandLine.Option("squirrel-updated", Hidden = true)]
            public string SquirrelUpdated { get; set; }

            [CommandLine.Option("squirrel-install", Hidden = true)]
            public string SquirrelInstall { get; set; }

            [CommandLine.Option("squirrel-obsolete", Hidden = true)]
            public string SquirrelObsolete { get; set; }

            [CommandLine.Option("squirrel-uninstall", Hidden = true)]
            public string SquirrelUninstall { get; set; }

            [CommandLine.Option("squirrel-firstrun", Hidden = true)]
            public bool SquirrelFirstRun { get; set; }

            [CommandLine.Option("inject", Hidden = true)]
            public bool InjectMode { get; set; }
        }

        public const string REPO_URL = "https://github.com/ottercorp/FFXIVQuickLauncher";

        public static ILauncherSettingsV3 Settings;
        public static WindowsSteam Steam;
        public static CommonUniqueIdCache UniqueIdCache;
        public static AccountManager AccountManager;
#if !XL_NOAUTOUPDATE
        private UpdateLoadingDialog _updateWindow;
#endif

        public static CmdLineOptions CommandLine { get; private set; }

        private FileInfo _dalamudRunnerOverride = null;
        private MainWindow _mainWindow;

        public static bool GlobalIsDisableAutologin { get; private set; }
        public static bool InjectMode { get; private set; }
        public static byte[] GlobalSteamTicket { get; private set; }
        public static DalamudUpdater DalamudUpdater { get; private set; }

        public static IBrush UaBrush = new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xFF, 0x00, 0x57, 0xB7), 0.5),
                new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xd7, 0x00), 0.5),
            },
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.7, 0.7, RelativeUnit.Relative),
        };

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                OnStartup(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static void OnSerilogLogLine(object sender, (string Line, LogEventLevel Level, DateTimeOffset TimeStamp, Exception Exception) e)
        {
            if (e.Exception == null)
                return;

            Troubleshooting.LogException(e.Exception, e.Line);
        }

        private void SetupSettings()
        {
            Settings = new ConfigurationBuilder<ILauncherSettingsV3>()
                       .UseCommandLineArgs()
                       .UseJsonFile(GetConfigPath("launcher"))
                       .UseTypeParser(new DirectoryInfoParser())
                       .UseTypeParser(new AddonListParser())
                       .UseTypeParser(new CommonJsonParser<PreserveWindowPosition.WindowPlacement>())
                       .Build();

            if (Settings.EnableVerboseLog.GetValueOrDefault(false)) {
                LogInit.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
            }
            Settings.EnableVerboseLog = false;
            Log.Information($"Current log level is {LogInit.LevelSwitch.MinimumLevel}");

            if (string.IsNullOrEmpty(Settings.AcceptLanguage))
            {
                Settings.AcceptLanguage = ApiHelpers.GenerateAcceptLanguage();
            }

            UniqueIdCache = new CommonUniqueIdCache(new FileInfo(Path.Combine(Paths.RoamingPath, "uidCache.json")));

            try
            {
                if (!string.IsNullOrEmpty(CommandLine.AccountName))
                {
                    App.Settings.CurrentAccountId = CommandLine.AccountName;
                    Log.Verbose("Account override: '{0}'", CommandLine.AccountName);
                }

                if (!string.IsNullOrEmpty(CommandLine.SteamTicket))
                {
                    GlobalSteamTicket = Convert.FromBase64String(CommandLine.SteamTicket);
                }

                if (CommandLine.ClientLanguage != null)
                {
                    App.Settings.Language = CommandLine.ClientLanguage;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not apply settings overrides from command line");
            }
        }

        private void OnUpdateCheckFinished(bool finishUp)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _useFullExceptionHandler = true;

#if !XL_NOAUTOUPDATE
                if (_updateWindow != null)
                    _updateWindow.Hide();
#endif

                if (!finishUp)
                    return;

                _mainWindow = new MainWindow();
                _mainWindow.Initialize();

                try
                {
                    DalamudUpdater = new DalamudUpdater(new DirectoryInfo(Path.Combine(Paths.RoamingPath, "addon")),
                        new DirectoryInfo(Path.Combine(Paths.RoamingPath, "runtime")),
                        new DirectoryInfo(Path.Combine(Paths.RoamingPath, "dalamudAssets")),
                        new DirectoryInfo(Paths.RoamingPath),
                        UniqueIdCache,
                        Settings.DalamudRolloutBucket);

                    if (this._dalamudRunnerOverride != null)
                    {
                        DalamudUpdater.RunnerOverride = this._dalamudRunnerOverride;
                    }

                    Settings.DalamudRolloutBucket = DalamudUpdater.RolloutBucket;

                    DalamudUpdater.Overlay = new DalamudLoadingOverlay();
                    ((Window)DalamudUpdater.Overlay).Hide();

                    DalamudUpdater.Run(Updates.HaveFeatureFlag(Updates.LeaseFeatureFlags.ForceProxyDalamudAndAssets));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not start dalamud updater");
                }
                if (InjectMode)
                {
                    _mainWindow.Model.InjectModeSwitchCommand.Execute(null);
                }
            });
        }

        private static void GenerateIntegrity(string path)
        {
            var result = IntegrityCheck.RunIntegrityCheckAsync(new DirectoryInfo(path), null).GetAwaiter().GetResult();
            string saveIntegrityPath = Path.Combine(Paths.RoamingPath, $"{result.GameVersion}.json");

            File.WriteAllText(saveIntegrityPath, JsonConvert.SerializeObject(result));

            ShowSimpleMessage($"Successfully hashed {result.Hashes.Count} files to {path}.", "Hello Franz");
            Environment.Exit(0);
        }

        private static void GenerateLocalizables()
        {
            try
            {
                Loc.ExportLocalizable();
            }
            catch (Exception ex)
            {
                ShowSimpleMessage(ex.ToString());
            }

            Environment.Exit(0);
        }

        private static void ShowSimpleMessage(string message, string title = "XIVLauncherCN")
        {
            var msgWindow = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                }
            };
            msgWindow.ShowDialog(null);
        }

        private bool _useFullExceptionHandler = false;

        private void TaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            if (!e.Observed)
                EarlyInitExceptionHandler(sender, new UnhandledExceptionEventArgs(e.Exception, true));
        }

        private void EarlyInitExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Log.Error((Exception)e.ExceptionObject, "Unhandled exception");

                if (_useFullExceptionHandler)
                {
                    CustomMessageBox.Builder
                                    .NewFrom((Exception)e.ExceptionObject, "Unhandled", CustomMessageBox.ExitOnCloseModes.ExitOnClose)
                                    .WithAppendText("\n\nError during early initialization. Please report this error.\n\n" + e.ExceptionObject)
                                    .Show();
                }
                else
                {
                    ShowSimpleMessage(
                        "Error during early initialization. Please report this error.\n\n" + e.ExceptionObject,
                        "XIVLauncher Error");
                }

                Environment.Exit(-1);
            });
        }

        private static string GetConfigPath(string prefix) => Path.Combine(Paths.RoamingPath, $"{prefix}ConfigV3.json");

        private void OnStartup(IClassicDesktopStyleApplicationLifetime desktop)
        {
#if !DEBUG
            try
            {
                AppDomain.CurrentDomain.UnhandledException += EarlyInitExceptionHandler;
                TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
            }
            catch
            {
                // ignored
            }
#endif

            try
            {
                LogInit.Setup(
                    Path.Combine(Paths.RoamingPath, "output.log"),
                    Environment.GetCommandLineArgs());

                Log.Information("========================================================");
                Log.Information("Starting a session(v{Version} - {Hash})", AppUtil.GetAssemblyVersion(), AppUtil.GetGitHash());

                SerilogEventSink.Instance.LogLine += OnSerilogLogLine;
            }
            catch (Exception ex)
            {
                ShowSimpleMessage("Could not set up logging. Please report this error.\n\n" + ex.Message, "XIVLauncherCN");
            }

            try
            {
                var helpWriter = new StringWriter();
                var parser = new Parser(config =>
                {
                    config.HelpWriter = helpWriter;
                    config.IgnoreUnknownArguments = true;
                });
                var result = parser.ParseArguments<CmdLineOptions>(Environment.GetCommandLineArgs());

                if (result.Errors.Any())
                {
                    ShowSimpleMessage(helpWriter.ToString(), "Help");
                }

                CommandLine = result.Value ?? new CmdLineOptions();

                if (!string.IsNullOrEmpty(CommandLine.RoamingPath))
                {
                    Paths.OverrideRoamingPath(CommandLine.RoamingPath);
                }

                if (!string.IsNullOrEmpty(CommandLine.RunnerOverride))
                {
                    this._dalamudRunnerOverride = new FileInfo(CommandLine.RunnerOverride);
                }

                if (CommandLine.NoAutoLogin)
                {
                    GlobalIsDisableAutologin = true;
                }

                if (!string.IsNullOrEmpty(CommandLine.DoGenerateIntegrity))
                {
                    GenerateIntegrity(CommandLine.DoGenerateIntegrity);
                }

                if (CommandLine.DoGenerateLocalizables)
                {
                    GenerateLocalizables();
                }
                if (CommandLine.InjectMode)
                {
                    InjectMode = true;
                }
            }
            catch (Exception ex)
            {
                ShowSimpleMessage("Could not parse command line arguments. Please report this error.\n\n" + ex.Message, "XIVLauncherCN");
            }

            try
            {
                SetupSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Settings were corrupted, resetting");
                File.Delete(GetConfigPath("launcher"));
                SetupSettings();
            }
            AccountManager = new AccountManager(Settings);
#if !XL_LOC_FORCEFALLBACKS
            try
            {
                if (App.Settings.LauncherLanguage == null)
                {
                    var currentUiLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    App.Settings.LauncherLanguage = App.Settings.LauncherLanguage.GetLangFromTwoLetterIso(currentUiLang);
                }

                Log.Information("Trying to set up Loc for language code {0}", App.Settings.LauncherLanguage.GetLocalizationCode());

                if (!App.Settings.LauncherLanguage.IsDefault())
                {
                    Loc.Setup(AppUtil.GetFromResources($"XIVLauncher.Resources.Loc.xl.xl_{App.Settings.LauncherLanguage.GetLocalizationCode()}.json"));
                }
                else
                {
                    Loc.SetupWithFallbacks();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not get language information. Setting up fallbacks.");
                Loc.Setup("{}");
            }
#else
            Loc.Setup("{}");
#endif

            try
            {
                Steam = new WindowsSteam();

                if (Settings.AutoStartSteam.GetValueOrDefault(false))
                    Steam.KickoffAsyncStartup(Settings.IsFt.GetValueOrDefault(false) ? Constants.STEAM_FT_APP_ID : Constants.STEAM_APP_ID);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not set up Steam");
            }

            VelopackApp.Build().Run();
#if !XL_NOAUTOUPDATE
            if (!EnvironmentSettings.IsDisableUpdates)
            {
                try
                {
                    Log.Information("Starting update check...");

                    _updateWindow = new UpdateLoadingDialog();
                    _updateWindow.Show();

                    var updateMgr = new Updates();
                    updateMgr.OnUpdateCheckFinished += OnUpdateCheckFinished;

                    ChangelogWindow changelogWindow = null;

                    try
                    {
                        changelogWindow = new ChangelogWindow(EnvironmentSettings.IsPreRelease);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Could not load changelog window");
                    }

                    Task.Run(() => updateMgr.Run(EnvironmentSettings.IsPreRelease, changelogWindow));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not dispatch update check");

                    if (ex is HttpRequestException httpRequestException && httpRequestException.StatusCode.HasValue && (int)httpRequestException.StatusCode is 403 or 444 or 522)
                    {
                        ShowSimpleMessage(
                            "错误: " + $"服务器返回了错误代码 {httpRequestException.StatusCode}.\n你的IP可能被WAF封禁, 请前往频道进行上报." + Environment.NewLine +
                            "XIVLauncher could not check for updates. Please check your internet connection or try again.\n\n" + ex,
                            "XIVLauncher Error");
                    }
                    else
                    {
                        ShowSimpleMessage("错误: " + ex.Message + Environment.NewLine +
                                        "XIVLauncher could not check for updates. Please check your internet connection or try again.\n\n" + ex,
                                        "XIVLauncher Error");
                    }

                    Environment.Exit(0);
                    return;
                }
            }
#endif

            if (EnvironmentSettings.IsDisableUpdates)
            {
                OnUpdateCheckFinished(true);
                return;
            }

#if XL_NOAUTOUPDATE
            OnUpdateCheckFinished(true);
#endif
        }
    }
}
