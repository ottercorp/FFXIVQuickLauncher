using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common;
using XIVLauncher.Settings;
using SQLite;
using System.Drawing;

namespace XIVLauncher.Accounts
{
    public class AccountManager
    {
        private readonly object syncRoot = new();

        private SQLiteConnection? db;

        public ObservableCollection<XivAccount> Accounts;

        public XivAccount CurrentAccount
        {
            get
            {
                return Accounts.Count > 1 ? Accounts.FirstOrDefault(a => a.Id == _setting.CurrentAccountId) : Accounts.FirstOrDefault();
            }
            set => _setting.CurrentAccountId = value.Id;
        }

        private readonly ILauncherSettingsV3 _setting;

        public AccountManager(ILauncherSettingsV3 setting)
        {
            Load();

            _setting = setting;

            Accounts.CollectionChanged += Accounts_CollectionChanged;
        }

        private void Accounts_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Save();
        }

        public void AddAccount(XivAccount account)
        {
            var existingAccount = Accounts.FirstOrDefault(a => a.Equals(account));

            Log.Verbose($"existingAccount: {existingAccount?.Id}");

            if (existingAccount != null)
            {
                Log.Verbose("Updating account...");
                existingAccount.Password = account.Password;
                existingAccount.AutoLoginSessionKey = account.AutoLoginSessionKey;
                existingAccount.TestSID = account.TestSID;
                existingAccount.AreaName = account.AreaName;
                return;
            }

            Accounts.Add(account);
        }

        public void RemoveAccount(XivAccount account)
        {
            account.Password = string.Empty;
            Accounts.Remove(account);

            lock (this.syncRoot)
            {

                this.db.RunInTransaction(() =>
                {
                    var record = this.db.Table<XivAccount>().FirstOrDefault(a => a.Id == account.Id);
                    if (record != null)
                    {
                        this.db.Delete(account);
                    }
                });
            }
        }

        #region SaveLoad

        private static readonly string DatabasePath = Path.Combine(Paths.RoamingPath, "accounts.db");

        public void Save(XivAccount account)
        {
            lock (this.syncRoot)
            {

                this.db.RunInTransaction(() =>
                {
                    var record = this.db.Table<XivAccount>().FirstOrDefault(a => a.Id == account.Id);
                    if (record == null)
                    {
                        this.db.Insert(account);
                    }
                    else
                    {
                        record = account;
                        this.db.Update(record);
                    }
                });
            }
        }

        public void Save()
        {
            foreach (var item in Accounts)
            {
                this.Save(item);
            }
        }

        public void SetupDb()
        {
            this.db = new SQLiteConnection(DatabasePath,
                   SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            this.db.CreateTable<XivAccount>();
        }

        public void Load()
        {
            try
            {
                this.SetupDb();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load VFS database, starting fresh");

                if (File.Exists(DatabasePath))
                    File.Delete(DatabasePath);

                this.SetupDb();

            }

            // If the file is corrupted, this will be null anyway
            Accounts ??= new ObservableCollection<XivAccount>(this.db.Table<XivAccount>());
        }

        #endregion
    }
}
