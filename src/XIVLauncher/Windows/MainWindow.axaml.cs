using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CheapLoc;
using System.Runtime.InteropServices;
using Material.Icons;
using Material.Icons.Avalonia;
using Serilog;
using XIVLauncher.Accounts;
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.Patch.Acquisition;
using XIVLauncher.Common.Windows;
using XIVLauncher.Game;
using XIVLauncher.Support;
using XIVLauncher.Windows.ViewModel;
using XIVLauncher.Xaml;
using Timer = System.Timers.Timer;

namespace XIVLauncher.Windows
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static KeyModifiers GetCurrentKeyModifiers()
        {
            var mods = KeyModifiers.None;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= KeyModifiers.Control;
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= KeyModifiers.Shift;
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= KeyModifiers.Alt;
            return mods;
        }

        private Timer _bannerChangeTimer;
        private Headlines _headlines;
        private IReadOnlyList<Banner> _banners;
        private Bitmap[] _bannerBitmaps;
        private int _currentBannerIndex;
        private bool _everShown = false;

        //private SdoArea[] _sdoAreas;
        public class BannerDotInfo
        {
            public bool Active { get; set; }
            public int Index { get; set; }
        }

        private ObservableCollection<BannerDotInfo> _bannerDotList;

        private Timer _maintenanceQueueTimer;

        private AccountManager _accountManager;

        public MainWindowViewModel Model => this.DataContext as MainWindowViewModel;
        private readonly Launcher _launcher;

        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = new MainWindowViewModel(this);
            _accountManager = Model.AccountManager;
            _launcher = Model.Launcher;

            Closed += Model.OnWindowClosed;
            Closing += (s, e) =>
            {
                var args = new CancelEventArgs();
                Model.OnWindowClosing(s!, args);
                if (args.Cancel)
                    e.Cancel = true;
            };

            Model.LoginCardTransitionerIndex = 1;

            Model.Activate += () => _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.Show();
                this.Activate();
                this.Focus();
            });

            Model.Hide += () => _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.Hide();
            });

            Model.ReloadHeadlines += () => Task.Run(SetupHeadlines);

            LoginTypeSelection.ItemsSource = GuiLoginType.Get(App.Settings.ShowWeGameTokenLogin.GetValueOrDefault(false));
            SetLoginTypeComboSelection(App.Settings.SelectedLoginType.GetValueOrDefault(LoginType.SdoSlide));
            NewsListView.ItemsSource = new List<News>
            {
                new News
                {
                    Title = Loc.Localize("NewsLoading", "Loading..."),
                    Tag = "DlError"
                }
            };

#if !XL_NOAUTOUPDATE
            Title += " v" + AppUtil.GetAssemblyVersion();
#else
            Title += " " + AppUtil.GetGitHash();
#endif

#if !XL_NOAUTOUPDATE
            if (EnvironmentSettings.IsDisableUpdates)
#endif
            {
                Title += " - UNSUPPORTED VERSION - NO UPDATES - COULD DO BAD THINGS";
            }

#if DEBUG
            Title += " - Debugging";
#endif

            if (EnvironmentSettings.IsWine)
                Title += " - Wine on Linux";

        }

        private async Task SetupServers()
        {
            var areas = new SdoArea[1] { new SdoArea { AreaName = "获取大区失败", Areaid = "-1" } };
            try
            {
                areas = await SdoArea.Get();
            }
            catch (Exception ex)
            {

                throw;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Model.SdoAreas = areas.ToArray();
                ServerSelection.ItemsSource = Model.SdoAreas;
                ServerSelection.SelectedIndex = 0;
            });
        }

        private async Task SetupHeadlines()
        {
            try
            {
                _bannerChangeTimer?.Stop();

                //await Headlines.GetWorlds(_launcher, App.Settings.Language.GetValueOrDefault(ClientLanguage.English));
                //_banners = await Headlines.GetBanners(_launcher, App.Settings.Language.GetValueOrDefault(ClientLanguage.English), App.Settings.ForceNorthAmerica.GetValueOrDefault(false))
                //                          .ConfigureAwait(false);
                //await Headlines.GetMessage(_launcher, App.Settings.Language.GetValueOrDefault(ClientLanguage.English), App.Settings.ForceNorthAmerica.GetValueOrDefault(false))
                //               .ConfigureAwait(false);
                _headlines = await Headlines.GetNews(_launcher, App.Settings.Language.GetValueOrDefault(ClientLanguage.ChineseSimplified), App.Settings.ForceNorthAmerica.GetValueOrDefault(false))
                                            .ConfigureAwait(false);
                _banners = this._headlines.Banner;

                _bannerBitmaps = new Bitmap[_banners.Count];
                _bannerDotList = new();

                for (var i = 0; i < _banners.Count; i++)
                {
                    var imageBytes = await _launcher.DownloadAsLauncher(_banners[i].LsbBanner.ToString(), App.Settings.Language.GetValueOrDefault(ClientLanguage.ChineseSimplified));

                    using var stream = new MemoryStream(imageBytes);

                    _bannerBitmaps[i] = new Bitmap(stream);

                    _bannerDotList.Add(new() { Index = i });
                }

                _bannerDotList[0].Active = true;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    this.BannerImage.Source = this._bannerBitmaps[0];
                    this.BannerDot.ItemsSource = this._bannerDotList;
                });

                _bannerChangeTimer = new Timer { Interval = 5000 };

                _bannerChangeTimer.Elapsed += (o, args) =>
                {
                    _bannerDotList.ToList().ForEach(x => x.Active = false);

                    if (_currentBannerIndex + 1 > _banners.Count - 1)
                        _currentBannerIndex = 0;
                    else
                        _currentBannerIndex++;

                    _bannerDotList[_currentBannerIndex].Active = true;

                    _ = Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        BannerImage.Source = _bannerBitmaps[_currentBannerIndex];
                        BannerDot.ItemsSource = _bannerDotList.ToList();
                    });
                };

                _bannerChangeTimer.AutoReset = true;
                _bannerChangeTimer.Start();

                await Dispatcher.UIThread.InvokeAsync(() => { NewsListView.ItemsSource = _headlines.News; });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not get news");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    NewsListView.ItemsSource = new List<News> { new News { Title = Loc.Localize("NewsDlFailed", "Could not download news data."), Tag = "DlError" } };
                });
            }
        }

        private const int CURRENT_VERSION_LEVEL = 2;

        private void SetDefaults()
        {
            // Set the default patch acquisition method
            App.Settings.PatchAcquisitionMethod ??=
                EnvironmentSettings.IsWine ? AcquisitionMethod.NetDownloader : AcquisitionMethod.Aria;

            // Set the default Dalamud injection method
            App.Settings.InGameAddonLoadMethod ??= EnvironmentSettings.IsWine
                ? DalamudLoadMethod.DllInject
                : DalamudLoadMethod.EntryPoint;

            // Clean up invalid addons
            if (App.Settings.AddonList != null)
                App.Settings.AddonList = App.Settings.AddonList.Where(x => !string.IsNullOrEmpty(x.Addon.Path)).ToList();


            App.Settings.AskBeforePatchInstall ??= true;

            App.Settings.DpiAwareness ??= DpiAwareness.Unaware;

            App.Settings.TreatNonZeroExitCodeAsFailure ??= false;
            App.Settings.ExitLauncherAfterGameExit ??= true;

            App.Settings.IsFt = false;
            App.Settings.UniqueIdCacheEnabled = false;
            //App.Settings.EncryptArguments = false;
            App.Settings.EnableBeta ??= false;

            App.Settings.AutoStartSteam ??= false;

            App.Settings.ForceNorthAmerica ??= false;

            var versionLevel = App.Settings.VersionUpgradeLevel.GetValueOrDefault(0);

            while (versionLevel < CURRENT_VERSION_LEVEL)
            {
                switch (versionLevel)
                {
                    case 0:
                        // Check for RTSS & Special K injectors
                        try
                        {
                            var hasRtss = Process.GetProcesses().Any(x =>
                                x.ProcessName.ToLowerInvariant().Contains("rtss") ||
                                x.ProcessName.ToLowerInvariant().Contains("skifsvc64"));

                            if (hasRtss)
                            {
                                App.Settings.DalamudInjectionDelayMs = 4000;
                                Log.Information("RTSS/SpecialK detected, setting delay");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Could not check for RTSS/SpecialK");
                        }

                        break;

                    // 5.12.2022: Bad main window placement when using auto-launch
                    case 1:
                        App.Settings.MainWindowPlacement = null;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }

                versionLevel++;
            }

            App.Settings.VersionUpgradeLevel = versionLevel;


        }

        public void Initialize()
        {
#if DEBUG
            var fakeStartMenuItem = new MenuItem
            {
                Header = "Fake start"
            };
            fakeStartMenuItem.Click += FakeStart_OnClick;

            LoginContextMenu.Items.Add(fakeStartMenuItem);
#endif

            this.SetDefaults();

            Model.IsFastLogin = App.Settings.FastLogin;
            //LoginPassword.IsEnabled = LoginPassword.IsVisible;
            //Model.EnableInjector = App.Settings.EnableInjector;

            //_accountManager = new AccountManager(App.Settings);
            //if (this._accountManager.CurrentAccount != null && !_accountManager.CurrentAccount.Password.IsNullOrEmpty()) ShowPassword_OnClick(null, null);

            var savedAccount = _accountManager.CurrentAccount;

            var modifiers = GetCurrentKeyModifiers();

            if (App.Settings.UniqueIdCacheEnabled && modifiers.HasFlag(KeyModifiers.Control))
            {
                App.UniqueIdCache.Reset();
                Console.Beep(523, 150); // Feedback without popup
            }

            if (App.GlobalIsDisableAutologin)
            {
                Log.Information("Autologin was disabled globally, saving into settings...");
                App.Settings.AutologinEnabled = false;
            }

            if (App.Settings.AutologinEnabled && savedAccount != null && !modifiers.HasFlag(KeyModifiers.Shift))
            {
                Log.Information("Engaging Autologin...");
                if (savedAccount.AccountType == XivAccountType.WeGameSid)
                {
                    Model.TryLogin(
                        LoginType.WeGameSid,
                        savedAccount.LoginAccount,
                        savedAccount.TestSID,
                        Model.IsFastLogin,
                        Model.IsReadWegameInfo,
                        MainWindowViewModel.AfterLoginAction.Start
                    );
                }
                else
                {
                    Model.TryLogin(
                        LoginType.AutoLoginSession,
                        savedAccount.LoginAccount,
                        savedAccount.AutoLoginSessionKey,
                        Model.IsFastLogin,
                        Model.IsReadWegameInfo,
                        MainWindowViewModel.AfterLoginAction.Start
                        );
                }
                return;
            }
            else if (modifiers.HasFlag(KeyModifiers.Shift) || bool.Parse(Environment.GetEnvironmentVariable("XL_NOAUTOLOGIN") ?? "false"))
            {
                App.Settings.AutologinEnabled = false;
                //AutoLoginCheckBox.IsChecked = false;
            }

            if (App.Settings.GamePath?.Exists != true)
            {
                var setup = new FirstTimeSetup();
                setup.ShowDialog(this).GetAwaiter().GetResult();

                // If the user didn't reach the end of the setup, we should quit
                if (!setup.WasCompleted)
                {
                    Environment.Exit(0);
                    return;
                }

                this.SettingsControl.ReloadSettings();
            }
            Task.Run(async () =>
            {
                await SetupServers();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (savedAccount != null)
                        SwitchAccount(savedAccount, false); ;
                });

                await SetupHeadlines();
                Troubleshooting.LogTroubleshooting(); ;
            });


            Log.Information("MainWindow initialized.");

            Show();
            Activate();

            _everShown = true;
        }

        private void BannerCard_MouseUp(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (_headlines != null) Process.Start(new ProcessStartInfo(_banners[_currentBannerIndex].Link.ToString()) { UseShellExecute = true });
        }

        private void NewsListView_OnMouseUp(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (_headlines == null)
                return;

            if (!(NewsListView.SelectedItem is News item))
                return;

            if (!string.IsNullOrEmpty(item.Url))
            {
                Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
            }
            //else
            //{
            //    string url;

            //    switch (App.Settings.Language)
            //    {
            //        case ClientLanguage.Japanese:
            //            url = "https://jp.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        case ClientLanguage.English when GameHelpers.IsRegionNorthAmerica():
            //            url = "https://na.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        case ClientLanguage.English:
            //            url = "https://eu.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        case ClientLanguage.German:
            //            url = "https://de.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        case ClientLanguage.French:
            //            url = "https://fr.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        case ClientLanguage.ChineseSimplified:
            //            url = "https://na.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;

            //        default:
            //            url = "https://eu.finalfantasyxiv.com/lodestone/news/detail/";
            //            break;
            //    }

            //    Process.Start(url + item.Id);
            //}
        }

        private void WorldStatusButton_Click(object? sender, RoutedEventArgs e)
        {
            if (App.Settings.Language == ClientLanguage.ChineseSimplified) Process.Start(new ProcessStartInfo("https://ff.web.sdo.com/web8/index.html#/servers") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("https://is.xivup.com/") { UseShellExecute = true });
        }

        private void QueueButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_maintenanceQueueTimer == null)
                SetupMaintenanceQueueTimer();

            Model.IsLoadingDialogCancelButtonVisible = true;
            Model.LoadingDialogMessage = Model.WaitingForMaintenanceLoc;
            Model.IsLoadingDialogOpen = true;

            _maintenanceQueueTimer.Start();

            // Manually fire the first event, avoid waiting the first timer interval
            Task.Run(() =>
            {
                OnMaintenanceQueueTimerEvent(null, null);
            });
        }

        private void SetupMaintenanceQueueTimer()
        {
            // This is a good indicator that we should clear the UID cache
            App.UniqueIdCache.Reset();

            _maintenanceQueueTimer = new Timer
            {
                Interval = 20000
            };

            _maintenanceQueueTimer.Elapsed += OnMaintenanceQueueTimerEvent;
        }

        private async void OnMaintenanceQueueTimerEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            var bootPatches = await _launcher.CheckBootVersion(App.Settings.GamePath);

            var gateStatus = false;

            try
            {
                //gateStatus = Task.Run(() => _launcher.GetGateStatus(App.Settings.Language.GetValueOrDefault(ClientLanguage.English))).Result.Status;
            }
            catch
            {
                // ignored
            }

            var hasBootPatch = bootPatches.Length > 0;
            if (gateStatus || hasBootPatch)
            {
                if (hasBootPatch)
                {
                    CustomMessageBox.Show(Loc.Localize("MaintenanceQueueBootPatch",
                        "A patch for the official launcher was detected.\nThis usually means that there is a patch for the game as well.\n\nYou will now be logged in."), "XIVLauncherCN", parentWindow: this);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    QuitMaintenanceQueueButton_OnClick(null, null);

                    Model.TryLogin(Model.GuiLoginType.LoginType, Model.Username, LoginPassword.Text, Model.IsFastLogin, Model.IsReadWegameInfo, MainWindowViewModel.AfterLoginAction.Start);
                });

                Console.Beep(523, 150);
                Thread.Sleep(25);
                Console.Beep(523, 150);
                Thread.Sleep(25);
                Console.Beep(523, 150);
                Thread.Sleep(25);
                Console.Beep(523, 300);
                Thread.Sleep(150);
                Console.Beep(415, 300);
                Thread.Sleep(150);
                Console.Beep(466, 300);
                Thread.Sleep(150);
                Console.Beep(523, 300);
                Thread.Sleep(25);
                Console.Beep(466, 150);
                Thread.Sleep(25);
                Console.Beep(523, 900);
            }
        }

        private void QuitMaintenanceQueueButton_OnClick(object? sender, RoutedEventArgs e)
        {
            //_maintenanceQueueTimer.Stop();
            //Model.EnableInjector = false;
            Model.IsLoadingDialogOpen = false;
        }

        private void Card_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            if (Model.IsLoggingIn)
                return;

            Model.StartLoginCommand.Execute(null);
        }

        private void AccountSwitcherButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var switcher = new AccountSwitcher(_accountManager);

            var screenPoint = AccountSwitcherButton.PointToScreen(new Point(0, 0));

            switcher.WindowStartupLocation = WindowStartupLocation.Manual;
            switcher.Position = new PixelPoint(screenPoint.X - 15, screenPoint.Y - 15);

            switcher.OnAccountSwitchedEventHandler += OnAccountSwitchedEventHandler;

            switcher.Show();
        }

        private void OnAccountSwitchedEventHandler(object sender, XivAccount e)
        {
            SwitchAccount(e, true);
        }

        private void SwitchAccount(XivAccount account, bool saveAsCurrent)
        {
            if (saveAsCurrent)
            {
                _accountManager.CurrentAccount = account;
            }

            Model.Username = account.UserName;
            //Model.IsOtp = account.UseOtp;
            //Model.IsSteam = account.UseSteamServiceAccount;
            Model.IsFastLogin = account.AutoLogin;
            Model.Area = Model.SdoAreas.Where(x => x.AreaName == account.AreaName).FirstOrDefault();
            LoginPassword.Text = string.Empty;

            switch (account.AccountType)
            {
                case XivAccountType.Sdo:
                    if (account.Password is not null)
                    {
                        SetLoginTypeComboSelection(LoginType.SdoStatic);

                        // Make users happy by not showing their password
                        LoginPassword.Text = MainWindowViewModel.PresudoPassword;
                    }
                    else
                    {
                        SetLoginTypeComboSelection(LoginType.SdoSlide);
                    }
                    break;
                case XivAccountType.WeGame:
                    SetLoginTypeComboSelection(LoginType.WeGameToken);
                    LoginPassword.Text = MainWindowViewModel.PresudoPassword;
                    break;
                case XivAccountType.WeGameSid:
                    SetLoginTypeComboSelection(LoginType.WeGameSid);
                    break;
            }
        }

        private void SettingsControl_OnSettingsDismissed(object? sender, EventArgs e)
        {
            Task.Run(SetupHeadlines);
        }

        private void FakeStart_OnClick(object? sender, RoutedEventArgs e)
        {
            _ = Model.StartGameAndAddon(new Launcher.LoginResult
            {
                OauthLogin = new Launcher.OauthLoginResult
                {
                    MaxExpansion = 5,
                    Playable = true,
                    Region = 0,
                    SessionId = "0",
                    TermsAccepted = true,
                    SndaId = "114514",
                },
                State = XIVLauncher.Common.Game.Launcher.LoginState.Ok,
                UniqueId = "0"
            }, false, false, false, false).ConfigureAwait(false);
        }

        private void LoginPassword_OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (this.DataContext != null)
                ((MainWindowViewModel)this.DataContext).Password = ((TextBox)sender!).Text;
        }

        private void SetLoginTypeComboSelection(LoginType loginType)
        {
            if (LoginTypeSelection.ItemsSource is not IEnumerable<GuiLoginType> items)
                return;
            LoginTypeSelection.SelectedItem = items.FirstOrDefault(x => x.LoginType == loginType);
        }

        private void RadioButton_MouseEnter(object? sender, PointerEventArgs e)
        {
            ((RadioButton)sender!).IsChecked = true;
            _currentBannerIndex = _bannerDotList.FirstOrDefault(x => x.Active)?.Index ?? _currentBannerIndex;
            _ = Dispatcher.UIThread.InvokeAsync(() => BannerImage.Source = _bannerBitmaps[_currentBannerIndex]);

            _bannerChangeTimer?.Stop();
        }

        private void RadioButton_MouseLeave(object? sender, PointerEventArgs e)
        {
            _bannerChangeTimer?.Start();
        }

        private void SettingsControl_OnCloseMainWindowGracefully(object? sender, EventArgs e)
        {
            Close();
        }

        private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (!_everShown)
                return;

            try
            {
                PreserveWindowPosition.SaveWindowPosition(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Couldn't save window position");
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            try
            {
                PreserveWindowPosition.RestorePosition(this);

                // Restore the size of the window to what we expect it to be
                // There's no better way to do it that doesn't make me wanna off myself
                Width = 814;
                Height = 399;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Couldn't restore window position");
            }
        }

        private void ServerSelection_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (this.DataContext != null)
                ((MainWindowViewModel)this.DataContext).Area = (SdoArea)((ComboBox)sender!).SelectedItem;
            App.Settings.SelectedServer = ((ComboBox)sender!).SelectedIndex;
        }

        private void LoginTypeSelection_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selectedItem = (GuiLoginType)((ComboBox)sender!).SelectedItem;
            if (this.DataContext != null)
                ((MainWindowViewModel)this.DataContext).GuiLoginType = selectedItem;
            App.Settings.SelectedLoginType = selectedItem.LoginType;
            // Default
            LoginUsername.IsVisible = true;
            LoginPassword.IsVisible = false;

            FastLoginCheckBox.IsVisible = true;
            ReadWeGameInfoCheckBox.IsVisible = false;
            FastLoginCheckBox.Content = "快速登录";
            LoginPassword.Text = string.Empty;
            this.LoginUsername.Watermark = "盛趣账号";
            this.LoginPassword.Watermark = "密码";

            switch (selectedItem.LoginType)
            {
                //Todo: 各种地方的Hint
                case LoginType.SdoSlide:
                    break;
                case LoginType.SdoQrCode:
                    LoginUsername.IsVisible = false;
                    break;
                case LoginType.SdoStatic:
                    LoginUsername.IsVisible = true;
                    LoginPassword.IsVisible = true;
                    FastLoginCheckBox.Content = "保存密码";
                    //FastLoginCheckBox.IsVisible = false;
                    break;
                case LoginType.WeGameToken:
                    LoginPassword.IsVisible = true;
                    this.LoginUsername.Watermark = "SndaId";
                    this.LoginPassword.Watermark = "抓包Token";
                    break;
                case LoginType.WeGameSid:
                    FastLoginCheckBox.IsVisible = false;
                    ReadWeGameInfoCheckBox.IsVisible = true;
                    this.LoginUsername.Watermark = "从Wegame自动获取的账号";
                    break;
            }
        }

        private void LoginUsername_OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (this.DataContext != null)
                ((MainWindowViewModel)this.DataContext).Username = ((TextBox)sender!).Text;
        }

        private void FastLoginCheckBox_OnClick(object? sender, RoutedEventArgs e)
        {
            //if (Model.IsFastLogin)
            //{
            //    LoginPassword.Text = String.Empty;
            //}
            //else
            //{
            //    LoginPassword.Text = _accountManager.CurrentAccount?.Password;
            //}
        }

        //private void InjectGame_OnClick(object sender, RoutedEventArgs e)
        //{
        //    Task.Run(() =>
        //    {
        //        Model.InjectGame();
        //        Environment.Exit(0);
        //    }).ConfigureAwait(false);

        //}

        private void BackToLoginPageButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Model.SwitchCard(MainWindowViewModel.LoginCard.MainPage);
        }

        private void InjectButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Model.SelectedProcess != null)
                AppUtil.BringProcessMainWindowToFront(Model.SelectedProcess.ProcessId);
        }
    }
}
