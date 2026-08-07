using System;
using System.Collections.Generic;
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
using System.Threading.Tasks;
using System.Windows;
using XIVLauncher.Windows;
namespace XIVLauncher.Accounts
{
    public sealed class AccountCredentialInitializationException : Exception
    {
        public AccountCredentialInitializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public class AccountManager
    {
        private readonly object syncRoot = new();

        private SQLiteConnection db;
        private readonly string databasePath;
        private readonly Task credentialInitializationTask;

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
            _setting = setting;

            var migration = AccountDatabaseMigrator.Prepare(
                Paths.RoamingPath,
                setting.CurrentAccountId);
            if (setting.CurrentAccountId != migration.CurrentAccountId)
                setting.CurrentAccountId = migration.CurrentAccountId;

            this.databasePath = migration.DatabasePath;
            Load();
            var credPath = Path.Combine(Paths.RoamingPath, "cred.json");
            try
            {
                this.CredData = new CredData("XIVLauncherCN", credPath);
            }
            catch (Exception ex)
            {
                throw new AccountCredentialInitializationException(
                    "账号凭据元数据读取失败，旧账号数据库和凭据文件均未被修改。",
                    ex);
            }

            Accounts.CollectionChanged += Accounts_CollectionChanged;
            this.credentialInitializationTask = InitializeCredentialProviderAsync(
                setting.CredType.GetValueOrDefault(CredType.WindowsCredManager),
                migration.NeedsCredentialValidation);
        }

        public async Task<string> Encrypt(string text)
        {
            try
            {
                if (text is null)
                    return null;

                await this.credentialInitializationTask;
                if (this.CredProvider == null)
                {
                    throw new Exception("CredProvider is null");
                }
                return await this.CredProvider.Encrypt(text);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to encrypt text");
                CustomMessageBox.Show(
                ex.ToString(),
                    "XIVLauncher Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

        public async Task<string> Decrypt(string text)
        {
            try
            {
                if (text is null)
                    return null;

                await this.credentialInitializationTask;
                if (this.CredProvider == null)
                {
                    throw new Exception("CredProvider is null");
                }
                return await this.CredProvider.Decrypt(text);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to encrypt text");
                CustomMessageBox.Show(
                ex.ToString(),
                    "XIVLauncher Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

        public async Task ClearCredentialCache()
        {
            await this.credentialInitializationTask;
            await this.CredProvider.ClearCache();
        }

        public async Task ChangeCredType(CredType? type)
        {
            await this.credentialInitializationTask;

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (type == this.CurrentCredType)
                return;

            var oldCred = this.CredProvider;
            var newCred = GetCredProvider(type.Value);
            await ValidateCredentialProviderAsync(newCred, type.Value);

            Log.Information($"Change cred type from {this.CurrentCredType} to {type}");
            var convertedCredentials = new List<(XivAccount Account, CredentialValues Values)>();
            var originalCredentials = new List<(XivAccount Account, CredentialValues Values)>();
            foreach (var item in Accounts)
            {
                originalCredentials.Add((item, GetCredentialValues(item)));
                convertedCredentials.Add((
                    item,
                    new CredentialValues
                    {
                        AutoLoginSessionKey = await ReencryptAsync(oldCred, newCred, item.AutoLoginSessionKey),
                        KeepLoginKey = await ReencryptAsync(oldCred, newCred, item.KeepLoginKey),
                        Password = await ReencryptAsync(oldCred, newCred, item.Password),
                        TestSID = await ReencryptAsync(oldCred, newCred, item.TestSID),
                        NSessionId = await ReencryptAsync(oldCred, newCred, item.NSessionId),
                    }));
            }

            foreach (var converted in convertedCredentials)
            {
                converted.Account.AutoLoginSessionKey = converted.Values.AutoLoginSessionKey;
                converted.Account.KeepLoginKey = converted.Values.KeepLoginKey;
                converted.Account.Password = converted.Values.Password;
                converted.Account.TestSID = converted.Values.TestSID;
                converted.Account.NSessionId = converted.Values.NSessionId;
            }

            try
            {
                Save();
            }
            catch
            {
                foreach (var original in originalCredentials)
                    ApplyCredentialValues(original.Account, original.Values);
                throw;
            }

            this.CurrentCredType = type;
            this.CredProvider = newCred;
            Log.Information($"Changed cred type to {type} successfully");
        }

        private async Task InitializeCredentialProviderAsync(
            CredType type,
            bool validateMigratedCredentials)
        {
            var credentialProvider = GetCredProvider(type);
            await ValidateCredentialProviderAsync(credentialProvider, type);
            this.CurrentCredType = type;
            this.CredProvider = credentialProvider;

            if (validateMigratedCredentials)
                await ValidateMigratedCredentialsAsync();
        }

        private static async Task ValidateCredentialProviderAsync(
            ICredProvider credentialProvider,
            CredType type)
        {
            if (!await credentialProvider.IsSupported())
                throw new Exception($"Cred type: {type} not supported");

            var testText = EncryptionHelper.GetRandomHexString(32);
            var encrypted = await credentialProvider.Encrypt(testText);
            var decrypted = await credentialProvider.Decrypt(encrypted);
            if (testText != decrypted)
                throw new Exception($"Cred type: {type} test failed");
        }

        private async Task ValidateMigratedCredentialsAsync()
        {
            var changed = false;

            foreach (var account in Accounts)
            {
                var autoLoginSessionKey = await ValidateCredentialAsync(
                    account.Id,
                    nameof(account.AutoLoginSessionKey),
                    account.AutoLoginSessionKey);
                var keepLoginKey = await ValidateCredentialAsync(
                    account.Id,
                    nameof(account.KeepLoginKey),
                    account.KeepLoginKey);
                var password = await ValidateCredentialAsync(
                    account.Id,
                    nameof(account.Password),
                    account.Password);
                var testSid = await ValidateCredentialAsync(
                    account.Id,
                    nameof(account.TestSID),
                    account.TestSID);
                var nSessionId = await ValidateCredentialAsync(
                    account.Id,
                    nameof(account.NSessionId),
                    account.NSessionId);

                changed |= account.AutoLoginSessionKey != autoLoginSessionKey
                           || account.KeepLoginKey != keepLoginKey
                           || account.Password != password
                           || account.TestSID != testSid
                           || account.NSessionId != nSessionId;

                account.AutoLoginSessionKey = autoLoginSessionKey;
                account.KeepLoginKey = keepLoginKey;
                account.Password = password;
                account.TestSID = testSid;
                account.NSessionId = nSessionId;
            }

            if (changed)
                Save();

            AccountDatabaseMigrator.MarkCredentialValidationCompleted(this.databasePath);
            Log.Information("Migrated account credential validation completed");
        }

        private async Task<string> ValidateCredentialAsync(
            string accountId,
            string fieldName,
            string encryptedValue)
        {
            if (encryptedValue == null)
                return null;

            try
            {
                var decrypted = await this.CredProvider.Decrypt(encryptedValue);
                if (decrypted == null)
                    throw new InvalidDataException("Credential provider returned an empty result.");
                return encryptedValue;
            }
            catch (Exception ex)
            {
                Log.Warning(
                    ex,
                    "Clearing invalid migrated credential {AccountId}.{FieldName}",
                    accountId,
                    fieldName);
                return null;
            }
        }

        private static async Task<string> ReencryptAsync(
            ICredProvider oldCredentialProvider,
            ICredProvider newCredentialProvider,
            string encryptedValue)
        {
            if (encryptedValue == null)
                return null;

            var decrypted = await oldCredentialProvider.Decrypt(encryptedValue);
            return await newCredentialProvider.Encrypt(decrypted);
        }

        private static CredentialValues GetCredentialValues(XivAccount account)
        {
            return new CredentialValues
            {
                AutoLoginSessionKey = account.AutoLoginSessionKey,
                KeepLoginKey = account.KeepLoginKey,
                Password = account.Password,
                TestSID = account.TestSID,
                NSessionId = account.NSessionId,
            };
        }

        private static void ApplyCredentialValues(XivAccount account, CredentialValues values)
        {
            account.AutoLoginSessionKey = values.AutoLoginSessionKey;
            account.KeepLoginKey = values.KeepLoginKey;
            account.Password = values.Password;
            account.TestSID = values.TestSID;
            account.NSessionId = values.NSessionId;
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
                existingAccount.AutoLogin = account.AutoLogin;
                existingAccount.AreaName = account.AreaName;
                existingAccount.NSessionId = account.NSessionId;

                if (!account.AutoLogin)
                {
                    existingAccount.Password = null;
                    existingAccount.AutoLoginSessionKey = null;
                    existingAccount.KeepLoginKey = null;
                    existingAccount.TestSID = null;
                }
                else if (account.AccountType == XivAccountType.WeGame)
                {
                    existingAccount.Password = account.Password;
                    existingAccount.TestSID = account.TestSID;
                    existingAccount.AutoLoginSessionKey = null;
                    existingAccount.KeepLoginKey = null;
                }
                else
                {
                    if (account.Password != null)
                        existingAccount.Password = account.Password;
                    existingAccount.AutoLoginSessionKey = account.AutoLoginSessionKey
                                                          ?? existingAccount.AutoLoginSessionKey;
                    existingAccount.KeepLoginKey = account.KeepLoginKey
                                                   ?? existingAccount.KeepLoginKey;
                }
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
            lock (this.syncRoot)
            {
                this.db.RunInTransaction(() =>
                {
                    foreach (var item in Accounts)
                    {
                        var record = this.db.Table<XivAccount>().FirstOrDefault(a => a.Id == item.Id);
                        if (record == null)
                            this.db.Insert(item);
                        else
                            this.db.Update(item);
                    }
                });
            }
        }

        public void SetupDb()
        {
            this.db = new SQLiteConnection(this.databasePath,
                   SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            this.db.CreateTable<XivAccount>();
        }

        public void Load()
        {
            this.SetupDb();

            // If the file is corrupted, this will be null anyway
            Accounts ??= new ObservableCollection<XivAccount>(this.db.Table<XivAccount>());

            var invalidAccounts = Accounts
                .Where(account => account.UserName.IsNullOrEmpty() || account.Id.IsNullOrEmpty())
                .ToList();

            foreach (var account in invalidAccounts)
            {
                Accounts.Remove(account);
            }
        }

        #endregion

        private sealed class CredentialValues
        {
            public string AutoLoginSessionKey { get; init; }
            public string KeepLoginKey { get; init; }
            public string Password { get; init; }
            public string TestSID { get; init; }
            public string NSessionId { get; init; }
        }
    }
}
