using LinqToTwitter;
using MoreLinq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IssueTweeter
{
    internal static class Program
    {
        private const int BacklogHours = 2;
        private const int CharactersInTweet = 280;
        private const int CharactersInUrl = 23;
        private const string Ellipsis = "…";
        private const string FooterSeparator = " ";
        private const int MaxTweets = 5;
        private const string NewLineSeparator = "\n";

        private static Configuration _configuration;
        private static HashSet<string> _excludedAccounts;
        private static GitHubClient _gitHubClient;

        private static async Task Main()
        {
            _configuration = Configuration.GetConfiguration();
            _excludedAccounts = new HashSet<string>(_configuration.ExcludedAccounts);
            _gitHubClient =
                new GitHubClient(new ProductHeaderValue("DotnetIssuesTweeter"))
                {
                    Credentials = new Credentials(
                        _configuration.GitHubClientId,
                        _configuration.GitHubClientSecret)
                };

            foreach (var feedConfiguration in _configuration.FeedConfigurations)
            {
                await UpdateFeedAsync(feedConfiguration);
            }
        }

        private static async Task UpdateFeedAsync(FeedConfiguration feedConfiguration)
        {
            var twitterContext = new TwitterContext(new SingleUserAuthorizer
            {
                CredentialStore = new SingleUserInMemoryCredentialStore
                {
                    ConsumerKey = feedConfiguration.TwitterAccountConfiguration.ConsumerKey,
                    ConsumerSecret = feedConfiguration.TwitterAccountConfiguration.ConsumerSecret,
                    AccessToken = feedConfiguration.TwitterAccountConfiguration.AccessToken,
                    AccessTokenSecret = feedConfiguration.TwitterAccountConfiguration.AccessTokenSecret,
                }
            });

            var tweets =
                await feedConfiguration.Repositories.SelectManyAsync(_ =>
                    GenerateTweetsAsync(_, DateTime.UtcNow - TimeSpan.FromHours(BacklogHours)));

            var existingTweets =
                await twitterContext.Status
                    .Where(_ =>
                        _.Type == StatusType.User &&
                        _.ScreenName == feedConfiguration.TwitterAccountConfiguration.AccountName &&
                        _.Count == 200)
                    .ToListAsync();

            var existingIds = new HashSet<string>();
            var existingTitlePrefixes = new List<string>();
            foreach (var existingTweet in existingTweets.Select(_ => _.Text))
            {
                if (existingTweet.Contains(NewLineSeparator))
                {
                    var parts = existingTweet.Split(NewLineSeparator, 2);
                    var footer = parts[1];
                    if (footer.Contains(Ellipsis))
                    {
                        existingTitlePrefixes.Add(parts[0]);
                    }
                    else
                    {
                        existingIds.Add(footer.Split(FooterSeparator, 2)[0]);
                    }
                }
                else
                {
                    existingTitlePrefixes.Add(existingTweet.Split(Ellipsis, 2)[0]);
                }
            }

            var newTweets = tweets.
                Where(_ => !existingIds.Contains(_.Id) && 
                           !existingTitlePrefixes.Any(prefix => _.Title.StartsWith(prefix))).
                Take(MaxTweets);

            foreach (var newTweet in newTweets)
            {
                await twitterContext.TweetAsync(newTweet.ToString());
            }
        }

        private static async Task<IReadOnlyCollection<Tweet>> GenerateTweetsAsync(
            string repository,
            DateTimeOffset since)
        {
            var parts = repository.Split('/');
            var issues = await _gitHubClient.Issue.GetAllForRepository(
                parts[0],
                parts[1],
                new RepositoryIssueRequest
                {
                    Since = since,
                    State = ItemStateFilter.All
                });

            return issues.
                Where(_ => _.CreatedAt > since && !_excludedAccounts.Contains(_.User.Login)).
                Select(_ => GenerateTweet(repository, _)).
                ToList();
        }

        private static Tweet GenerateTweet(string repository, Issue issue)
        {
            var id = $"{repository} #{issue.Number}";
            var remainingCharacters = CharactersInTweet - (id.Length + 2 + CharactersInUrl);

            var title = issue.Title.Trim();
            title = EnforceLength(title, remainingCharacters);

            Regex.Matches(title, @"\w+(\.\w+)+").
                Cast<Match>().
                Select(match => match.Value.
                    Split('.').
                    Select(substring => substring.Length).
                    Pairwise((previous, current) => previous + current).
                    Min()).
                Where(minUrlLength => minUrlLength < CharactersInUrl).
                ForEach(minUrlLength => remainingCharacters -= CharactersInUrl - minUrlLength);
            title = EnforceLength(title, remainingCharacters);

            return new Tweet(id, title, issue.HtmlUrl);
        }

        private static string EnforceLength(string value, int length) =>
            value.Length > length
                ? $"{value.Substring(0, length - 1)}{Ellipsis}"
                : value;

        private class Tweet
        {
            private readonly string _url;

            public string Id { get; }
            public string Title { get; }

            public Tweet(string id, string title, string url)
            {
                Id = id;
                Title = title;
                _url = url;
            }

            public override string ToString() =>
                $"{Title}{NewLineSeparator}{Id}{FooterSeparator}{_url}";
        }
    }
}