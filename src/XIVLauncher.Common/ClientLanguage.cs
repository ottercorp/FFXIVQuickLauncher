namespace XIVLauncher.Common
{
    public enum ClientLanguage
    {
        Japanese,
        English,
        German,
        French,
        ChineseSimplified
    }

    public static class ClientLanguageExtensions
    {
        public static string GetLangCode(this ClientLanguage language)
        {
            switch (language)
            {
                case ClientLanguage.Japanese:
                    return "ja";

                case ClientLanguage.English when Util.IsRegionNorthAmerica():
                    return "en-us";

                case ClientLanguage.English:
                    return "en-gb";

                case ClientLanguage.German:
                    return "de";

                case ClientLanguage.French:
                    return "fr";

                case ClientLanguage.ChineseSimplified:
                    return "zh-CN";

                default:
                    return "en-gb";
            }
        }
    }
}