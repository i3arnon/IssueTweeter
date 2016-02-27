using LinqToTwitter;
using Newtonsoft.Json;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IssueTweeter
{
    internal class Program
    {
        private readonly string _configurationFileName = $"{nameof(Configuration)}.json";

        private Configuration _configuration;
        private GitHubClient _gitHubClient;
        private HashSet<string> _excludedAccounts;

        private static void Main() => new Program().AsyncMain().Wait();

        private async Task AsyncMain()
        {
            _configuration = GetConfiguration();
            _gitHubClient = new GitHubClient(new ProductHeaderValue("DotnetIssuesTweeter"));
            _excludedAccounts = new HashSet<string>(_configuration.ExcludedAccounts);

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

            // Get issues for each repository
            var since = DateTime.UtcNow - TimeSpan.FromHours(1);
            List<Task<List<KeyValuePair<string, string>>>> issuesTasks =
                feedConfiguration.Repositories.Select(x => GetIssues(x, since)).ToList();
            await Task.WhenAll(issuesTasks);

            // Get recent tweets
            string twitterUser = feedConfiguration.TwitterAccountConfiguration.AccountName;
            List<Status> timeline = await twitterContext.Status
                .Where(x => x.Type == StatusType.User && x.ScreenName == twitterUser && x.Count == 200)
                .ToListAsync();

            // Aggregate and eliminate issues already tweeted
            List<string> tweets = issuesTasks
                .SelectMany(x => x.Result.Where(i => !timeline.Any(t => t.Text.Contains(i.Key))).Select(i => i.Value))
                .ToList();

            // Send tweets
            List<Task<Status>> tweetTasks = tweets.Select(x => twitterContext.TweetAsync(x)).ToList();
            await Task.WhenAll(tweetTasks);
        }

        // Kvp = owner/repository#issue, full text of tweet
        private async Task<List<KeyValuePair<string, string>>> GetIssues(
            string repository,
            DateTimeOffset since)
        {
            List<KeyValuePair<string, string>> tweets = new List<KeyValuePair<string, string>>();
            string[] ownerName = repository.Split('\\');
            IReadOnlyList<Issue> issues = await _gitHubClient.Issue
                .GetAllForRepository(ownerName[0], ownerName[1], new RepositoryIssueRequest { Since = since, State = ItemState.All });
            issues = issues.Where(x => x.CreatedAt > since && !_excludedAccounts.Contains(x.User.Login)).ToList();
            foreach (Issue issue in issues)
            {
                string key = $"{repository}#{issue.Number}";
                int remainingChars = 140 - (key.Length + 25);
                string value = $"{(issue.Title.Length <= remainingChars ? issue.Title : issue.Title.Substring(0, remainingChars))}\r\n{key} {issue.HtmlUrl}";
                tweets.Add(new KeyValuePair<string, string>(key, value));
            }
            return tweets;
        }

        private Configuration GetConfiguration() =>
            JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(_configurationFileName));
    }
}
