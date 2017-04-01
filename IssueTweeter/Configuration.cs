using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace IssueTweeter
{
    public sealed class Configuration
    {
        private static readonly string _configurationFileName = $"{nameof(Configuration)}.json";

        public IReadOnlyCollection<string> ExcludedAccounts { get; set; }
        public IReadOnlyCollection<FeedConfiguration> FeedConfigurations { get; set; }
        public string GitHubToken { get; set; }

        public static Configuration GetConfiguration() =>
           JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configurationFileName));
    }

    public sealed class FeedConfiguration
    {
        public IReadOnlyCollection<string> Repositories { get; set; }
        public TwitterAccountConfiguration TwitterAccountConfiguration { get; set; }
    }

    public sealed class TwitterAccountConfiguration
    {
        public string AccountName { get; set; }
        public string ConsumerKey { get; set; }
        public string ConsumerSecret { get; set; }
        public string AccessToken { get; set; }
        public string AccessTokenSecret { get; set; }
    }
}