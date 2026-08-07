using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XIVLauncher.Accounts.Cred;

namespace XIVLauncher.Accounts.Tests
{
    [TestClass]
    public sealed class CredDataTests
    {
        [TestMethod]
        public void DoesNotOverwriteMalformedCredentialMetadata()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "XIVLauncherCredDataTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "cred.json");
            const string malformedContent = """{"PackageName":"XIVLauncherCN"}""";
            File.WriteAllText(path, malformedContent);

            try
            {
                Assert.ThrowsException<InvalidDataException>(() => new CredData("XIVLauncherCN", path));
                Assert.AreEqual(malformedContent, File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
