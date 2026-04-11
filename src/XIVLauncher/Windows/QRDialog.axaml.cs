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
using System.ComponentModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for OtpInputDialog.xaml
    /// </summary>
    public partial class QRDialog : Window
    {
        //public event Action<string> OnResult;
        public event Action OnCancel;
        private static QRDialog dialog;

        //private OtpInputDialogViewModel ViewModel => DataContext as OtpInputDialogViewModel;

        //private OtpListener _otpListener;
        //private bool _ignoreCurrentOtp;
        private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");
        public static BitmapImage BitmapFromUri(Uri source)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = source;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }

        public static BitmapImage BitmapFromStream(Stream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }

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
                using (var ms = new FileStream(qrPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    QRImage.Source = BitmapFromStream(ms);
                } ;
            }
            else
            {
                QRImage.Source = null;
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
        public void Cancel()
        {
            //OnResult?.Invoke(null);
            //_otpListener?.Stop();
            DialogResult = false;
            Reset();
            Close();          
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
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
            dialog.OnCancel?.Invoke();
            Cancel();
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            Cancel();
        }

        public void OpenQRShortcutInfo_MouseUp(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo($"https://www.daoyu8.com/") { UseShellExecute = true });
        }

        public static void CloseQRWindow(Window parentWindow) {
            if (Dispatcher.CurrentDispatcher != parentWindow.Dispatcher)
            {
                parentWindow.Dispatcher.Invoke(() => CloseQRWindow(parentWindow));
                return;
            }
            if (dialog == null)
            {
                return;
            }
            dialog.Hide();
        }

        public static void OpenQRWindow(Window parentWindow,Action onCancel)
        {
            if (Dispatcher.CurrentDispatcher != parentWindow.Dispatcher)
            {
                parentWindow.Dispatcher.Invoke(() => OpenQRWindow(parentWindow,onCancel));
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
            dialog.OnCancel += onCancel;
            dialog.ShowDialog();
        }
    }
}
