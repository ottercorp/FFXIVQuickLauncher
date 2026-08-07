using Microsoft.VisualStudio.TestTools.UnitTesting;
using SQLite;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace XIVLauncher.Accounts.Tests
{
    [TestClass]
    public sealed class AccountDatabaseMigratorTests
    {
        private string testDirectory;

        [TestInitialize]
        public void Initialize()
        {
            SQLitePCL.Batteries_V2.Init();
            this.testDirectory = Path.Combine(
                Path.GetTempPath(),
                "XIVLauncher.AccountDatabaseMigratorTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.testDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(this.testDirectory))
                Directory.Delete(this.testDirectory, true);
        }

        [TestMethod]
        public void MigratesLegacyAccountsAndOpaqueCredentials()
        {
            var legacyPath = this.CreateLegacyDatabase(
                new LegacyAccount
                {
                    Id = "sdo|Sdo",
                    SndaId = "100",
                    LoginAccount = "sdo",
                    AccountType = 0,
                    AutoLogin = true,
                    AutoLoginSessionKey = "encrypted-session",
                    Password = "encrypted-password",
                },
                new LegacyAccount
                {
                    Id = "200|WeGameSid",
                    SndaId = "200",
                    AccountType = 2,
                    AutoLogin = true,
                    TestSID = "encrypted-sid",
                });

            var legacyBytes = File.ReadAllBytes(legacyPath);
            var result = AccountDatabaseMigrator.Prepare(this.testDirectory, "200|WeGameSid");

            Assert.IsTrue(result.MigratedFromLegacy);
            Assert.IsTrue(result.NeedsCredentialValidation);
            Assert.AreEqual("200|WeGame", result.CurrentAccountId);
            CollectionAssert.AreEqual(legacyBytes, File.ReadAllBytes(legacyPath));

            using var target = OpenReadOnly(result.DatabasePath);
            var accounts = target.Table<AccountDatabaseRecord>().ToArray();
            Assert.AreEqual(2, accounts.Length);
            var sdo = accounts.Single(x => x.Id == "sdo|Sdo");
            var weGame = accounts.Single(x => x.Id == "200|WeGame");
            Assert.AreEqual("encrypted-session", sdo.AutoLoginSessionKey);
            Assert.AreEqual("encrypted-password", sdo.Password);
            Assert.AreEqual(1, weGame.AccountType);
            Assert.AreEqual("encrypted-sid", weGame.TestSID);
        }

        [TestMethod]
        public void ReusesCompletedMigrationWithoutReadingLegacyAgain()
        {
            var legacyPath = this.CreateLegacyDatabase(new LegacyAccount
            {
                Id = "sdo|Sdo",
                SndaId = "100",
                LoginAccount = "sdo",
                AccountType = 0,
            });

            var first = AccountDatabaseMigrator.Prepare(this.testDirectory, "sdo|Sdo");
            File.WriteAllText(legacyPath, "legacy database changed after migration");
            var second = AccountDatabaseMigrator.Prepare(this.testDirectory, "sdo|Sdo");

            Assert.IsTrue(first.NeedsCredentialValidation);
            Assert.IsFalse(second.MigratedFromLegacy);
            Assert.IsTrue(second.NeedsCredentialValidation);
            Assert.AreEqual(first.DatabasePath, second.DatabasePath);
            Assert.AreEqual(1, second.AccountCount);

            AccountDatabaseMigrator.MarkCredentialValidationCompleted(second.DatabasePath);
            var third = AccountDatabaseMigrator.Prepare(this.testDirectory, "sdo|Sdo");
            Assert.IsFalse(third.NeedsCredentialValidation);
        }

        [TestMethod]
        public void ResolvesLegacyWeGameCollisionWithoutDuplicateRows()
        {
            this.CreateLegacyDatabase(
                new LegacyAccount
                {
                    Id = "same|WeGame",
                    SndaId = "same",
                    LoginAccount = "same",
                    AccountType = 1,
                    Password = "encrypted-token",
                },
                new LegacyAccount
                {
                    Id = "same|WeGameSid",
                    SndaId = "same",
                    AccountType = 2,
                    TestSID = "encrypted-sid",
                });

            var result = AccountDatabaseMigrator.Prepare(this.testDirectory, "same|WeGameSid");

            using var target = OpenReadOnly(result.DatabasePath);
            var accounts = target.Table<AccountDatabaseRecord>().ToArray();
            Assert.AreEqual(1, accounts.Length);
            Assert.AreEqual("same|WeGame", accounts[0].Id);
            Assert.AreEqual(1, accounts[0].AccountType);
            Assert.AreEqual("encrypted-sid", accounts[0].TestSID);
            Assert.AreEqual("encrypted-token", accounts[0].Password);
            Assert.AreEqual("same|WeGame", result.CurrentAccountId);
        }

        [TestMethod]
        public void FailedMigrationLeavesLegacyDatabaseUntouched()
        {
            var legacyPath = Path.Combine(
                this.testDirectory,
                AccountDatabaseMigrator.LegacyDatabaseFileName);
            var legacyBytes = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(legacyPath, legacyBytes);

            Assert.ThrowsException<AccountDatabaseMigrationException>(
                () => AccountDatabaseMigrator.Prepare(this.testDirectory, null));
            CollectionAssert.AreEqual(legacyBytes, File.ReadAllBytes(legacyPath));
            Assert.IsFalse(File.Exists(Path.Combine(
                this.testDirectory,
                AccountDatabaseMigrator.DatabaseFileName)));
            Assert.IsFalse(File.Exists(Path.Combine(
                this.testDirectory,
                "accounts-v3.db.migrating")));
        }

        [TestMethod]
        public void InvalidTargetDatabaseIsNotDeletedOrRecreated()
        {
            var result = AccountDatabaseMigrator.Prepare(this.testDirectory, null);
            using (var target = OpenReadWrite(result.DatabasePath))
            {
                target.Execute(
                    "DELETE FROM AccountDatabaseMetadata WHERE Key = ?",
                    "credential_validation_status");
            }

            var invalidDatabaseHash = HashFile(result.DatabasePath);
            Assert.ThrowsException<AccountDatabaseMigrationException>(
                () => AccountDatabaseMigrator.Prepare(this.testDirectory, null));
            Assert.IsTrue(File.Exists(result.DatabasePath));
            Assert.AreEqual(invalidDatabaseHash, HashFile(result.DatabasePath));
        }

        [TestMethod]
        public void RemovesStaleTemporaryDatabaseSidecarsBeforeMigration()
        {
            var temporaryPath = Path.Combine(this.testDirectory, "accounts-v3.db.migrating");
            File.WriteAllText(temporaryPath, "stale");
            File.WriteAllText(temporaryPath + "-journal", "stale");
            File.WriteAllText(temporaryPath + "-wal", "stale");
            File.WriteAllText(temporaryPath + "-shm", "stale");

            var result = AccountDatabaseMigrator.Prepare(this.testDirectory, null);

            Assert.IsTrue(File.Exists(result.DatabasePath));
            Assert.IsFalse(File.Exists(temporaryPath));
            Assert.IsFalse(File.Exists(temporaryPath + "-journal"));
            Assert.IsFalse(File.Exists(temporaryPath + "-wal"));
            Assert.IsFalse(File.Exists(temporaryPath + "-shm"));
        }

        [TestMethod]
        public void FreshInstallCreatesCompletedDatabase()
        {
            var result = AccountDatabaseMigrator.Prepare(this.testDirectory, null);

            Assert.IsFalse(result.MigratedFromLegacy);
            Assert.IsFalse(result.NeedsCredentialValidation);
            Assert.AreEqual(0, result.AccountCount);
            Assert.IsTrue(File.Exists(result.DatabasePath));
        }

        private string CreateLegacyDatabase(params LegacyAccount[] accounts)
        {
            var path = Path.Combine(
                this.testDirectory,
                AccountDatabaseMigrator.LegacyDatabaseFileName);
            using var connection = new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            connection.CreateTable<LegacyAccount>();
            foreach (var account in accounts)
                connection.Insert(account);
            return path;
        }

        private static SQLiteConnection OpenReadOnly(string path)
        {
            return new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        }

        private static string HashFile(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        private static SQLiteConnection OpenReadWrite(string path)
        {
            return new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
        }

        [Table(AccountDatabaseMigrator.AccountTableName)]
        private sealed class LegacyAccount
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
            public string Password { get; set; }
            public string TestSID { get; set; }
            public string NSessionId { get; set; }
        }
    }
}
