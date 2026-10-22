using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Builds the Windows game to Builds/Windows/LifeInDowntown.exe (gitignored) with the game icon.
    // Prints BUILD_OK / BUILD_FAIL; exits 1 on failure.
    public static class BuildTool
    {
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const string IconPath = "Assets/Branding/GameIcon.png";
        public const string ExePath = "Builds/Windows/LifeInDowntown.exe";

        [MenuItem("Life In Downtown/Build Windows")]
        public static void BuildWindows()
        {
            PlayerSettings.productName = "Life In Downtown";
            PlayerSettings.companyName = "Hamzah Muhammad";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            else Debug.LogWarning($"BUILD_WARN no icon at {IconPath}");

            Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var s = report.summary;
            bool ok = s.result == BuildResult.Succeeded;
            Debug.Log($"{(ok ? "BUILD_OK" : "BUILD_FAIL")} result={s.result} size={s.totalSize / (1024 * 1024)} MB " +
                      $"time={s.totalTime} errors={s.totalErrors} path={Path.GetFullPath(ExePath)}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
