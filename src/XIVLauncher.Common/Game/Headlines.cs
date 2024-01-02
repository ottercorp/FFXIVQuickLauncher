using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Serilog;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game
{
    public partial class Headlines
    {
        [JsonProperty("news")]
        public News[] News { get; set; }

        [JsonProperty("topics")]
        public News[] Topics { get; set; }

        [JsonProperty("pinned")]
        public News[] Pinned { get; set; }

        [JsonProperty("banner")]
        public Banner[] Banner { get; set; }
    }

    public class Banner
    {
        [JsonProperty("HomeImagePath")]
        public Uri LsbBanner { get; set; }

        [JsonProperty("OutLink")]
        public Uri Link { get; set; }

        [JsonProperty("order_priority")]
        public int? OrderPriority { get; set; }

        [JsonProperty("fix_order")]
        public int? FixOrder { get; set; }
    }

    public class BannerRoot
    {
        [JsonProperty("banner")]
        public List<Banner> Banner { get; set; }
    }

    public class News
    {
        [JsonProperty("PublishDate")]
        public DateTimeOffset Date { get; set; }

        [JsonProperty("Title")]
        public string Title { get; set; }

        [JsonProperty("Author")]
        public string Url { get; set; }

        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public string Tag { get; set; }
    }

    public class SdoBanner {
        public Banner[] Data { get; set; }
    }

    public class SdoNews
    {
        public News[] Data { get; set; }
    }

    public partial class Headlines
    {
        public static async Task<Headlines> GetNews(Launcher game, ClientLanguage language, bool forceNa = false)
        {
            var headlines = new Headlines();
            headlines.Banner = await GetBanner(game);
            headlines.News = await GetNews(game);
            return headlines;
        }

        private static async Task<Banner[]> GetBanner(Launcher game) {
            var json = Encoding.UTF8.GetString(await game.DownloadAsLauncher("https://cqnews.web.sdo.com/api/news/newsList?gameCode=ff&CategoryCode=5203&pageIndex=0&pageSize=8", ClientLanguage.ChineseSimplified, "*/*").ConfigureAwait(false));
            var sdoBanner = JsonConvert.DeserializeObject<SdoBanner>(json);
            return sdoBanner.Data;
        }

        private static async Task<News[]> GetNews(Launcher game)
        {
            var json = Encoding.UTF8.GetString(await game.DownloadAsLauncher("https://cqnews.web.sdo.com/api/news/newsList?gameCode=ff&CategoryCode=5310,5311,5312,5313,5316&pageIndex=0&pageSize=12", ClientLanguage.ChineseSimplified, "*/*").ConfigureAwait(false));
            var sdoNews = JsonConvert.DeserializeObject<SdoNews>(json);
            return sdoNews.Data;
        }

        //public static async Task<IReadOnlyList<Banner>> GetBanners(Launcher game, ClientLanguage language, bool forceNa = false)
        //{
        //    var unixTimestamp = ApiHelpers.GetUnixMillis();
        //    var langCode = language.GetLangCode(forceNa);
        //    var url = $"https://frontier.ffxiv.com/v2/topics/{langCode}/banner.json?lang={langCode}&media=pcapp&_={unixTimestamp}";

        //    var json = Encoding.UTF8.GetString(await game.DownloadAsLauncher(url, language, "application/json, text/plain, */*").ConfigureAwait(false));

        //    return JsonConvert.DeserializeObject<BannerRoot>(json, Converter.SETTINGS).Banner;
        //}

        //public static async Task<IReadOnlyCollection<Banner>> GetMessage(Launcher game, ClientLanguage language, bool forceNa = false)
        //{
        //    var unixTimestamp = ApiHelpers.GetUnixMillis();
        //    var langCode = language.GetLangCode(forceNa);
        //    var url = $"https://frontier.ffxiv.com/v2/notice/{langCode}/message.json?_={unixTimestamp}";

        //    var json = Encoding.UTF8.GetString(await game.DownloadAsLauncher(url, language, "application/json, text/plain, */*").ConfigureAwait(false));

        //    return JsonConvert.DeserializeObject<BannerRoot>(json, Converter.SETTINGS).Banner;
        //}

        //public static async Task<IReadOnlyCollection<Banner>> GetWorlds(Launcher game, ClientLanguage language)
        //{
        //    var unixTimestamp = ApiHelpers.GetUnixMillis();
        //    var url = $"https://frontier.ffxiv.com/v2/world/status.json?_={unixTimestamp}";

        //    var json = Encoding.UTF8.GetString(await game.DownloadAsLauncher(url, language, "application/json, text/plain, */*").ConfigureAwait(false));

        //    return JsonConvert.DeserializeObject<BannerRoot>(json, Converter.SETTINGS).Banner;
        //}
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings SETTINGS = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            }
        };
    }

}
