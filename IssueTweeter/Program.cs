using LinqToTwitter;
using MoreLinq;
using Newtonsoft.Json;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IssueTweeter
{
    internal class Program
    {
        private const int CharactersInTweet = 140;
        private const int CharactersInUrl = 23;

        private readonly string _configurationFileName = $"{nameof(Configuration)}.json";

        private Configuration _configuration;
        private HashSet<string> _excludedAccounts;
        private GitHubClient _gitHubClient;

        private static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            _configuration = GetConfiguration();
            _excludedAccounts = new HashSet<string>(_configuration.ExcludedAccounts);
            _gitHubClient = new GitHubClient(new ProductHeaderValue("DotnetIssuesTweeter"))
            {
                Credentials = new Credentials(_configuration.GitHubToken),
            };

            await _configuration.FeedConfigurations.ForEachAsync(UpdateFeed);
        }

        private async Task UpdateFeed(FeedConfiguration feedConfiguration)
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

            var tweetsTask = feedConfiguration.Repositories.SelectManyAsync(_ =>
                GenerateTweets(_, DateTime.UtcNow - TimeSpan.FromHours(1)));

            var existingTweetsTask = twitterContext.Status
                .Where(_ =>
                    _.Type == StatusType.User &&
                    _.ScreenName == feedConfiguration.TwitterAccountConfiguration.AccountName &&
                    _.Count == 200)
                .ToListAsync();

            await Task.WhenAll(tweetsTask, existingTweetsTask);

            var tweets = await tweetsTask;
            var existingTweets = await existingTweetsTask;

            var newTweets = tweets.
                Where(_ => !existingTweets.Any(existingTweet => existingTweet.Text.Contains(_.Id))).
                Select(_ => _.Contents);

            await newTweets.ForEachAsync(twitterContext.TweetAsync);
        }

        private async Task<IReadOnlyCollection<Tweet>> GenerateTweets(
            string repository,
            DateTimeOffset since)
        {
            var repositoryParts = repository.Split('\\');
            var issues = await _gitHubClient.Issue.GetAllForRepository(
                repositoryParts[0],
                repositoryParts[1],
                new RepositoryIssueRequest
                {
                    Since = since,
                    State = ItemState.All
                });

            return issues.
                Where(_ => _.CreatedAt > since && !_excludedAccounts.Contains(_.User.Login)).
                Select(_ => GenerateTweet(repository, _)).
                ToList();
        }

        private Tweet GenerateTweet(string repository, Issue issue)
        {
            var id = $"{repository}#{issue.Number}";
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

            return new Tweet(id, $"{title}\n{id} {issue.HtmlUrl}");
        }

        private string EnforceLength(string value, int length) =>
            value.Length > length
                ? $"{value.Substring(0, length - 1)}…"
                : value;

        private Configuration GetConfiguration() =>
            JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configurationFileName));

        private class Tweet
        {
            public string Id { get; }
            public string Contents { get; }

            public Tweet(string id, string contents)
            {
                Id = id;
                Contents = contents;
            }

            public override string ToString() => Contents;
        }
    }
}