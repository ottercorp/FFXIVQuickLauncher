using Avalonia.Controls;
using Avalonia.Input;
using XIVLauncher.Common.Game;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    ///     Interaction logic for FirstTimeSetup.xaml
    /// </summary>
    public partial class IntegrityCheckProgressWindow : Window
    {
        public IntegrityCheckProgressWindow()
        {
            InitializeComponent();

            this.DataContext = new IntegrityCheckProgressWindowViewModel();
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        public void UpdateProgress(IntegrityCheck.IntegrityCheckProgress progress)
        {
            InfoTextBlock.Text = $"{progress.CurrentFile}";
        }
    }
}
