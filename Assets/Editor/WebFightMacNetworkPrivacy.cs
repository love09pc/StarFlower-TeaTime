using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class WebFightMacNetworkPrivacy : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneOSX) return;
        string path = Path.Combine(report.summary.outputPath, "Contents/Info.plist");
        var document = XDocument.Load(path);
        var dictionary = document.Root.Element("dict");
        const string name = "NSLocalNetworkUsageDescription";
        var key = dictionary.Elements("key").FirstOrDefault(item => item.Value == name);
        const string message = "StarFlower::TeaTime uses your local network to find rooms and play with friends on the same Wi-Fi or LAN.";
        if (key == null) dictionary.Add(new XElement("key", name), new XElement("string", message));
        else key.ElementsAfterSelf().First().Value = message;
        // Mono's XML writer emits an empty internal DTD subset that Apple's plist parser rejects.
        document.DocumentType?.Remove();
        document.Save(path);

        // Unity signs before this postprocessor edits Info.plist. Strip build-host
        // metadata, re-sign the private demo, and verify the finished bundle.
        Run("/usr/bin/xattr", "-cr " + Quote(report.summary.outputPath), "metadata cleanup");
        Run("/usr/bin/codesign", "--force --deep --sign - " + Quote(report.summary.outputPath), "ad-hoc signing");
        Run("/usr/bin/codesign", "--verify --deep --strict " + Quote(report.summary.outputPath), "signature verification");
    }

    private static void Run(string fileName, string arguments, string description)
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
                throw new BuildFailedException("macOS " + description + " failed: " + output + error);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
