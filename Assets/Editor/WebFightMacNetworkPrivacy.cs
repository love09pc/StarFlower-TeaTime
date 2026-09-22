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
        const string message = "WebFight uses your local network to find rooms and play with friends on the same Wi-Fi or LAN.";
        if (key == null) dictionary.Add(new XElement("key", name), new XElement("string", message));
        else key.ElementsAfterSelf().First().Value = message;
        // Mono's XML writer emits an empty internal DTD subset that Apple's plist parser rejects.
        document.DocumentType?.Remove();
        document.Save(path);
    }
}
