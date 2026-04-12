using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CheapLoc;
using Serilog;
using XIVLauncher.Common.Game;
using XIVLauncher.Common;
using XIVLauncher.Common.Addon;
using XIVLauncher.Common.Addon.Implementations;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.Patch.Acquisition;
using XIVLauncher.Common.Util;
using XIVLauncher.Support;
using XIVLauncher.Windows.ViewModel;
using XIVLauncher.Accounts.Cred;
using XIVLauncher.Xaml;
using XIVLauncher.Xaml.Components;

namespace XIVLauncher.Windows
{
    /// <summary>
    ///     Interaction logic for SettingsControl.axaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        public event EventHandler SettingsDismissed;
        public event EventHandler CloseMainWindowGracefully;

        private SettingsControlViewModel ViewModel => DataContext as SettingsControlViewModel;

        private const int BYTES_TO_MB = 1048576;

        private bool _hasTriggeredLogo = false;

        public SettingsControl()
        {
            InitializeComponent();

            GamePathFolderEntry.PropertyChanged += GamePathFolderEntry_OnPropertyChanged;

            QqButton.Click += (_, _) =>
                Process.Start(new ProcessStartInfo("https://qun.qq.com/qqweb/qunpro/share?inviteCode=CZtWN") { UseShellExecute = true });
            FaqButton.Click += (_, _) =>
                Process.Start(new ProcessStartInfo("https://ottercorp.github.io/faq") { UseShellExecute = true });
            DataContext = new SettingsControlViewModel();
            ReloadSettings();
        }

        private Window GetOwnerWindow() => TopLevel.GetTopLevel(this) as Window;

        private void GamePathFolderEntry_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == FolderEntry.TextProperty)
                GamePathEntry_OnTextChanged();
        }

        public void ReloadSettings()
        {
            if (App.Settings.GamePath != null)
                ViewModel.GamePath = App.Settings.GamePath.FullName;

            if (App.Settings.PatchPath is { Exists: false })
            {
                App.Settings.PatchPath = null;
            }

            App.Settings.PatchPath ??= new DirectoryInfo(Path.Combine(Paths.RoamingPath, "patches"));

            if (App.Settings.PatchPath != null)
                ViewModel.PatchPath = App.Settings.PatchPath.FullName;

            LanguageComboBox.SelectedIndex = (int)App.Settings.Language.GetValueOrDefault(ClientLanguage.English);
            LauncherLanguageComboBox.SelectedIndex = (int)App.Settings.LauncherLanguage.GetValueOrDefault(LauncherLanguage.English);
            LauncherLanguageNoticeTextBlock.IsVisible = false;
            AddonListView.ItemsSource = App.Settings.AddonList ??= new List<AddonEntry>();
            AskBeforePatchingCheckBox.IsChecked = App.Settings.AskBeforePatchInstall;
            KeepPatchesCheckBox.IsChecked = App.Settings.KeepPatches;
            PatchAcquisitionComboBox.SelectedIndex = (int)App.Settings.PatchAcquisitionMethod.GetValueOrDefault(AcquisitionMethod.Aria);
            AutoStartSteamCheckBox.IsChecked = App.Settings.AutoStartSteam;

            InjectionDelayUpDown.Value = App.Settings.DalamudInjectionDelayMs;

            if (App.Settings.InGameAddonLoadMethod == DalamudLoadMethod.DllInject)
                DllInjectDalamudLoadMethodRadioButton.IsChecked = true;
            else
                EntryPointDalamudLoadMethodRadioButton.IsChecked = true;

            // Prevent raising events... (Avalonia 12: ToggleButton.Checked removed; use IsChecked property changes.)
            this.EnableHooksCheckBox.PropertyChanged -= this.EnableHooksCheckBox_OnIsCheckedPropertyChanged;
            EnableHooksCheckBox.IsChecked = App.Settings.InGameAddonEnabled;
            this.EnableHooksCheckBox.PropertyChanged += this.EnableHooksCheckBox_OnIsCheckedPropertyChanged;

            this.EnableDcTravelCheckBox.IsChecked = App.Settings.EnableDcTravel;

            OtpServerCheckBox.IsChecked = App.Settings.OtpServerEnabled;

            LaunchArgsTextBox.Text = App.Settings.AdditionalLaunchArgs;

            DpiAwarenessComboBox.SelectedIndex = (int)App.Settings.DpiAwareness.GetValueOrDefault(DpiAwareness.Unaware);

            VersionLabel.Text = "XIVLauncher" + " - v" + AppUtil.GetAssemblyVersion() + " - " + AppUtil.GetGitHash() + " - " + Environment.Version;

            var val = (decimal)App.Settings.SpeedLimitBytes / BYTES_TO_MB;

            SpeedLimiterUpDown.Value = val;

            IsFreeTrialCheckbox.IsChecked = App.Settings.IsFt;

            AccountStorageEncryptCombox.SelectedIndex = (int)App.Settings.CredType.GetValueOrDefault(CredType.WindowsCredManager);
        }

        private void AcceptButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.GamePath == ViewModel.PatchPath)
            {
                CustomMessageBox.Show(Loc.Localize("SettingsGamePatchPathError", "Game and patch download paths cannot be the same.\nPlease make sure to choose distinct game and patch download paths."), "XIVLauncher Error", MessageBoxButton.OK,
                    MessageBoxImage.Error, parentWindow: GetOwnerWindow());
                return;
            }

            App.Settings.GamePath = !string.IsNullOrEmpty(ViewModel.GamePath) ? new DirectoryInfo(ViewModel.GamePath) : null;
            App.Settings.PatchPath = !string.IsNullOrEmpty(ViewModel.PatchPath) ? new DirectoryInfo(ViewModel.PatchPath) : null;

            App.Settings.Language = (ClientLanguage)LanguageComboBox.SelectedIndex;
            // Keep the notice visible if LauncherLanguage has changed
            if (App.Settings.LauncherLanguage == (LauncherLanguage)LauncherLanguageComboBox.SelectedIndex)
                LauncherLanguageNoticeTextBlock.IsVisible = false;
            App.Settings.LauncherLanguage = (LauncherLanguage)LauncherLanguageComboBox.SelectedIndex;

            App.Settings.AddonList = (List<AddonEntry>)AddonListView.ItemsSource;
            App.Settings.AskBeforePatchInstall = AskBeforePatchingCheckBox.IsChecked == true;
            App.Settings.KeepPatches = KeepPatchesCheckBox.IsChecked == true;
            App.Settings.PatchAcquisitionMethod = (AcquisitionMethod)PatchAcquisitionComboBox.SelectedIndex;
            App.Settings.AutoStartSteam = AutoStartSteamCheckBox.IsChecked == true;

            App.Settings.InGameAddonEnabled = EnableHooksCheckBox.IsChecked == true;

            App.Settings.DalamudInjectionDelayMs = (int)InjectionDelayUpDown.Value;

            if (DllInjectDalamudLoadMethodRadioButton.IsChecked == true)
                App.Settings.InGameAddonLoadMethod = DalamudLoadMethod.DllInject;
            else
                App.Settings.InGameAddonLoadMethod = DalamudLoadMethod.EntryPoint;

            App.Settings.EnableDcTravel = EnableDcTravelCheckBox.IsChecked == true;

            App.Settings.OtpServerEnabled = OtpServerCheckBox.IsChecked == true;

            App.Settings.AdditionalLaunchArgs = LaunchArgsTextBox.Text;

            App.Settings.DpiAwareness = (DpiAwareness)DpiAwarenessComboBox.SelectedIndex;

            SettingsDismissed?.Invoke(this, EventArgs.Empty);

            App.Settings.SpeedLimitBytes = (long)(SpeedLimiterUpDown.Value * BYTES_TO_MB);

            App.Settings.IsFt = this.IsFreeTrialCheckbox.IsChecked == true;
            App.Settings.CredType = (CredType)AccountStorageEncryptCombox.SelectedIndex;
            App.AccountManager.ChangeCredType(App.Settings.CredType);

            NavigateFromSettingsToMain();
        }

        /// <summary>
        ///     Return to the main shell view (replaces Material Design WPF <c>Transitioner.MoveNextCommand</c>).
        /// </summary>
        private void NavigateFromSettingsToMain()
        {
            for (var p = this.GetLogicalParent() as Control; p != null; p = p.GetLogicalParent() as Control)
            {
                if (p is SelectingItemsControl sic && sic.ItemCount >= 2 && sic.SelectedIndex == 0)
                {
                    sic.SelectedIndex = 1;
                    return;
                }
            }
        }

        private void GitHubButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/ottercorp/FFXIVQuickLauncher") { UseShellExecute = true });
        }

        private void BackupToolButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(ViewModel.GamePath, "boot", "ffxivconfig64.exe")) { UseShellExecute = true });
        }

        private void OriginalLauncherButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var isSteam = CustomMessageBox.Builder
                                          .NewFrom(Loc.Localize("LaunchAsSteam", "Launch as a steam user?"))
                                          .WithButtons(MessageBoxButton.YesNo)
                                          .WithImage(MessageBoxImage.Question)
                                          .WithParentWindow(GetOwnerWindow())
                                          .Show() == MessageBoxResult.Yes;

            GameHelpers.StartOfficialLauncher(App.Settings.GamePath, isSteam, App.Settings.IsFt.GetValueOrDefault(false));
        }

        // All of the list handling is very dirty - but i guess it works

        private void AddAddon_OnClick(object? sender, RoutedEventArgs e)
        {
            var addonSetup = new GenericAddonSetupWindow();
            addonSetup.ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();

            if (addonSetup.Result != null && !string.IsNullOrEmpty(addonSetup.Result.Path))
            {
                var addonList = App.Settings.AddonList;

                addonList.Add(new AddonEntry
                {
                    IsEnabled = true,
                    Addon = addonSetup.Result
                });

                App.Settings.AddonList = addonList;

                AddonListView.ItemsSource = App.Settings.AddonList;
            }
        }

        private void AddonListView_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (!(AddonListView.SelectedItem is AddonEntry entry))
                return;

            if (entry.Addon is GenericAddon genericAddon)
            {
                var selectedIndex = AddonListView.SelectedIndex;
                var addonSetup = new GenericAddonSetupWindow(genericAddon);
                addonSetup.ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();

                if (addonSetup.Result != null)
                {
                    var addonList = App.Settings.AddonList;
                    addonList.RemoveAt(selectedIndex);
                    addonList.Insert(selectedIndex, new AddonEntry
                    {
                        IsEnabled = entry.IsEnabled,
                        Addon = addonSetup.Result
                    });

                    App.Settings.AddonList = addonList;

                    AddonListView.ItemsSource = App.Settings.AddonList;
                }
            }
        }

        private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { IsChecked: true })
                return;
            App.Settings.AddonList = (List<AddonEntry>)AddonListView.ItemsSource;
        }

        private void RemoveAddonEntry_OnClick(object? sender, RoutedEventArgs e)
        {
            if (AddonListView.SelectedItem is AddonEntry)
            {
                var addonList = App.Settings.AddonList;
                addonList.RemoveAt(this.AddonListView.SelectedIndex);

                App.Settings.AddonList = addonList;

                AddonListView.ItemsSource = App.Settings.AddonList;
            }
        }

        private void RunIntegrityCheck_OnClick(object? s, RoutedEventArgs e)
        {
            var window = new IntegrityCheckProgressWindow();
            var progress = new Progress<IntegrityCheck.IntegrityCheckProgress>();
            progress.ProgressChanged += (sender, checkProgress) => window.UpdateProgress(checkProgress);

            var gamePath = new DirectoryInfo(ViewModel.GamePath);

            if (Repository.Ffxiv.IsBaseVer(gamePath))
            {
                CustomMessageBox.Show(Loc.Localize("IntegrityCheckBase", "The game is not installed to the path you specified.\nPlease install the game before running an integrity check."), "XIVLauncherCN", parentWindow: GetOwnerWindow());
                return;
            }

            Task.Run(async () => await IntegrityCheck.CompareIntegrityAsync(progress, gamePath)).ContinueWith(task =>
            {
                Dispatcher.UIThread.InvokeAsync(() => window.Close()).GetAwaiter().GetResult();

                string saveIntegrityPath = Path.Combine(Paths.RoamingPath, "integrityreport.txt");
#if DEBUG
                Log.Information("Saving integrity to " + saveIntegrityPath);
#endif
                File.WriteAllText(saveIntegrityPath, task.Result.report);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    switch (task.Result.compareResult)
                    {
                        case IntegrityCheck.CompareResult.ReferenceNotFound:
                            CustomMessageBox.Show(Loc.Localize("IntegrityCheckImpossible",
                                    "There is no reference report yet for this game version. Please try again later."),
                                "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Asterisk, parentWindow: GetOwnerWindow());
                            return;

                        case IntegrityCheck.CompareResult.ReferenceFetchFailure:
                            CustomMessageBox.Show(Loc.Localize("IntegrityCheckNetworkError",
                                    "Failed to download reference files for checking integrity. Check your internet connection and try again."),
                                "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Error, parentWindow: GetOwnerWindow());
                            return;

                        case IntegrityCheck.CompareResult.Invalid:
                            CustomMessageBox.Show(Loc.Localize("IntegrityCheckFailed",
                                    "Some game files seem to be modified or corrupted. \n\nIf you use TexTools mods, this is an expected result.\n\nIf you do not use mods, right click the \"Login\" button on the XIVLauncher start page and choose \"Repair game\"."),
                                "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Exclamation, showReportLinks: true, parentWindow: GetOwnerWindow());
                            break;

                        case IntegrityCheck.CompareResult.Valid:
                            CustomMessageBox.Show(Loc.Localize("IntegrityCheckValid", "Your game install seems to be valid."), "XIVLauncherCN", MessageBoxButton.OK,
                                MessageBoxImage.Asterisk, parentWindow: GetOwnerWindow());
                            break;
                    }
                }).GetAwaiter().GetResult();
            });

            window.ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();
        }

        private void LauncherLanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (LauncherLanguageNoticeTextBlock != null)
            {
                LauncherLanguageNoticeTextBlock.IsVisible = true;
            }
        }

        private void EnableHooksCheckBox_OnIsCheckedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != CheckBox.IsCheckedProperty)
                return;
            if (e.NewValue is not true)
                return;
            EnableHooksCheckBox_OnChecked(EnableHooksCheckBox, new RoutedEventArgs());
        }

        private void EnableHooksCheckBox_OnChecked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ViewModel.GamePath) && GameHelpers.IsValidGamePath(ViewModel.GamePath) && !DalamudLauncher.CanRunDalamud(new DirectoryInfo(ViewModel.GamePath)))
                {
                    CustomMessageBox.Show(
                        Loc.Localize("DalamudIncompatible", "Dalamud was not yet updated for your current game version.\nThis is common after patches, so please be patient or ask on the Discord for a status update!"),
                        "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Asterisk, parentWindow: GetOwnerWindow());
                }
            }
            catch (Exception exc)
            {
                CustomMessageBox.Show(Loc.Localize("DalamudCompatCheckFailed",
                    "Could not contact the server to get the current compatible game version for Dalamud. This might mean that your .NET installation is too old.\nPlease check the Discord for more information."), "XIVLauncherCN Problem", MessageBoxButton.OK, MessageBoxImage.Hand, parentWindow: GetOwnerWindow());

                Log.Error(exc, "Couldn't check dalamud compatibility.");
            }
        }

        private void PluginsFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            var pluginsPath = Path.Combine(Paths.RoamingPath, "installedPlugins");

            try
            {
                Directory.CreateDirectory(pluginsPath);
                Process.Start(new ProcessStartInfo(pluginsPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                var error = $"Could not open the plugins folder! {pluginsPath}";
                CustomMessageBox.Show(error,
                    "XIVLauncher Error", MessageBoxButton.OK, MessageBoxImage.Error, parentWindow: GetOwnerWindow());
                Log.Error(ex, error);
            }
        }

        private void OpenI18nLabel_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            PlatformHelpers.OpenBrowser("https://crowdin.com/project/ffxivquicklauncher");
        }

        private void GamePathEntry_OnTextChanged()
        {
            var isBootOrGame = false;
            var mightBeNonInternationalVersion = false;

            try
            {
                isBootOrGame = !GameHelpers.LetChoosePath(ViewModel.GamePath);
                mightBeNonInternationalVersion = GameHelpers.CanMightNotBeInternationalClient(ViewModel.GamePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not check game path");
            }

            if (isBootOrGame)
            {
                GamePathSafeguardText.Text = ViewModel.GamePathSafeguardLoc;
                GamePathSafeguardText.IsVisible = true;
            }
            else if (mightBeNonInternationalVersion && App.Settings.Language != ClientLanguage.ChineseSimplified)
            {
                GamePathSafeguardText.Text = ViewModel.GamePathSafeguardRegionLoc;
                GamePathSafeguardText.IsVisible = true;
            }
            else
            {
                GamePathSafeguardText.IsVisible = false;
            }
        }

        private void LicenseText_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            Process.Start(new ProcessStartInfo(Path.Combine(Paths.ResourcesPath, "LICENSE.txt")) { UseShellExecute = true });
        }

        private void Logo_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
#if DEBUG
            var result = CustomMessageBox.Builder
                .NewFrom("Yes: FTS\nNo: Save troubleshooting\nCancel: Cancel")
                .WithCaption("XIVLauncher Expert Debugging Interface")
                .WithButtons(MessageBoxButton.YesNoCancel)
                .WithImage(MessageBoxImage.Question)
                .WithParentWindow(GetOwnerWindow())
                .Show();
            switch (result)
            {
                case MessageBoxResult.Yes:
                    var fts = new FirstTimeSetup();
                    fts.ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();

                    Log.Debug($"WasCompleted: {fts.WasCompleted}");

                    this.ReloadSettings();
                    break;
                case MessageBoxResult.No:
                    PackGenerator.PackAndShowMessage();
                    break;
                case MessageBoxResult.Cancel:
                    return;
            }
#else
            if (_hasTriggeredLogo)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select, \"{PackGenerator.SavePack()}\"",
                UseShellExecute = true
            });
            _hasTriggeredLogo = true;
#endif
        }

        private void VersionLabel_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;
            var cw = new ChangelogWindow(EnvironmentSettings.IsPreRelease);
            cw.UpdateVersion(AppUtil.GetAssemblyVersion());
            ((Avalonia.Controls.Window)cw).ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();
        }

        private void LearnMoreButton_OnClick(object? sender, RoutedEventArgs e)
        {
            PlatformHelpers.OpenBrowser("https://goatcorp.github.io/faq/mobile_otp");
        }

        private void IsFreeTrialCheckbox_OnClick(object? sender, RoutedEventArgs e)
        {
            if (App.Steam.AsyncStartTask != null)
            {
                CustomMessageBox.Show(Loc.Localize("SteamFtToggleAutoStartWarning", "To apply this setting, XIVLauncher needs to restart.\nPlease reopen XIVLauncher."),
                                      "XIVLauncherCN", image: MessageBoxImage.Information, showDiscordLink: false, showHelpLinks: false, parentWindow: GetOwnerWindow());
                App.Settings.IsFt = IsFreeTrialCheckbox.IsChecked == true;
                CloseMainWindowGracefully?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OpenAdvancedSettings_OnClick(object? sender, RoutedEventArgs e)
        {
            var asw = new AdvancedSettingsWindow();
            asw.ShowDialog(GetOwnerWindow()).GetAwaiter().GetResult();
        }
    }
}
