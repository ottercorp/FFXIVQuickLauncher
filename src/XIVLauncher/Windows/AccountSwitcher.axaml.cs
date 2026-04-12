using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using IWshRuntimeLibrary;
using XIVLauncher.Accounts;
using XIVLauncher.Common;
using XIVLauncher.Windows.ViewModel;

namespace XIVLauncher.Windows
{
    /// <summary>
    /// Interaction logic for AccountSwitcher.axaml
    /// </summary>
    public partial class AccountSwitcher : Window
    {
        private const string AccountSwitcherDragIndexFormat = "xivlauncher/account-switcher-index";

        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

        private readonly AccountManager _accountManager;

        private Avalonia.Point? _dragStart;
        private ListBoxItem? _draggedItem;
        private bool _isDragging;
        private bool _listReorderDragStarted;

        public EventHandler<XivAccount>? OnAccountSwitchedEventHandler;

        public AccountSwitcher(AccountManager accountManager)
        {
            InitializeComponent();

            DataContext = new AccountSwitcherViewModel();

            _accountManager = accountManager;

            AccountListView.PointerPressed += AccountListView_OnPointerPressed;
            AccountListView.PointerMoved += AccountListView_OnPointerMoved;

            RefreshEntries();
        }

        private void RefreshEntries()
        {
            var accountEntries = new List<AccountSwitcherEntry>();

            foreach (var accountManagerAccount in _accountManager.Accounts)
            {
                var entry = new AccountSwitcherEntry
                {
                    Account = accountManagerAccount
                };

                Task.Run(() =>
                {
                    if (string.IsNullOrEmpty(accountManagerAccount.ThumbnailUrl))
                        accountManagerAccount.ThumbnailUrl = accountManagerAccount.FindCharacterThumb();

                    entry.UpdateProfileImage();
                });

                accountEntries.Add(entry);
            }

            _accountManager.Save();

            AccountListView.ItemsSource = accountEntries;
        }

        private bool _closing;

        private void AccountListView_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (_listReorderDragStarted)
            {
                _listReorderDragStarted = false;
                return;
            }

            var selectedEntry = AccountListView.SelectedItem as AccountSwitcherEntry;

            if (selectedEntry?.Account == null)
                return;

            OnAccountSwitchedEventHandler?.Invoke(this, selectedEntry.Account);

            _closing = true;
            Close();
        }

        private void AccountListViewContext_Opened(object? sender, RoutedEventArgs e)
        {
            var selectedEntry = AccountListView.SelectedItem as AccountSwitcherEntry;
            if (selectedEntry == null)
                return;

            AccountEntrySavePasswordCheck.IsChecked = !selectedEntry.Account.AutoLogin;
        }

        private void AccountSwitcher_OnDeactivated(object? sender, EventArgs e)
        {
            if (!_closing)
                Close();
        }

        private static System.Drawing.Bitmap AvaloniaBitmapToDrawingBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
        {
            using var outStream = new MemoryStream();
            bitmap.Save(outStream);
            outStream.Position = 0;
            return new System.Drawing.Bitmap(outStream);
        }

        // https://stackoverflow.com/questions/11434673/bitmap-save-to-save-an-icon-actually-saves-a-png
        private static void SaveAsIcon(System.Drawing.Bitmap sourceBitmap, string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Create);
            // ICO header
            fs.WriteByte(0); fs.WriteByte(0);
            fs.WriteByte(1); fs.WriteByte(0);
            fs.WriteByte(1); fs.WriteByte(0);

            // Image size
            fs.WriteByte((byte)sourceBitmap.Width);
            fs.WriteByte((byte)sourceBitmap.Height);
            // Palette
            fs.WriteByte(0);
            // Reserved
            fs.WriteByte(0);
            // Number of color planes
            fs.WriteByte(0); fs.WriteByte(0);
            // Bits per pixel
            fs.WriteByte(32); fs.WriteByte(0);

            // Data size, will be written after the data
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);

            // Offset to image data, fixed at 22
            fs.WriteByte(22);
            fs.WriteByte(0);
            fs.WriteByte(0);
            fs.WriteByte(0);

            // Writing actual data
            sourceBitmap.Save(fs, ImageFormat.Png);

            // Getting data length (file length minus header)
            var len = fs.Length - 22;

            // Write it in the correct place
            fs.Seek(14, SeekOrigin.Begin);
            fs.WriteByte((byte)len);
            fs.WriteByte((byte)(len >> 8));
        }

        private void CreateDesktopShortcut_OnClick(object? sender, RoutedEventArgs e)
        {
            if (AccountListView.SelectedItem is not AccountSwitcherEntry selectedEntry)
                return;

            var thumbnailPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;

            if (thumbnailPath == null)
                return;

            if (!string.IsNullOrEmpty(selectedEntry.Account.ThumbnailUrl) && selectedEntry.ProfileImage is Avalonia.Media.Imaging.Bitmap avaBitmap)
            {
                var thumbnailDirectory = Path.Combine(Paths.RoamingPath, "profileIcons");
                Directory.CreateDirectory(thumbnailDirectory);

                thumbnailPath = Path.Combine(thumbnailDirectory, $"{selectedEntry.Account.Id}.ico");

                SaveAsIcon(AvaloniaBitmapToDrawingBitmap(avaBitmap), thumbnailPath);
            }

            var shDesktop = (object)"Desktop";

            var shell = new WshShell();
            var shortcutAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + $@"\XIVLauncherCN - {selectedEntry.Account.UserName}.lnk";
            var shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);
            shortcut.Description = $"Open XIVLauncher with the \"{selectedEntry.Account.UserName}\" Sdo account.";
            shortcut.TargetPath = Path.Combine(new DirectoryInfo(Environment.CurrentDirectory).Parent!.FullName, "XIVLauncherCN.exe");
            shortcut.Arguments = $"--account={selectedEntry.Account.Id}";
            shortcut.WorkingDirectory = Environment.CurrentDirectory;
            shortcut.IconLocation = thumbnailPath;
            shortcut.Save();
        }

        private void RemoveAccount_OnClick(object? sender, RoutedEventArgs e)
        {
            if (AccountListView.SelectedItem is not AccountSwitcherEntry selectedEntry)
                return;

            _accountManager.RemoveAccount(selectedEntry.Account);

            RefreshEntries();
        }

        private async void SetProfilePicture_OnClick(object? sender, RoutedEventArgs e)
        {
            if (AccountListView.SelectedItem is not AccountSwitcherEntry selectedEntry)
                return;

            var inputDialog = new ProfilePictureInputWindow(selectedEntry.Account);
            await inputDialog.ShowDialog(this);

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            account.ChosenCharacterName = inputDialog.ResultName;
            account.ChosenCharacterWorld = inputDialog.ResultWorld;
            _accountManager.Save();

            RefreshEntries();
        }

        private void DontSavePassword_OnClick(object? sender, RoutedEventArgs e)
        {
            if (AccountListView.SelectedItem is not AccountSwitcherEntry selectedEntry)
                return;

            var account = _accountManager.Accounts.First(a => a.Id == selectedEntry.Account.Id);
            if (AccountEntrySavePasswordCheck.IsChecked == true)
            {
                account.AutoLogin = false;
                account.Password = string.Empty;
            }
            else
            {
                account.AutoLogin = true;
            }

            _accountManager.Save();
        }

        private void AccountListView_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(AccountListView).Properties.IsLeftButtonPressed)
                return;

            _listReorderDragStarted = false;
            _dragStart = e.GetPosition(null);
            var src = e.Source as Control;
            _draggedItem = e.Source as ListBoxItem ?? src?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();

            if (_draggedItem == null)
                return;

            _draggedItem.IsSelected = true;
        }

        private async void AccountListView_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_dragStart == null || _draggedItem == null || _isDragging)
                return;

            if (!e.GetCurrentPoint(AccountListView).Properties.IsLeftButtonPressed)
                return;

            var mousePos = e.GetPosition(null);
            var diff = _dragStart.Value - mousePos;

            if (Math.Abs(diff.X) <= 4 && Math.Abs(diff.Y) <= 4)
                return;

            var draggedIndex = AccountListView.ItemsSource is System.Collections.IList list
                ? list.IndexOf(_draggedItem.DataContext)
                : -1;
            if (draggedIndex < 0)
                return;

            _listReorderDragStarted = true;
            _isDragging = true;
            try
            {
                var dragData = new DataObject();
                dragData.Set(AccountSwitcherDragIndexFormat,
                    draggedIndex.ToString(InvariantCulture));

                await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
                _dragStart = null;
                _draggedItem = null;
            }
        }

        private void AccountListView_OnDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(AccountSwitcherDragIndexFormat))
                e.DragEffects = DragDropEffects.Move;
            else
                e.DragEffects = DragDropEffects.None;
        }

        private void AccountListView_OnDrop(object? sender, DragEventArgs e)
        {
            var indexObj = e.Data.Get(AccountSwitcherDragIndexFormat);
            if (indexObj is not string indexStr ||
                !int.TryParse(indexStr, NumberStyles.Integer, InvariantCulture, out var draggedIndex))
                return;

            var src = e.Source as Control;
            var targetItem = e.Source as ListBoxItem ?? src?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();

            if (targetItem == null)
                return;

            var targetIndex = AccountListView.ItemsSource is System.Collections.IList list
                ? list.IndexOf(targetItem.DataContext)
                : -1;

            if (targetIndex < 0 || draggedIndex < 0)
                return;

            if (AccountListView.ItemsSource is not List<AccountSwitcherEntry> accountEntries)
                return;

            var draggedEntry = accountEntries[draggedIndex];
            accountEntries.RemoveAt(draggedIndex);
            accountEntries.Insert(targetIndex, draggedEntry);

            _accountManager.Accounts.Clear();
            foreach (var accountEntry in accountEntries)
                _accountManager.Accounts.Add(accountEntry.Account);

            _accountManager.Save();
            RefreshEntries();
        }
    }
}
