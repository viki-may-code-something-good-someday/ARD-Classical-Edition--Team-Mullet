using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildPostProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        // Pfad zur Quelldatei (liegt im Projekt-Root, neben dem Assets-Ordner)
        string sourcePath = Path.Combine(Application.dataPath, "..", "steam_appid.txt");
        sourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(sourcePath))
        {
            Debug.LogWarning($"[BuildPostProcessor] steam_appid.txt nicht gefunden unter: {sourcePath}\n" +
                             "Bitte lege die Datei im Projekt-Root ab (neben dem Assets-Ordner).");
            return;
        }

        // Zielordner = der Ordner, in dem die .exe liegt
        string buildFolder = Path.GetDirectoryName(report.summary.outputPath);
        string destPath = Path.Combine(buildFolder, "steam_appid.txt");

        File.Copy(sourcePath, destPath, overwrite: true);
        Debug.Log($"[BuildPostProcessor] steam_appid.txt kopiert nach: {destPath}");
    }
}