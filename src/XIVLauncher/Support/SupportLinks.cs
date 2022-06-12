using System.Diagnostics;
using System.Windows;

namespace XIVLauncher.Support
{
    public static class SupportLinks
    {
        public static void OpenDiscord(object sender, RoutedEventArgs e)
        {
            Process.Start("https://discord.gg/QSDmvXG");
        }

        public static void OpenFaq(object sender, RoutedEventArgs e)
        {
            Process.Start("https://ottercorp.github.io/faq/who_we_are");
        }

        public static void OpenQQChannel(object sender, RoutedEventArgs e)
        {
            Process.Start("https://qun.qq.com/qqweb/qunpro/share?inviteCode=CZtWN");
        }
    }
}