using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Serilog;
using XIVLauncher.Common.Http;
using XIVLauncher.Windows.ViewModel;
using System.Windows.Media.Imaging;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for OtpInputDialog.xaml
    /// </summary>
    public partial class QRDialog : Window
    {
        public event Action<string> OnResult;

        private static QRDialog dialog;

        private OtpInputDialogViewModel ViewModel => DataContext as OtpInputDialogViewModel;

        //private OtpListener _otpListener;
        //private bool _ignoreCurrentOtp;

        private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");

        public QRDialog()
        {
            InitializeComponent();

            //_otpInputPromptDefaultBrush = OtpInputPrompt.Foreground;

            this.DataContext = new OtpInputDialogViewModel();

            MouseMove += OtpInputDialog_OnMouseMove;
            Activated += (_, _) => QRImage.Focus();
            GotFocus += (_, _) => QRImage.Focus();
        }

        public new bool? ShowDialog()
        {
            QRImage.Focus();
            if (File.Exists(qrPath))
            {
                QRImage.Source = new BitmapImage(new Uri(qrPath));
            }

            return base.ShowDialog();
        }

        public void Reset()
        {
            // OtpInputPrompt.Text = ViewModel.OtpInputPromptLoc;
            // OtpInputPrompt.Foreground = _otpInputPromptDefaultBrush;
            // QRImage.Text = "";
            QRImage.Source = null;
            QRImage.Focus();
        }

        public void IgnoreCurrentResult(string reason)
        {
            // OtpInputPrompt.Text = reason;
            // OtpInputPrompt.Foreground = Brushes.Red;
            //_ignoreCurrentOtp = true;
        }

        private void Cancel()
        {
            OnResult?.Invoke(null);
            //_otpListener?.Stop();
            DialogResult = false;
            Hide();
        }

        private void OtpInputDialog_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void OtpTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void OtpTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        private void OtpTextBox_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
            }
            else if (e.Key == Key.Enter)
            {
                //TryAcceptOtp(this.QRImage.Text);
                Cancel();
            }
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            //TryAcceptOtp(this.QRImage.Text);
            Cancel();
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Cancel();
        }

        public void OpenQRShortcutInfo_MouseUp(object sender, RoutedEventArgs e)
        {
            Process.Start($"https://www.daoyu8.com/");
        }

        public static void AskForQR(Window parentWindow)
        {
            if (Dispatcher.CurrentDispatcher != parentWindow.Dispatcher)
            {
                parentWindow.Dispatcher.Invoke(() => AskForQR(parentWindow));
                return;
            }
            if (dialog == null)
            {
                dialog = new QRDialog();
            }
            if (parentWindow.IsVisible)
            {
                dialog.Owner = parentWindow;
                dialog.ShowInTaskbar = false;
            }

            dialog.ShowDialog();
        }
    }
}