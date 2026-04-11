using System;
using System.IO;
using System.Linq;
using System.Net;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XIVLauncher.Common;

namespace XIVLauncher.Accounts
{
    class AccountSwitcherEntry
    {
        private static readonly IImage DefaultImage = new Bitmap(AssetLoader.Open(new Uri("avares://XIVLauncherCN/Resources/defaultprofile.png")));

        public XivAccount Account { get; set; }
        public IImage ProfileImage { get; set; } = DefaultImage;

        public void UpdateProfileImage()
        {
            if (string.IsNullOrEmpty(Account.ThumbnailUrl))
                return;

            var cacheFolder = Path.Combine(Paths.RoamingPath, "profilePictures");
            Directory.CreateDirectory(cacheFolder);

            var uri = new Uri(Account.ThumbnailUrl);
            var cacheFile = Path.Combine(cacheFolder, uri.Segments.Last());

            byte[] imageBytes;

            if (File.Exists(cacheFile))
            {
                imageBytes = File.ReadAllBytes(cacheFile);
            }
            else
            {
                using (var client = new WebClient())
                {
                    imageBytes = client.DownloadData(uri);
                }

                File.WriteAllBytes(cacheFile, imageBytes);
            }

            using var stream = new MemoryStream(imageBytes);
            ProfileImage = new Bitmap(stream);
        }
    }
}
