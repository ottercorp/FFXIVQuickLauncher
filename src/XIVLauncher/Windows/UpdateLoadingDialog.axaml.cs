using Avalonia.Controls;
using Avalonia.Input;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for OtpInputDialog.xaml
    /// </summary>
    public partial class UpdateLoadingDialog : Window
    {
        public UpdateLoadingDialog()
        {
            InitializeComponent();

            AutoLoginDisclaimer.IsVisible = App.Settings.AutologinEnabled;
            ResetUidCacheDisclaimer.IsVisible = App.Settings.UniqueIdCacheEnabled;
            if (ResetUidCacheDisclaimer.IsVisible
                && AutoLoginDisclaimer.IsVisible) {
                var card = this.FindControl<Control>("UpdateLoadingCard");
                if (card != null)
                    card.Height += 19;
            }

            this.DataContext = new UpdateLoadingDialogViewModel();
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
