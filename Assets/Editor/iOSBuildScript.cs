using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;
using System.Linq;

/// <summary>
/// Automated build script for iOS. Exports a fresh Xcode project to Builds/iOS.
/// Command line: Unity -batchmode -quit -projectPath . -buildTarget iOS -executeMethod iOSBuildScript.BuildFromCommandLine
/// </summary>
public class iOSBuildScript
{
    private const string BUILD_PATH = "Builds/iOS";
    private const string BUNDLE_ID = "BomSapo.Chess3D";
    private const string TEAM_ID = "KX25Q9LD97";

    [MenuItem("Build/Build iOS (Xcode project)")]
    public static void Build()
    {
        ConfigureiOSSettings();
        BuildiOS(BuildOptions.None);
    }

    [MenuItem("Build/Build iOS Development")]
    public static void BuildDevelopment()
    {
        ConfigureiOSSettings();
        BuildiOS(BuildOptions.Development);
    }

    public static void BuildFromCommandLine()
    {
        ConfigureiOSSettings();
        bool ok = BuildiOS(BuildOptions.None);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Brandon's phone builds (Builds/device-build.sh): a release build with BLITZ_REVIEW defined, so the
    /// Queen Blitz review and test packs can be switched on (five taps on Your pawns). TestFlight and
    /// App Store builds use BuildFromCommandLine, without it.
    /// </summary>
    public static void BuildDeviceFromCommandLine()
    {
        ConfigureiOSSettings();
        bool ok = BuildiOS(BuildOptions.None, BUILD_PATH, new[] { "BLITZ_REVIEW" });
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Export an Xcode project that runs in the iOS Simulator (Builds/iOS-Simulator).
    /// </summary>
    [MenuItem("Build/Build iOS Simulator")]
    public static void BuildSimulator()
    {
        ConfigureiOSSettings();
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
        try
        {
            BuildiOS(BuildOptions.Development, "Builds/iOS-Simulator");
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        }
    }

    public static void BuildSimulatorFromCommandLine()
    {
        ConfigureiOSSettings();
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
        bool ok = BuildiOS(BuildOptions.Development, "Builds/iOS-Simulator");
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static void ConfigureiOSSettings()
    {
        Debug.Log("⚙️ Configuring iOS Player Settings...");

        BuildShaderIncludes.EnsureIncluded();

        PlayerSettings.companyName = "Bom Sapo";
        PlayerSettings.productName = "3D Chess";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BUNDLE_ID);

        // Signing
        PlayerSettings.iOS.appleDeveloperTeamID = TEAM_ID;
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;

        // Runtime
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;

        // Plain http (the local test server) only in development builds; release builds use https
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;

        // App icon: the neon rook (rendered by Art/Pieces/render_app_icon.py). Unity scales the
        // 1024×1024 default icon to every size iOS needs.
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icon/AppIcon.png");
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        else Debug.LogWarning("iOSBuildScript: Assets/Art/Icon/AppIcon.png missing, app icon not set");

        // Landscape only until the UI is checked in portrait
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        Debug.Log("✅ iOS settings configured");
    }

    private static bool BuildiOS(BuildOptions options, string buildPath = BUILD_PATH, string[] extraDefines = null)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("❌ No scenes found in Build Settings!");
            return false;
        }

        // Always export a clean Xcode project
        if (Directory.Exists(buildPath))
        {
            Directory.Delete(buildPath, true);
        }
        Directory.CreateDirectory(buildPath);

        Debug.Log($"📦 Building {scenes.Length} scenes to: {buildPath}");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = buildPath, target = BuildTarget.iOS, options = options,
            extraScriptingDefines = extraDefines,
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"✅ iOS build succeeded: open {buildPath}/Unity-iPhone.xcodeproj in Xcode");
            return true;
        }

        Debug.LogError($"❌ iOS build failed with {report.summary.totalErrors} errors");
        return false;
    }
}
