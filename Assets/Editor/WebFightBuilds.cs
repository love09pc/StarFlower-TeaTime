using System;
using System.IO;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebFightBuilds
{
    [MenuItem("Web Fight/Build/Windows x64 - Steam Target")]
    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, "Builds/Windows/WebFight.exe");
    }

    [MenuItem("Web Fight/Build/macOS Universal - Private")]
    public static void BuildMac()
    {
        PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, 2);
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "StarFlowerTeaTimeMacBuild");
        string temporaryApp = Path.Combine(temporaryDirectory, "WebFight.app");
        string destinationApp = "Builds/macOS/WebFight.app";

        if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        try
        {
            Build(BuildTarget.StandaloneOSX, temporaryApp);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationApp));
            if (Directory.Exists(destinationApp)) Directory.Delete(destinationApp, true);
            Run("/usr/bin/ditto", "--noextattr --noqtn " + Quote(temporaryApp) + " " + Quote(destinationApp));
            Debug.Log("Web Fight macOS build ready: " + Path.GetFullPath(destinationApp));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    [MenuItem("Web Fight/Verify Combat and LAN", true)]
    private static bool CanVerify() => EditorApplication.isPlaying;

    [MenuItem("Web Fight/Verify Combat and LAN")]
    private static void Verify()
    {
        var game = UnityEngine.Object.FindFirstObjectByType<WebFightBootstrap>();
        if (game == null) throw new InvalidOperationException("Open SampleScene in Play mode first.");
        game.BeginVerification();
    }

    private static void Build(BuildTarget target, string path)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            throw new InvalidOperationException("Install the matching Unity Build Support module in Unity Hub, then restart the Editor.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = path,
            target = target,
            options = BuildOptions.DetailedBuildReport
        });
        bool outputExists = target == BuildTarget.StandaloneOSX
            ? Directory.Exists(path) && Directory.Exists(Path.Combine(path, "Contents/MacOS"))
            : File.Exists(path);
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0 || !outputExists)
            throw new InvalidOperationException("Build incomplete: " + report.summary.result
                + ", errors=" + report.summary.totalErrors + ", outputExists=" + outputExists);
        Debug.Log("Web Fight build ready: " + Path.GetFullPath(path));
    }

    private static void Run(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new BuildFailedException("macOS build copy failed: " + output + error);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
