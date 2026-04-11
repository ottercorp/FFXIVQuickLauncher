using System.Diagnostics;
using Avalonia.Interactivity;

namespace XIVLauncher.Support
{
    public static class SupportLinks
    {
        public static void OpenDiscord(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://discord.gg/QSDmvXG") { UseShellExecute = true });
        }

        public static void OpenFaq(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://ottercorp.github.io/faq") { UseShellExecute = true });
        }

        public static void OpenQQChannel(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://qun.qq.com/qqweb/qunpro/share?inviteCode=CZtWN") { UseShellExecute = true });
        }
    }
}
