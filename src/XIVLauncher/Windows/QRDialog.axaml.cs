using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for QRDialog.axaml
    /// </summary>
    public partial class QRDialog : Window
    {
        public event Action OnCancel;

        private static QRDialog dialog;

        private readonly string qrPath = Path.Combine(Environment.CurrentDirectory, "Resources", "QR.png");

        public QRDialog()
        {
            InitializeComponent();

            this.DataContext = new QRDialogViewModel();

            PointerPressed += QRDialog_OnPointerPressed;
            Activated += (_, _) => QRImage.Focus();
            GotFocus += (_, _) => QRImage.Focus();
        }

        private void QRDialog_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            if (e.Source is Button)
                return;
            BeginMoveDrag(e);
        }

        public void PrepareQrImage()
        {
            QRImage.Focus();
            if (File.Exists(qrPath))
            {
                using var stream = File.OpenRead(qrPath);
                QRImage.Source = new Bitmap(stream);
            }
            else
            {
                QRImage.Source = null;
            }
        }

        public void Reset()
        {
            QRImage.Source = null;
            QRImage.Focus();
        }

        public void IgnoreCurrentResult(string reason)
        {
        }

        public void Cancel()
        {
            Reset();
            Hide();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void OkButton_OnClick(object? sender, RoutedEventArgs e)
        {
            OnCancel?.Invoke();
            Cancel();
        }

        public void OpenQRShortcutInfo_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.daoyu8.com/") { UseShellExecute = true });
        }

        public static void CloseQRWindow(Window parentWindow)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.InvokeAsync(() => CloseQRWindow(parentWindow)).GetAwaiter().GetResult();
                return;
            }

            if (dialog == null)
            {
                return;
            }

            dialog.Hide();
        }

        public static void OpenQRWindow(Window parentWindow, Action onCancel)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.InvokeAsync(() => OpenQRWindow(parentWindow, onCancel)).GetAwaiter().GetResult();
                return;
            }

            if (dialog == null)
            {
                dialog = new QRDialog();
            }

            if (parentWindow.IsVisible)
            {
                dialog.ShowInTaskbar = false;
            }

            dialog.OnCancel += onCancel;
            dialog.PrepareQrImage();
            dialog.ShowDialog(parentWindow.IsVisible ? parentWindow : null).GetAwaiter().GetResult();
        }
    }
}
