using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Windows;
using XIVLauncher.Common.Support;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    ///     Interaction logic for FirstTimeSetup.xaml
    /// </summary>
    public partial class AdvancedSettingsWindow : Window
    {
        public bool WasCompleted { get; private set; } = false;

        public AdvancedSettingsWindow()
        {
            InitializeComponent();

            this.DataContext = new AdvancedSettingsViewModel();
            Load();
        }

        private void Load()
        {
            UidCacheCheckBox.IsChecked = App.Settings.UniqueIdCacheEnabled;
            ExitLauncherAfterGameExitCheckbox.IsChecked = App.Settings.ExitLauncherAfterGameExit ?? true;
            TreatNonZeroExitCodeAsFailureCheckbox.IsChecked = App.Settings.TreatNonZeroExitCodeAsFailure ?? false;
            ForceNorthAmericaCheckbox.IsChecked = App.Settings.ForceNorthAmerica ?? false;
            EnableBeta.IsChecked = App.Settings.EnableBeta ?? false;
            EnableVerboseLog.IsChecked = LogInit.LevelSwitch.MinimumLevel == LogEventLevel.Verbose;
        }

        private void Save()
        {
            App.Settings.UniqueIdCacheEnabled = UidCacheCheckBox.IsChecked == true;
            App.Settings.ExitLauncherAfterGameExit = ExitLauncherAfterGameExitCheckbox.IsChecked == true;
            App.Settings.TreatNonZeroExitCodeAsFailure = TreatNonZeroExitCodeAsFailureCheckbox.IsChecked == true;
            App.Settings.ForceNorthAmerica = ForceNorthAmericaCheckbox.IsChecked == true;
            App.Settings.EnableBeta = EnableBeta.IsChecked == true;
            App.Settings.EnableVerboseLog = EnableVerboseLog.IsChecked == true;
            if (EnableVerboseLog.IsChecked == true)
            {
                LogInit.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
            }
            else
            {
                LogInit.LevelSwitch.MinimumLevel = LogInit.GetDefaultLevel();
            }
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Save();
            Close();
        }

        private void ResetCacheButton_OnClick(object sender, RoutedEventArgs e)
        {
            App.UniqueIdCache.Reset();
        }
    }
}
