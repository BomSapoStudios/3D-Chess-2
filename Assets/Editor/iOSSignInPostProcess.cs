#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// Turns on Push Notifications (Assets/Plugins/iOS/Push.mm) and links the frameworks the
/// plugins use. Links AuthenticationServices (used by Assets/Plugins/iOS/AppleSignIn.mm) and turns on the
/// Sign in with Apple capability. The capability needs a signing profile that includes it,
/// which Xcode can only make while signed in to the Apple developer account, so it's added
/// only when ProjectSettings/EnableSignInWithApple exists.
/// </summary>
public static class iOSSignInPostProcess
{
    [PostProcessBuild(110)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        string frameworkTarget = project.GetUnityFrameworkTargetGuid();
        project.AddFrameworkToProject(frameworkTarget, "AuthenticationServices.framework", false);
        project.AddFrameworkToProject(frameworkTarget, "UserNotifications.framework", false);
        project.WriteToFile(projectPath);

        var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone/3DChess.entitlements", null, project.GetUnityMainTargetGuid());
        // Development entitlement; Xcode switches it to production when exporting for the App Store / TestFlight
        capabilities.AddPushNotifications(true);
        if (File.Exists("ProjectSettings/EnableSignInWithApple")) capabilities.AddSignInWithApple();
        capabilities.WriteToFile();
    }
}
#endif
