using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Model-path validation, loading, camera fitting, and last-file persistence.
public sealed partial class MainForm
{
    /// <summary>
    /// Validates and loads a GLB or glTF file, updates the camera, and persists the path for the next launch.
    /// </summary>
    /// <param name="path">Model path selected by the user, drag-and-drop, or startup recovery.</param>
    private void LoadModelFromPath(string path, bool rememberAsLast = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".glb" && ext != ".gltf") return;
        try
        {
            SceneData scene = GltfSceneLoader.LoadFromFile(path);
            _renderer.LoadScene(scene); Text = $"FastViewDX12 - {Path.GetFileName(path)}";
            if (rememberAsLast)
            {
                Directory.CreateDirectory(_stateFolder);
                File.WriteAllText(LastModelPathFile, path);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"LoadModelFromPath failed: {ex}"); }
    }

    /// <summary>
    /// Loads the command-line model, or otherwise restores the last successfully opened model.
    /// </summary>
    private void TryLoadStartupModel()
    {
        string? startupPath =
            _startupModelPath;

        if (IsSupportedModelPath(
            startupPath))
        {
            LoadModelFromPath(
                startupPath!,
                true);

            return;
        }

        if (!File.Exists(
            LastModelPathFile))
        {
            return;
        }

        string lastPath =
            File.ReadAllText(
                LastModelPathFile)
            .Trim();

        if (IsSupportedModelPath(
            lastPath))
        {
            LoadModelFromPath(
                lastPath,
                false);
        }
    }

    /// <summary>
    /// Checks whether a path points to a GLB or glTF file.
    /// </summary>
    /// <param name="path">Candidate file path.</param>
    /// <returns>True when the extension is supported.</returns>
    private static bool IsSupportedModelPath(
    string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path))
        {
            return false;
        }

        string extension =
            Path.GetExtension(path);

        return
            extension.Equals(
                ".glb",
                StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(
                ".gltf",
                StringComparison.OrdinalIgnoreCase);
    }

}
