using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using XIVLauncher.Common.Http;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for OtpInputDialog.axaml
    /// </summary>
    public partial class OtpInputDialog : Window
    {
        public event Action<string> OnResult;

        private readonly IBrush _otpInputPromptDefaultBrush;

        private OtpInputDialogViewModel ViewModel => DataContext as OtpInputDialogViewModel;

        private OtpListener _otpListener;
        private bool _ignoreCurrentOtp;
        private bool? _dialogResult;

        public OtpInputDialog()
        {
            InitializeComponent();

            _otpInputPromptDefaultBrush = OtpInputPrompt.Foreground;

            this.DataContext = new OtpInputDialogViewModel();

            PointerPressed += OtpInputDialog_OnPointerPressed;
            Activated += (_, _) => OtpTextBox.Focus();
            GotFocus += (_, _) => OtpTextBox.Focus();
        }

        private void OtpInputDialog_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            if (e.Source is Button || e.Source is TextBox)
                return;
            BeginMoveDrag(e);
        }

        /// <summary>Shows the OTP dialog modally. Pass the owner window when available (Avalonia requires an owner for modal dialogs).</summary>
        public bool? ShowOtpDialog(Window owner)
        {
            _dialogResult = null;
            OtpTextBox.Focus();

            if (App.Settings.OtpServerEnabled)
            {
                _otpListener = new OtpListener("legacy-" + AppUtil.GetAssemblyVersion());
                _otpListener.OnOtpReceived += TryAcceptOtp;

                try
                {
                    Task.Run(() => _otpListener.Start());
                    Log.Debug("OTP server started...");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not start OTP HTTP listener.");
                }
            }

            if (owner != null && owner.IsVisible)
                ShowInTaskbar = false;

            ShowDialog(owner).GetAwaiter().GetResult();
            return _dialogResult;
        }

        public void Reset()
        {
            OtpInputPrompt.Text = ViewModel.OtpInputPromptLoc;
            OtpInputPrompt.Foreground = _otpInputPromptDefaultBrush;
            OtpTextBox.Text = "";
            OtpTextBox.Focus();
        }

        public void IgnoreCurrentResult(string reason)
        {
            OtpInputPrompt.Text = reason;
            OtpInputPrompt.Foreground = Brushes.Red;
            _ignoreCurrentOtp = true;
        }

        public void TryAcceptOtp(string otp)
        {
            if (otp.Length != 6)
            {
                Log.Error("Malformed OTP: {Otp}", otp);

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OtpInputPrompt.Text = ViewModel.OtpInputPromptBadLoc;
                    OtpInputPrompt.Foreground = Brushes.Red;
                    ShakeAnimationAsync();
                    OtpTextBox.Focus();
                });

                return;
            }

            _ignoreCurrentOtp = false;
            OnResult?.Invoke(otp);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_ignoreCurrentOtp)
                {
                    ShakeAnimationAsync();
                    OtpTextBox.Focus();
                }
                else
                {
                    _otpListener?.Stop();
                    _dialogResult = true;
                    Close();
                }
            });
        }

        private async void ShakeAnimationAsync()
        {
            var transform = OtpInputPrompt.RenderTransform as TranslateTransform ?? new TranslateTransform();
            OtpInputPrompt.RenderTransform = transform;
            for (var i = 0; i < 4; i++)
            {
                transform.X = 5;
                await Task.Delay(50);
                transform.X = -5;
                await Task.Delay(50);
            }

            transform.X = 0;
        }

        private void Cancel()
        {
            OnResult?.Invoke(null);
            _otpListener?.Stop();
            _dialogResult = false;
            Close();
        }

        private void OtpTextBox_OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            var regex = new Regex("[^0-9]+");
            if (regex.IsMatch(e.Text))
                e.Handled = true;
        }

        private void OtpTextBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                Cancel();
            }
            else if (e.Key == Key.Enter)
            {
                TryAcceptOtp(OtpTextBox.Text);
            }
        }

        private void OkButton_OnClick(object? sender, RoutedEventArgs e)
        {
            TryAcceptOtp(OtpTextBox.Text);
        }

        private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Cancel();
        }

        private async void PasteButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var top = TopLevel.GetTopLevel(this);
            var text = top?.Clipboard != null ? await top.Clipboard.TryGetTextAsync() : null;
            OtpTextBox.Text = text ?? "";
            TryAcceptOtp(OtpTextBox.Text);
        }

        public void OpenShortcutInfo_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://goatcorp.github.io/faq/mobile_otp") { UseShellExecute = true });
        }

        public static string AskForOtp(Action<OtpInputDialog, string> onOtpResult, Window parentWindow)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return Dispatcher.UIThread.InvokeAsync(() => AskForOtp(onOtpResult, parentWindow)).GetAwaiter().GetResult();

            var dialog = new OtpInputDialog();
            if (parentWindow.IsVisible)
            {
                dialog.ShowInTaskbar = false;
            }

            string result = null;
            dialog.OnResult += otp => onOtpResult(dialog, result = otp);
            return dialog.ShowOtpDialog(parentWindow.IsVisible ? parentWindow : null) == true ? result : null;
        }
    }
}
