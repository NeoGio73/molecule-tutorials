// TutorialBuilds.cs (Editor) — one WebGL build per tutorial, each with its own product name (the page title) and its
// own output folder, Builds/<Tutorial>/WebGL, so the files are named WebGL.* and each twin can be copied into its own
// folder of the GitHub Pages site. Gzip with the decompression fallback, because GitHub Pages does not send the
// Content-Encoding header Unity's compressed builds otherwise need. The page is the project's "Tutorial" template
// (Assets/WebGLTemplates/Tutorial: full window, title = product name). Player settings are restored afterwards.
using UnityEditor;
using UnityEngine;

public static class TutorialBuilds
{
    struct Tutorial { public string name, title, scene; }

    static readonly Tutorial[] All =
    {
        new Tutorial { name = "Hemoglobin", title = "Hemoglobin Tutorial", scene = "Assets/Scenes/Hemoglobin.unity" },
        new Tutorial { name = "Chymotrypsin", title = "Chymotrypsin Tutorial", scene = "Assets/Scenes/Chymotrypsin.unity" },
    };

    [MenuItem("Tutorials/Build WebGL/Hemoglobin")] static void BuildHemoglobin() { Debug.Log(Build("Hemoglobin")); }
    [MenuItem("Tutorials/Build WebGL/Chymotrypsin")] static void BuildChymotrypsin() { Debug.Log(Build("Chymotrypsin")); }
    [MenuItem("Tutorials/Build WebGL/All tutorials")] static void BuildAll() { foreach (var t in All) Debug.Log(Build(t.name)); }

    /// <summary>Builds one tutorial's WebGL twin and returns a one-line summary.</summary>
    public static string Build(string name)
    {
        Tutorial t = default(Tutorial); bool found = false;
        foreach (var x in All) if (x.name == name) { t = x; found = true; }
        if (!found) return "TutorialBuilds: unknown tutorial " + name;

        string product = PlayerSettings.productName;
        var compression = PlayerSettings.WebGL.compressionFormat;
        bool fallback = PlayerSettings.WebGL.decompressionFallback;
        string template = PlayerSettings.WebGL.template;
        string outDir = "Builds/" + t.name + "/WebGL";
        try
        {
            PlayerSettings.productName = t.title;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            if (System.IO.File.Exists("Assets/WebGLTemplates/Tutorial/index.html")) PlayerSettings.WebGL.template = "PROJECT:Tutorial";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { t.scene }, locationPathName = outDir,
                target = BuildTarget.WebGL, targetGroup = BuildTargetGroup.WebGL, options = BuildOptions.None,
            };
            var s = BuildPipeline.BuildPlayer(options).summary;
            return string.Format("TutorialBuilds: {0} {1}, {2} errors, {3:F1} MB, {4:F0} s -> {5}",
                                 t.name, s.result, s.totalErrors, s.totalSize / 1048576f, s.totalTime.TotalSeconds, outDir);
        }
        finally
        {
            PlayerSettings.productName = product;
            PlayerSettings.WebGL.compressionFormat = compression;
            PlayerSettings.WebGL.decompressionFallback = fallback;
            PlayerSettings.WebGL.template = template;
        }
    }
}
