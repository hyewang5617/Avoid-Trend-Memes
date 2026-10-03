using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWindowsGame
{
    [MenuItem("Tools/Avoid Trend Memes/Build Windows Game")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building.");
        ValidateGameRules.Validate();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        AddressableAssetSettings.BuildPlayerContent(out var content);
        if (!string.IsNullOrEmpty(content.Error)) throw new Exception(content.Error);
        Directory.CreateDirectory("Builds/Windows");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/AvoidTrendMemes.unity" },
            locationPathName = "Builds/Windows/AvoidTrendMemes.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Directory.CreateDirectory("../tmp/verification");
        string result = $"{report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings, {report.summary.totalSize} bytes\n{DateTime.UtcNow:O}";
        File.WriteAllText("../tmp/verification/windows-build.txt", result);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(result);
        Debug.Log("Windows game built: " + Path.GetFullPath("Builds/Windows/AvoidTrendMemes.exe"));
    }
}
