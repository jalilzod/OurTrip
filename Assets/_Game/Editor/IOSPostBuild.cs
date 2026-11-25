#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// Runs automatically after every iOS build and adds the permission texts iOS requires
/// to the Xcode project's Info.plist. Must live in a folder named "Editor".
/// </summary>
public static class IOSPostBuild
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        // Needed to save photos from photo mode into the Photos app.
        plist.root.SetString("NSPhotoLibraryAddUsageDescription",
            "Save the photos you take together in Our Trip to your Photos.");

        plist.WriteToFile(plistPath);
    }
}
#endif