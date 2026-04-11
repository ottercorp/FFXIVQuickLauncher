using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace XIVLauncher.Xaml.Components
{
    public partial class FolderEntry : UserControl
    {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<FolderEntry, string>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<string> DescriptionProperty =
            AvaloniaProperty.Register<FolderEntry, string>(nameof(Description));

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public FolderEntry()
        {
            InitializeComponent();
        }

        private async void BrowseFolder(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Description,
                AllowMultiple = false,
            });

            if (folders.Any())
            {
                Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
            }
        }
    }
}
