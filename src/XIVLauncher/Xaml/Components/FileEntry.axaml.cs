using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace XIVLauncher.Xaml.Components
{
    public partial class FileEntry : UserControl
    {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<FileEntry, string>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

        public static readonly StyledProperty<string> DescriptionProperty =
            AvaloniaProperty.Register<FileEntry, string>(nameof(Description));

        public static readonly StyledProperty<string> FiltersProperty =
            AvaloniaProperty.Register<FileEntry, string>(nameof(Filters));

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

        public string Filters
        {
            get => GetValue(FiltersProperty);
            set => SetValue(FiltersProperty, value);
        }

        public FileEntry()
        {
            InitializeComponent();
        }

        private async void BrowseFile(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var fileTypeFilters = new List<FilePickerFileType>();
            if (!string.IsNullOrEmpty(Filters))
            {
                var filterSets = Filters.Split(';');
                foreach (var filterSet in filterSets)
                {
                    var filterOptions = filterSet.Split(',');
                    if (filterOptions.Length >= 2)
                    {
                        fileTypeFilters.Add(new FilePickerFileType(filterOptions[0])
                        {
                            Patterns = filterOptions[1].Split(',').Select(p => p.Trim()).ToList()
                        });
                    }
                }
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Description,
                AllowMultiple = false,
                FileTypeFilter = fileTypeFilters.Count > 0 ? fileTypeFilters : null,
            });

            if (files.Any())
            {
                Text = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
            }
        }
    }
}
