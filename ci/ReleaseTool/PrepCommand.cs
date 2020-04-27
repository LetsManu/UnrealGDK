using System;
using System.IO;
using System.Linq;
using CommandLine;
using Newtonsoft.Json.Linq;
using NLog;

namespace ReleaseTool
{
    /// <summary>
    ///     Runs the steps required to cut a release candidate branch.
    ///     * Adds the spatialos org remote to our local copy and fetch this remote.
    ///     * Checks out the source branch (master or 4.xx-SpatialOSUnrealGDK for the engine repo).
    ///     * Makes repo-dependent changes for prepping the release (e.g. updating version files).
    ///     * Pushes this to an RC branch.
    ///     * Creates a release branch if it doesn't exist.
    ///     * Opens a PR for merging the RC branch into the release branch.
    /// </summary>

    internal class PrepCommand
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string CandidateCommitMessageTemplate = "Release candidate for version {0}.";
        private const string ReleaseBranchCreationCommitMessageTemplate = "Create release branch off {0} release candidate."; // TODO: modify this line if we create the RC off master isntead
        private const string PullRequestTemplate = "Release {0}";

        // Names of the version files that live in the UnrealEngine repository.
        private const string UnrealGDKVersionFile = "UnrealGDKVersion.txt";
        private const string UnrealGDKExampleProjectVersionFile = "UnrealGDKExampleProjectVersion.txt";

        // Plugin file configuration.
        private const string pluginFileName = "SpatialGDK.uplugin";
        private const string VersionKey = "Version";
        private const string VersionNameKey = "VersionName";

        // Changelog file configuration
        private const string ChangeLogFilename = "CHANGELOG.md";
        private const string ChangeLogReleaseHeadingTemplate = "## [`{0}`] - {1:yyyy-MM-dd}";

        [Verb("prep", HelpText = "Prep a release candidate branch.")]
        public class Options : GitHubClient.IGitHubOptions, BuildkiteMetadataSink.IBuildkiteOptions
        {
            [Value(0, MetaName = "version", HelpText = "The release version that is being cut.", Required = true)]
            public string Version { get; set; }

            [Option("source-branch", HelpText = "The source branch name from which we are cutting the candidate.", Required = true)]
            public string SourceBranch { get; set; }

            [Option("candidate-branch", HelpText = "The candidate branch name.", Required = true)]
            public string CandidateBranch { get; set; }

            [Option("release-branch", HelpText = "The name of the branch into which we are merging the candidate.", Required = true)]
            public string ReleaseBranch { get; set; }

            [Option("git-repository-name", HelpText = "The Git repository that we are targeting.", Required = true)]
            public string GitRepoName { get; set; }

            [Option("github-organization", HelpText = "The Github Organization that contains the targeted repository.", Required = true)]
            public string GithubOrgName { get; set; }

            #region IBuildkiteOptions implementation

            public string MetadataFilePath { get; set; }

            #endregion

            #region IGithubOptions implementation

            public string GitHubTokenFile { get; set; }

            public string GitHubToken { get; set; }

            #endregion
        }

        private readonly Options options;

        public PrepCommand(Options options)
        {
            this.options = options;
        }

        /*
         *     This tool is designed to be used with a robot Github account. When we prep a release:
         *         1. Checkout of the repo.
         *         2. Checkout the source branch (master or 4.xx-SpatialOSUnrealGDK for the engine repo).
         *         3. Make repo-dependent changes for prepping the release (e.g. updating version files).
         *         4. Push this to an RC branch.
         *         5. Create a release branch if it doesn't exist
         *         6. Open a PR for merging the RC branch into the release branch.
         */
        public int Run()
        {
            Common.VerifySemanticVersioningFormat(options.Version);

            var remoteUrl = string.Format(Common.RepoUrlTemplate, options.GithubOrgName, options.GitRepoName);

            try
            {
                var gitHubClient = new GitHubClient(options);

                using (var gitClient = GitClient.FromRemote(remoteUrl))
                {
                    // This does step 2 from above.
                    gitClient.CheckoutRemoteBranch(options.SourceBranch, options.GithubOrgName);

                    // This does step 3 from above.
                    switch (options.GitRepoName)
                    {
                        case "UnrealGDK":
                            UpdateChangeLog(ChangeLogFilename, options, gitClient);
                            UpdatePluginFile(pluginFileName, gitClient);
                            break;
                        case "UnrealEngine":
                            UpdateVersionFile(gitClient, options.Version, UnrealGDKVersionFile);
                            UpdateVersionFile(gitClient, options.Version, UnrealGDKExampleProjectVersionFile);
                            break;
                    }

                    // This does step 4 from above.
                    gitClient.Commit(string.Format(CandidateCommitMessageTemplate, options.Version));
                    gitClient.ForcePush(options.CandidateBranch);

                    // This does step 5 from above.
                    if (!gitClient.LocalBranchExists(options.ReleaseBranch))
                    {
                        gitClient.CheckoutRemoteBranch(options.CandidateBranch, options.GithubOrgName); // TODO: Remove this line if we want to create release from master
                        gitClient.Commit(string.Format(ReleaseBranchCreationCommitMessageTemplate, options.Version));
                        gitClient.ForcePush(options.ReleaseBranch);
                    }

                    // This does step 6 from above.
                    var gitHubRepo = gitHubClient.GetRepositoryFromUrl(remoteUrl);
                    var branchFrom = options.SourceBranch;
                    var branchTo = options.ReleaseBranch;

                    // Only open a PR if one does not exist yet.
                    if (!gitHubClient.TryGetPullRequest(gitHubRepo, branchFrom, branchTo, out var pullRequest))
                    {
                        pullRequest = gitHubClient.CreatePullRequest(gitHubRepo,
                            branchFrom,
                            branchTo,
                            string.Format(PullRequestTemplate, options.Version),
                            GetPullRequestBody(options.GitRepoName));
                    }

                    if (BuildkiteMetadataSink.CanWrite(options))
                    {
                        using (var sink = new BuildkiteMetadataSink(options))
                        {
                            sink.WriteMetadata($"{options.GitRepoName}-release-branch",
                                $"pull/{pullRequest.Number}/head:{options.CandidateBranch}");
                            sink.WriteMetadata($"{options.GitRepoName}-pr-url", pullRequest.HtmlUrl);
                        }
                    }

                    Logger.Info("Pull request available: {0}", pullRequest.HtmlUrl);
                    Logger.Info("Successfully created release!");
                    Logger.Info("Release hash: {0}", gitClient.GetHeadCommit().Sha);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "ERROR: Unable to prep release candidate branch. Error: {0}", e);
                return 1;
            }

            return 0;
        }

        internal static void UpdateChangeLog(string ChangeLogFilePath, Options options, GitClient gitClient)
        {
            using (new WorkingDirectoryScope(gitClient.RepositoryPath))
            {
                if (File.Exists(ChangeLogFilePath))
                {
                    Logger.Info("Updating {0}...", ChangeLogFilePath);

                    var changelog = File.ReadAllLines(ChangeLogFilePath).ToList();

                    // If we already have a changelog entry for this release. Skip this step.
                    if (changelog.Any(line => IsMarkdownHeading(line, 2, $"[`{options.Version}`] - ")))
                    {
                        Logger.Info($"Changelog already has release version {options.Version}. Skipping..", ChangeLogFilePath);
                        return;
                    }

                    // First add the new release heading under the "## Unreleased" one.
                    // Assuming that this is the first heading.
                    var unreleasedIndex = changelog.FindIndex(line => IsMarkdownHeading(line, 2));
                    var releaseHeading = string.Format(ChangeLogReleaseHeadingTemplate, options.Version,
                        DateTime.Now);

                    changelog.InsertRange(unreleasedIndex + 1, new[]
                    {
                        string.Empty,
                        releaseHeading
                    });

                    File.WriteAllLines(ChangeLogFilePath, changelog);
                    gitClient.StageFile(ChangeLogFilePath);
                }
            }
        }

        private static bool IsMarkdownHeading(string markdownLine, int level, string startTitle = null)
        {
            var heading = $"{new string('#', level)} {startTitle ?? string.Empty}";

            return markdownLine.StartsWith(heading);
        }

        private static void UpdateVersionFile(GitClient gitClient, string fileContents, string filePath)
        {
            Logger.Info($"Updating contents of version file '{0}' to '{1}'...", filePath, fileContents);

            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException("Could not update the version file as the file " +
                    $"'{filePath}' does not exist.");
            }

            // Pin is always to master in this case.
            File.WriteAllText(filePath, $"{fileContents}");

            gitClient.StageFile(filePath);
        }

        private void UpdatePluginFile(string pluginFilePath, GitClient gitClient)
        {
            Logger.Info("Updating {0}...", pluginFilePath);

            JObject jsonObject;
            using (var streamReader = new StreamReader(pluginFilePath))
            {
                jsonObject = JObject.Parse(streamReader.ReadToEnd());

                if (jsonObject.ContainsKey(VersionKey) && jsonObject.ContainsKey(VersionNameKey))
                {
                    var oldVersion = (string) jsonObject[VersionNameKey];
                    if (ShouldIncrementPluginVersion(oldVersion, options.Version))
                    {
                        jsonObject[VersionKey] = ((int)jsonObject[VersionKey] + 1);
                    }

                    // Update the version name to the new one
                    jsonObject[VersionNameKey] = options.Version;
                }
                else
                {
                    throw new InvalidOperationException($"Could not update the plugin file at '{pluginFilePath}', " +
                        $"because at least one of the two expected keys '{VersionKey}' and '{VersionNameKey}' " +
                        $"could not be found.");
                }
            }

            File.WriteAllText(pluginFilePath, jsonObject.ToString());

            gitClient.StageFile(pluginFilePath);
        }

        private bool ShouldIncrementPluginVersion(string oldVersionName, string newVersionName)
        {
            var oldMajorMinorVersions = oldVersionName.Split('.').Take(2).Select(s => int.Parse(s));
            var newMajorMinorVersions = newVersionName.Split('.').Take(2).Select(s => int.Parse(s));
            return Enumerable.Any(Enumerable.Zip(oldMajorMinorVersions, newMajorMinorVersions, (o, n) => o < n));
        }

        // TODO: Alter the PR bodies so that they reflect the Unreal GDK release process (note that these are the for the merge of the RC into release)
        private static string GetPullRequestBody(string repoName)
        {
            switch (repoName)
            {
                case "UnrealGDK":
                    return @"#### Description
- Package versions
- Changelog
- Upgrade guide

#### Tests
- Windows
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
	- [ ] editor tooling
- Mac
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
	- [ ] editor tooling
- Android
	- [ ] local client
	- [ ] cloud client
- iOS
	- [ ] local client
	- [ ] cloud client";
                case "UnrealGDKExampleProject":
                    return @"#### Description
- Package versions
- Changelog
- Pinned gdk

#### Tests
- Windows
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Mac
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Android
	- [ ] local client
	- [ ] cloud client
- iOS
	- [ ] local client
	- [ ] cloud client";
                case "UnrealGDKTestGyms":
                    return @"#### Description
- Package versions
- Changelog
- pinned gdk

#### Tests
- Windows
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Mac
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Android
	- [ ] local client
	- [ ] cloud client
- iOS
	- [ ] local client
	- [ ] cloud client";
                case "UnrealGDKEngineNetTest":
                    return @"#### Description
- Package versions
- Changelog
- pinned gdk

#### Tests
- Windows
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Mac
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Android
	- [ ] local client
	- [ ] cloud client
- iOS
	- [ ] local client
	- [ ] cloud client";
                case "UnrealEngine":
                    return @"#### Description
- Package versions
- Changelog
- pinned gdk

#### Tests
- Windows
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Mac
	- [ ] local deploy
	- [ ] cloud client (Release QA pipeline)
- Android
	- [ ] local client
	- [ ] cloud client
- iOS
	- [ ] local client
	- [ ] cloud client";
                default:
                    throw new ArgumentException($"No PR body template found for repo {repoName}");
            }
        }
    }
}