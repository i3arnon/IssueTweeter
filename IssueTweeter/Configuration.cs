using System.Collections.Generic;

namespace IssueTweeter
{
    public sealed class Configuration
    {
        public IReadOnlyCollection<string> ExcludedAccounts { get; set; }
        public IReadOnlyCollection<FeedConfiguration> FeedConfigurations { get; set; }
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
