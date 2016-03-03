using LinqToTwitter;
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

        private static void Main() => new Program().AsyncMain().Wait();

        private async Task AsyncMain()
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
                .Where(x =>
                    x.Type == StatusType.User &&
                    x.ScreenName == feedConfiguration.TwitterAccountConfiguration.AccountName &&
                    x.Count == 200)
                .ToListAsync();

            await Task.WhenAll(tweetsTask, existingTweetsTask);

            var tweets = await tweetsTask;
            var existingTweets = await existingTweetsTask;

            var newTweets = tweets.
                Where(tweet => !existingTweets.Any(existingTweet => existingTweet.Text.Contains(tweet.Id))).
                Select(tweet => tweet.Contents);

            await newTweets.ForEachAsync(_ => twitterContext.TweetAsync(_));
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
                Where(x => x.CreatedAt > since && !_excludedAccounts.Contains(x.User.Login)).
                Select(issue => GenerateTweet(repository, issue)).
                ToList();
        }

        private Tweet GenerateTweet(string repository, Issue issue)
        {
            var id = $"{repository}#{issue.Number}";
            var remainingCharacters = CharactersInTweet - (id.Length + CharactersInUrl + 2);

            var title = EscapeUrl(issue.Title.Trim());
            if (title.Length > remainingCharacters)
            {
                title = $"{title.Substring(0, remainingCharacters - 1)}…";
            }

            return new Tweet(id, $"{title}\n{id} {issue.HtmlUrl}");
        }

        private string EscapeUrl(string value) =>
            Regex.Replace(value, @"\.net\b($|\s)", _ => $" {_.Value}", RegexOptions.IgnoreCase);

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
        }
    }
}