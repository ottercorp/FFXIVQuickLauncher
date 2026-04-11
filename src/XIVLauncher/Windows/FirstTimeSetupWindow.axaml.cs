using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using CheapLoc;
using IWshRuntimeLibrary;
using XIVLauncher.Common;
using XIVLauncher.Common.Addon;
using XIVLauncher.Common.Util;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    ///     Interaction logic for FirstTimeSetup.xaml
    /// </summary>
    public partial class FirstTimeSetup : Window
    {
        public bool WasCompleted { get; private set; } = false;

        public FirstTimeSetup()
        {
            InitializeComponent();

            this.DataContext = new FirstTimeSetupViewModel();

            var detectedPath = AppUtil.TryGamePaths();

            if (detectedPath != null) GamePathEntry.Text = detectedPath;

#if !XL_NOAUTOUPDATE
            if (EnvironmentSettings.IsDisableUpdates || AppUtil.GetBuildOrigin() != "ottercorp/FFXIVQuickLauncher")
            {
#endif
                CustomMessageBox.Show(
                    $"你正在使用一个不受支持的XLLauncher版本！\n\n有可能不安全或对账号带来危害。 请从 {App.REPO_URL}/releases 下载干净的版本并重新安装或联系我们。",
                    "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Exclamation, parentWindow: this);
#if !XL_NOAUTOUPDATE
            }
#endif
        try {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);//获取桌面文件夹路径
                CreateShortcut(desktop, "XIVLauncherCN", Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent.FullName, "XIVLauncherCN.exe"));
            }
            catch {
                CustomMessageBox.Show(
                    $"创建快捷方式失败，如需要请手动创建快捷方式到桌面。",
                    "XIVLauncherCN", MessageBoxButton.OK, MessageBoxImage.Exclamation, parentWindow: this);                
            }
        }

        public static void CreateShortcut(string directory, string shortcutName, string targetPath,
                                          string description = null, string iconLocation = null)
        {
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string shortcutPath = Path.Combine(directory, string.Format("{0}.lnk", shortcutName));
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath); //创建快捷方式对象
            shortcut.TargetPath = targetPath; //指定目标路径
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath); //设置起始位置
            shortcut.WindowStyle = 1; //设置运行方式，默认为常规窗口
            shortcut.Description = description; //设置备注
            shortcut.IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? targetPath : iconLocation; //设置图标路径
            shortcut.Save(); //保存快捷方式
        }

        public static string GetShortcutTargetFile(string path)
        {
            var shell = new WshShell();
            var shortcut = (IWshShortcut) shell.CreateShortcut(path);

            return shortcut.TargetPath;
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (SetupTabControl.SelectedIndex == 0)
            {
                if (string.IsNullOrEmpty(GamePathEntry.Text))
                {
                    CustomMessageBox.Show(Loc.Localize("GamePathEmptyError", "Please select a game path."), "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error, false, false, parentWindow: this);
                    return;
                }

                if (!GameHelpers.LetChoosePath(GamePathEntry.Text))
                {
                    CustomMessageBox.Show(Loc.Localize("GamePathSafeguardError", "Please do not select the \"game\" or \"boot\" folder of your game installation, and choose the folder that contains these instead."), "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error, parentWindow: this);
                    return;
                }

                if (!GameHelpers.IsValidGamePath(GamePathEntry.Text))
                {
                    if (CustomMessageBox.Show(Loc.Localize("GamePathInvalidConfirm", "The folder you selected has no installation of the game.\nXIVLauncher will install the game the first time you log in.\nContinue?"), "XIVLauncherCN",
                            MessageBoxButton.YesNo, MessageBoxImage.Information, parentWindow: this) != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                //if (GameHelpers.CanFfxivMightNotBeInternationalClient(GamePathEntry.Text) && App.Settings.Language != ClientLanguage.ChineseSimplified)
                //{
                //    if (CustomMessageBox.Show(Loc.Localize("GamePathRegionConfirm", "The folder you selected might be the Chinese or Korean release of the game. XIVLauncher only supports international release of the game.\nIs the folder you've selected indeed for the international version?"), "XIVLauncher",
                //        MessageBoxButton.YesNo, MessageBoxImage.Warning, parentWindow: this) != MessageBoxResult.Yes)
                //    {
                //        return;
                //    }
                //}

                if (GamePathEntry.Text.StartsWith("C"))
                {
                    if (CustomMessageBox.Show("你选择的游戏路径位于C盘。\nXIVLauncher将会无法正常登陆，请将游戏移出C盘或者使用管理员启动XIVLauncher。", "XIVLauncherCN",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, parentWindow: this) != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
            }

            if (SetupTabControl.SelectedIndex == 2)
            {
                App.Settings.GamePath = new DirectoryInfo(GamePathEntry.Text);
                App.Settings.Language = (ClientLanguage) LanguageComboBox.SelectedIndex;
                App.Settings.InGameAddonEnabled = HooksCheckBox.IsChecked == true;

                App.Settings.AddonList = new List<AddonEntry>();

                WasCompleted = true;
                Close();
            }

            SetupTabControl.SelectedIndex++;
        }
    }
}
