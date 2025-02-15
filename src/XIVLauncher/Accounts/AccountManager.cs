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
using XIVLauncher.Accounts.Cred;
using XIVLauncher.Accounts.Cred.CredProviders;
using Castle.Core.Internal;
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

        private readonly CredData CredData;
        private CredType? CurrentCredType;
        public ICredProvider CredProvider { get; private set; }

        public AccountManager(ILauncherSettingsV3 setting)
        {
            Load();

            _setting = setting;

            var credPath = Path.Combine(Paths.RoamingPath, "cred.json");
            this.CredData = new CredData("XIVLauncherCN", credPath);

            Accounts.CollectionChanged += Accounts_CollectionChanged;
            ChangeCredType(setting.CredType.GetValueOrDefault(CredType.WindowsCredManager));
        }

        public async void ChangeCredType(CredType? type)
        {
            if (type == this.CurrentCredType)
                return;
            var oldCred = this.CredProvider;
            var newCred = GetCredProvider(type.Value);
            var isSupported = await newCred.IsSupported();
            if (!isSupported)
            {
                throw new Exception($"Cred type: {type} not supported");
            }

            if (oldCred == null)
            {
                this.CurrentCredType = type;
                this.CredProvider = newCred;
                return;
            }

            Log.Information($"Change cred type from {this.CurrentCredType} to {type}");
            foreach (var item in Accounts)
            {
                if (item.AutoLoginSessionKey != null)
                {
                    try
                    {
                        var sessionKey = await oldCred.Decrypt(item.AutoLoginSessionKey);
                        item.AutoLoginSessionKey = await newCred.Encrypt(sessionKey);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to change {item.Id}.AutoLoginSessionKey");
                    }
                }

                if (item.TestSID != null)
                {
                    try
                    {
                        var testSid = await oldCred.Decrypt(item.TestSID);
                        item.TestSID = await newCred.Encrypt(testSid);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to change {item.Id}.TestSID");
                    }
                }
                Save();
            }

            this.CurrentCredType = type;
            this.CredProvider = newCred;
        }

        private ICredProvider GetCredProvider(CredType type)
        {
            switch (type)
            {
                case CredType.WindowsCredManager:
                    return new CredentialManager(this.CredData);
                case CredType.WindowsHello:
                    return new WindowsHello(this.CredData);
                case CredType.NoEncryption:
                    return new NoCred(this.CredData);
            }
            return null;
        }

        private void Accounts_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Save();
        }

        public void AddAccount(XivAccount account)
        {
            if (account.UserName.IsNullOrEmpty() || account.Id.IsNullOrEmpty())
            {
                throw new Exception($"UserName:{account.UserName} Id:{account.Id} 不能为空");
            }

            var existingAccount = Accounts.FirstOrDefault(a => a.Equals(account));

            Log.Verbose($"existingAccount: {existingAccount?.Id}");

            if (existingAccount != null)
            {
                Log.Verbose("Updating account...");
                existingAccount.Id = account.Id;
                existingAccount.Password = account.Password;
                existingAccount.AutoLogin = account.AutoLogin;
                existingAccount.AutoLoginSessionKey = account.AutoLoginSessionKey;
                existingAccount.TestSID = account.TestSID;
                existingAccount.AreaName = account.AreaName;
                return;
            }
            else
            {
                Accounts.Add(account);
            }
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
            ChangeCredType(this.CurrentCredType);
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

            foreach (var account in Accounts)
            {
                if (account.UserName.IsNullOrEmpty() || account.Id.IsNullOrEmpty())
                {
                    Accounts.Remove(account);
                }
            }
        }

        #endregion
    }
}
