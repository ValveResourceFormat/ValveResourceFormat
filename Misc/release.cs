using System.Diagnostics;
using System.Text.RegularExpressions;

if (args.Length == 0 || !Regex.IsMatch(args[0], @"^[0-9]+\.[0-9]+$"))
{
    Console.Error.WriteLine("Provide version as first argument. Example: 2.0");
    Environment.Exit(1);
}

var version = args[0];
var buildPropsPath = "Directory.Build.props";

if (!File.Exists(buildPropsPath))
{
    Console.Error.WriteLine("Directory.Build.props not found. Run this script from the repository root.");
    Environment.Exit(1);
}

var buildProps = File.ReadAllText(buildPropsPath);
var newBuildProps = Regex.Replace(
    buildProps,
    @"<ProjectBaseVersion>.+</ProjectBaseVersion>",
    $"<ProjectBaseVersion>{version}</ProjectBaseVersion>"
);

File.WriteAllText(buildPropsPath, newBuildProps);

RunGitCommand($"add {buildPropsPath}");
RunGitCommand($"commit --message \"Bump version to {version}\"");
RunGitCommand($"tag \"{version}\" --message \"{version}\" --sign");

static void RunGitCommand(string arguments)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        }
    };

    process.Start();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        Environment.Exit(process.ExitCode);
    }
}
