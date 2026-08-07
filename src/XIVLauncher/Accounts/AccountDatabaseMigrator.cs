using Serilog;
using SQLite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XIVLauncher.Accounts
{
    public sealed class AccountDatabaseMigrationException : Exception
    {
        public AccountDatabaseMigrationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class AccountDatabaseMigrationResult
    {
        public string DatabasePath { get; init; }
        public bool MigratedFromLegacy { get; init; }
        public bool NeedsCredentialValidation { get; init; }
        public string CurrentAccountId { get; init; }
        public int AccountCount { get; init; }
    }

    [Table(AccountDatabaseMigrator.AccountTableName)]
    public sealed class AccountDatabaseRecord
    {
        [PrimaryKey]
        [AutoIncrement]
        [Column("index")]
        public int Index { get; set; }

        [Unique]
        public string Id { get; set; }

        public string SndaId { get; set; }
        public string UserDefinedName { get; set; }
        public int AccountType { get; set; }
        public string LoginAccount { get; set; }
        public string AreaName { get; set; }
        public bool AutoLogin { get; set; }
        public string AutoLoginSessionKey { get; set; }
        public string KeepLoginKey { get; set; }
        public string Password { get; set; }
        public string TestSID { get; set; }
        public string NSessionId { get; set; }
    }

    [Table("AccountDatabaseMetadata")]
    internal sealed class AccountDatabaseMetadata
    {
        [PrimaryKey]
        public string Key { get; set; }

        public string Value { get; set; }
    }

    [Table("AccountMigrationMap")]
    internal sealed class AccountMigrationMap
    {
        [PrimaryKey]
        public string LegacyId { get; set; }

        public string CurrentId { get; set; }
    }

    public static class AccountDatabaseMigrator
    {
        public const string AccountTableName = "XivAccount";
        public const string LegacyDatabaseFileName = "accounts.db";
        public const string DatabaseFileName = "accounts-v3.db";

        private const string TemporaryDatabaseFileName = "accounts-v3.db.migrating";
        private const int CurrentSchemaVersion = 3;
        private const int LegacyWeGameSidAccountType = 2;
        private const int WeGameAccountType = 1;
        private const string SchemaVersionKey = "schema_version";
        private const string MigrationStatusKey = "migration_status";
        private const string CredentialValidationStatusKey = "credential_validation_status";
        private const string MigrationCompleted = "completed";
        private const string CredentialValidationPending = "pending";

        public static AccountDatabaseMigrationResult Prepare(string roamingPath, string currentAccountId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(roamingPath);

            var databasePath = Path.Combine(roamingPath, DatabaseFileName);
            var legacyDatabasePath = Path.Combine(roamingPath, LegacyDatabaseFileName);
            var temporaryDatabasePath = Path.Combine(roamingPath, TemporaryDatabaseFileName);

            try
            {
                Directory.CreateDirectory(roamingPath);

                if (File.Exists(databasePath))
                    return ReadExisting(databasePath, currentAccountId);

                DeleteTemporaryDatabaseFiles(temporaryDatabasePath);

                var legacyAccounts = File.Exists(legacyDatabasePath)
                    ? ReadLegacyAccounts(legacyDatabasePath)
                    : new List<AccountDatabaseRecord>();
                var normalized = NormalizeAccounts(legacyAccounts, currentAccountId);
                var migratedFromLegacy = File.Exists(legacyDatabasePath);

                CreateDatabase(
                    temporaryDatabasePath,
                    normalized.Accounts,
                    normalized.IdMap,
                    migratedFromLegacy);

                File.Move(temporaryDatabasePath, databasePath);

                Log.Information(
                    "Account database {MigrationAction}: {AccountCount} accounts -> {DatabasePath}",
                    migratedFromLegacy ? "migration completed" : "initialized",
                    normalized.Accounts.Count,
                    databasePath);

                return new AccountDatabaseMigrationResult
                {
                    DatabasePath = databasePath,
                    MigratedFromLegacy = migratedFromLegacy,
                    NeedsCredentialValidation = migratedFromLegacy,
                    CurrentAccountId = ResolveCurrentAccountId(normalized.IdMap, currentAccountId),
                    AccountCount = normalized.Accounts.Count,
                };
            }
            catch (Exception ex)
            {
                try
                {
                    DeleteTemporaryDatabaseFiles(temporaryDatabasePath);
                }
                catch (Exception cleanupException)
                {
                    Log.Warning(
                        cleanupException,
                        "Could not clean temporary account database {DatabasePath}",
                        temporaryDatabasePath);
                }

                if (ex is AccountDatabaseMigrationException)
                    throw;

                throw new AccountDatabaseMigrationException(
                    $"无法创建新版账号数据库。旧账号数据库未被修改：{legacyDatabasePath}",
                    ex);
            }
        }

        public static void MarkCredentialValidationCompleted(string databasePath)
        {
            using var connection = OpenReadWrite(databasePath);
            connection.RunInTransaction(() =>
                SetMetadata(connection, CredentialValidationStatusKey, MigrationCompleted));
        }

        private static AccountDatabaseMigrationResult ReadExisting(string databasePath, string currentAccountId)
        {
            try
            {
                using var connection = OpenReadOnly(databasePath);
                ValidateCompletedDatabase(connection);

                var idMap = connection.Table<AccountMigrationMap>()
                    .ToDictionary(x => x.LegacyId, x => x.CurrentId, StringComparer.Ordinal);
                var credentialValidationStatus = GetMetadata(connection, CredentialValidationStatusKey);

                return new AccountDatabaseMigrationResult
                {
                    DatabasePath = databasePath,
                    MigratedFromLegacy = false,
                    NeedsCredentialValidation = credentialValidationStatus == CredentialValidationPending,
                    CurrentAccountId = ResolveCurrentAccountId(idMap, currentAccountId),
                    AccountCount = connection.Table<AccountDatabaseRecord>().Count(),
                };
            }
            catch (Exception ex) when (ex is not AccountDatabaseMigrationException)
            {
                throw new AccountDatabaseMigrationException(
                    $"新版账号数据库无效，未执行自动删除：{databasePath}",
                    ex);
            }
        }

        private static void CreateDatabase(
            string databasePath,
            IReadOnlyCollection<AccountDatabaseRecord> accounts,
            IReadOnlyDictionary<string, string> idMap,
            bool migratedFromLegacy)
        {
            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

            connection.CreateTable<AccountDatabaseRecord>();
            connection.CreateTable<AccountDatabaseMetadata>();
            connection.CreateTable<AccountMigrationMap>();

            connection.RunInTransaction(() =>
            {
                foreach (var account in accounts)
                {
                    account.Index = 0;
                    connection.Insert(account);
                }

                foreach (var mapping in idMap)
                {
                    connection.Insert(new AccountMigrationMap
                    {
                        LegacyId = mapping.Key,
                        CurrentId = mapping.Value,
                    });
                }

                SetMetadata(connection, SchemaVersionKey, CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
                SetMetadata(connection, MigrationStatusKey, MigrationCompleted);
                SetMetadata(
                    connection,
                    CredentialValidationStatusKey,
                    migratedFromLegacy ? CredentialValidationPending : MigrationCompleted);
                SetMetadata(connection, "created_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                SetMetadata(connection, "source", migratedFromLegacy ? LegacyDatabaseFileName : "fresh");
            });
        }

        private static List<AccountDatabaseRecord> ReadLegacyAccounts(string databasePath)
        {
            using var connection = OpenReadOnly(databasePath);
            var columns = connection.GetTableInfo(AccountTableName)
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var requiredColumns = new[]
            {
                "index",
                "Id",
                "SndaId",
                "UserDefinedName",
                "AccountType",
                "LoginAccount",
                "AreaName",
                "AutoLogin",
                "AutoLoginSessionKey",
                "Password",
                "TestSID",
                "NSessionId",
            };

            var missingColumns = requiredColumns.Where(x => !columns.Contains(x)).ToArray();
            if (missingColumns.Length > 0)
                throw new InvalidDataException($"旧账号数据库缺少字段：{string.Join(", ", missingColumns)}");

            var keepLoginKeyExpression = columns.Contains("KeepLoginKey")
                ? "\"KeepLoginKey\""
                : "NULL";
            var sql = $"""
                SELECT
                    "index",
                    "Id",
                    "SndaId",
                    "UserDefinedName",
                    "AccountType",
                    "LoginAccount",
                    "AreaName",
                    "AutoLogin",
                    "AutoLoginSessionKey",
                    {keepLoginKeyExpression} AS "KeepLoginKey",
                    "Password",
                    "TestSID",
                    "NSessionId"
                FROM "XivAccount"
                ORDER BY "index"
                """;

            return connection.Query<AccountDatabaseRecord>(sql);
        }

        private static NormalizedAccounts NormalizeAccounts(
            IEnumerable<AccountDatabaseRecord> source,
            string currentAccountId)
        {
            var accountsById = new Dictionary<string, NormalizedAccount>(StringComparer.Ordinal);
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var sourceAccount in source)
            {
                var account = Clone(sourceAccount);
                var legacyId = account.Id;

                if (account.AccountType == LegacyWeGameSidAccountType)
                {
                    account.AccountType = WeGameAccountType;
                    account.LoginAccount = string.IsNullOrEmpty(account.LoginAccount)
                        ? account.SndaId
                        : account.LoginAccount;
                    account.Id = $"{GetUserName(account)}|WeGame";
                }

                if (!IsSupportedAccount(account))
                {
                    Log.Warning(
                        "Skipping invalid legacy account {AccountId} with type {AccountType}",
                        legacyId,
                        account.AccountType);
                    continue;
                }

                account.Index = 0;
                var isCurrent = string.Equals(legacyId, currentAccountId, StringComparison.Ordinal);
                idMap[legacyId] = account.Id;

                if (!accountsById.TryGetValue(account.Id, out var existing))
                {
                    accountsById.Add(account.Id, new NormalizedAccount(account, isCurrent));
                    continue;
                }

                var candidateIsPreferred = isCurrent && !existing.IsCurrent;
                var preferred = candidateIsPreferred ? account : existing.Account;
                var secondary = candidateIsPreferred ? existing.Account : account;
                accountsById[account.Id] = new NormalizedAccount(
                    MergeAccounts(preferred, secondary),
                    existing.IsCurrent || isCurrent);

                Log.Warning(
                    "Merged colliding legacy accounts into {AccountId}; preferred current account: {PreferredCurrent}",
                    account.Id,
                    candidateIsPreferred);
            }

            return new NormalizedAccounts(
                accountsById.Values.Select(x => x.Account).ToList(),
                idMap);
        }

        private static bool IsSupportedAccount(AccountDatabaseRecord account)
        {
            return (account.AccountType is 0 or WeGameAccountType)
                   && !string.IsNullOrEmpty(account.Id)
                   && !string.IsNullOrEmpty(GetUserName(account));
        }

        private static AccountDatabaseRecord MergeAccounts(
            AccountDatabaseRecord preferred,
            AccountDatabaseRecord secondary)
        {
            var merged = Clone(preferred);
            merged.SndaId = FirstNonEmpty(merged.SndaId, secondary.SndaId);
            merged.UserDefinedName = FirstNonEmpty(merged.UserDefinedName, secondary.UserDefinedName);
            merged.LoginAccount = FirstNonEmpty(merged.LoginAccount, secondary.LoginAccount);
            merged.AreaName = FirstNonEmpty(merged.AreaName, secondary.AreaName);
            merged.AutoLogin |= secondary.AutoLogin;
            merged.AutoLoginSessionKey = FirstNonEmpty(
                merged.AutoLoginSessionKey,
                secondary.AutoLoginSessionKey);
            merged.KeepLoginKey = FirstNonEmpty(merged.KeepLoginKey, secondary.KeepLoginKey);
            merged.NSessionId = FirstNonEmpty(merged.NSessionId, secondary.NSessionId);

            if (merged.AccountType == WeGameAccountType)
            {
                merged.Password = FirstNonEmpty(merged.Password, secondary.Password);
                merged.TestSID = FirstNonEmpty(merged.TestSID, secondary.TestSID);
            }
            else
            {
                merged.Password = FirstNonEmpty(merged.Password, secondary.Password);
                merged.TestSID = FirstNonEmpty(merged.TestSID, secondary.TestSID);
            }

            return merged;
        }

        private static string ResolveCurrentAccountId(
            IReadOnlyDictionary<string, string> idMap,
            string currentAccountId)
        {
            if (string.IsNullOrEmpty(currentAccountId))
                return currentAccountId;

            return idMap.TryGetValue(currentAccountId, out var mappedId)
                ? mappedId
                : currentAccountId;
        }

        private static void ValidateCompletedDatabase(SQLiteConnection connection)
        {
            var schemaVersion = GetMetadata(connection, SchemaVersionKey);
            var migrationStatus = GetMetadata(connection, MigrationStatusKey);
            var credentialValidationStatus = GetMetadata(connection, CredentialValidationStatusKey);

            if (schemaVersion != CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)
                || migrationStatus != MigrationCompleted
                || credentialValidationStatus is not (CredentialValidationPending or MigrationCompleted))
            {
                throw new InvalidDataException(
                    "账号数据库版本或迁移状态无效："
                    + $"schema={schemaVersion}, status={migrationStatus}, "
                    + $"credentialValidation={credentialValidationStatus}");
            }

            if (connection.GetTableInfo(AccountTableName).Count == 0)
                throw new InvalidDataException("账号数据库缺少账号表。");
        }

        private static string GetMetadata(SQLiteConnection connection, string key)
        {
            return connection.Table<AccountDatabaseMetadata>()
                .FirstOrDefault(x => x.Key == key)
                ?.Value;
        }

        private static void SetMetadata(SQLiteConnection connection, string key, string value)
        {
            connection.InsertOrReplace(new AccountDatabaseMetadata
            {
                Key = key,
                Value = value,
            });
        }

        private static SQLiteConnection OpenReadOnly(string databasePath)
        {
            return new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        }

        private static SQLiteConnection OpenReadWrite(string databasePath)
        {
            return new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
        }

        private static void DeleteTemporaryDatabaseFiles(string databasePath)
        {
            foreach (var path in new[]
                     {
                         databasePath,
                         databasePath + "-journal",
                         databasePath + "-wal",
                         databasePath + "-shm",
                     })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static string GetUserName(AccountDatabaseRecord account)
        {
            return string.IsNullOrEmpty(account.LoginAccount)
                ? account.SndaId
                : account.LoginAccount;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrEmpty(first) ? second : first;
        }

        private static AccountDatabaseRecord Clone(AccountDatabaseRecord account)
        {
            return new AccountDatabaseRecord
            {
                Index = account.Index,
                Id = account.Id,
                SndaId = account.SndaId,
                UserDefinedName = account.UserDefinedName,
                AccountType = account.AccountType,
                LoginAccount = account.LoginAccount,
                AreaName = account.AreaName,
                AutoLogin = account.AutoLogin,
                AutoLoginSessionKey = account.AutoLoginSessionKey,
                KeepLoginKey = account.KeepLoginKey,
                Password = account.Password,
                TestSID = account.TestSID,
                NSessionId = account.NSessionId,
            };
        }

        private sealed class NormalizedAccount
        {
            public NormalizedAccount(AccountDatabaseRecord account, bool isCurrent)
            {
                this.Account = account;
                this.IsCurrent = isCurrent;
            }

            public AccountDatabaseRecord Account { get; }
            public bool IsCurrent { get; }
        }

        private sealed class NormalizedAccounts
        {
            public NormalizedAccounts(
                IReadOnlyList<AccountDatabaseRecord> accounts,
                IReadOnlyDictionary<string, string> idMap)
            {
                this.Accounts = accounts;
                this.IdMap = idMap;
            }

            public IReadOnlyList<AccountDatabaseRecord> Accounts { get; }
            public IReadOnlyDictionary<string, string> IdMap { get; }
        }
    }
}
