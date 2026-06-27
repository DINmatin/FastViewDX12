using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Model-path validation, scene replacement, additional loading, and last-file persistence.
public sealed partial class MainForm
{
    // BuildRenderScene flattens the models in document order. Remembering
    // each source mesh count lets viewport picking map a rendered triangle
    // back to the SceneModel that owns it.
    private readonly Dictionary<SceneModel, int> _sourceMeshCountByModel =
        new();

    /// <summary>
    /// Validates and loads a GLB or glTF file as the only model in the scene.
    /// </summary>
    /// <param name="path">Model path selected by the user, drag-and-drop, or startup recovery.</param>
    /// <param name="rememberAsLast">Whether the path becomes the next startup model.</param>
    private void LoadModelFromPath(
        string path,
        bool rememberAsLast = true)
    {
        LoadModelFromPath(
            path,
            addToScene: false,
            rememberAsLast: rememberAsLast);
    }

    /// <summary>
    /// Validates and appends a GLB or glTF file without removing existing models.
    /// </summary>
    private void AddModelFromPath(
        string path)
    {
        LoadModelFromPath(
            path,
            addToScene: true,
            rememberAsLast: false);
    }

    /// <summary>
    /// Opens a file dialog for either replacing the scene or appending one model.
    /// </summary>
    private void ChooseModelFile(
        bool addToScene)
    {
        using var dialog =
            new OpenFileDialog
            {
                Title =
                    addToScene
                        ? "Add model to scene"
                        : "Open model",

                Filter =
                    "glTF models (*.glb;*.gltf)|*.glb;*.gltf|" +
                    "GLB files (*.glb)|*.glb|" +
                    "glTF files (*.gltf)|*.gltf",

                CheckFileExists =
                    true,

                Multiselect =
                    false,

                RestoreDirectory =
                    true
            };

        if (dialog.ShowDialog(this) !=
            DialogResult.OK)
        {
            return;
        }

        if (addToScene)
        {
            AddModelFromPath(
                dialog.FileName);
        }
        else
        {
            LoadModelFromPath(
                dialog.FileName,
                true);
        }
    }

    private void LoadModelFromPath(
        string path,
        bool addToScene,
        bool rememberAsLast)
    {
        if (!IsSupportedModelPath(path))
        {
            return;
        }

        try
        {
            SceneData loadedScene =
                GltfSceneLoader.LoadFromFile(
                    path);

            if (addToScene)
            {
                _sceneDocument.Add(
                    path,
                    loadedScene);
            }
            else
            {
                ClearTransformUndoHistory();
                _sourceMeshCountByModel.Clear();

                _sceneDocument.ReplaceWith(
                    path,
                    loadedScene);
            }

            RememberLoadedModelMeshCount(
                loadedScene);

            RebuildRenderedScene();
            RefreshSceneSidebar();
            UpdateWindowTitle();

            if (rememberAsLast)
            {
                Directory.CreateDirectory(
                    _stateFolder);

                File.WriteAllText(
                    LastModelPathFile,
                    path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"LoadModelFromPath failed: {ex}");

            MessageBox.Show(
                this,
                ex.Message,
                addToScene
                    ? "Model could not be added"
                    : "Model could not be loaded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Rebuilds the flattened renderer scene after the document model list changes.
    /// </summary>
    private void RebuildRenderedScene(
        bool fitCamera = true)
    {
        _renderer.LoadScene(
            BuildEditorRenderScene(),
            fitCamera);
    }

    /// <summary>
    /// Rebuilds only mesh buffers after model transforms change. Material textures
    /// stay resident on the GPU so interactive gizmo drags remain responsive.
    /// </summary>
    private void UpdateRenderedSceneGeometry()
    {
        _renderer.UpdateSceneGeometry(
            BuildEditorRenderScene());
    }

    /// <summary>
    /// Removes the currently selected model and keeps the remaining scene intact.
    /// </summary>
    private void RemoveSelectedSceneModel()
    {
        SceneModel? selectedModel =
            _sceneDocument.SelectedModel;

        if (selectedModel == null)
        {
            return;
        }

        ForgetTransformUndoForModel(
            selectedModel);

        _sourceMeshCountByModel.Remove(
            selectedModel);

        _sceneDocument.Remove(
            selectedModel.Id);

        RebuildRenderedScene();
        RefreshSceneSidebar();
        UpdateWindowTitle();
    }

    private void RememberLoadedModelMeshCount(
        SceneData loadedScene)
    {
        if (_sceneDocument.Models.Count == 0)
        {
            return;
        }

        SceneModel loadedModel =
            _sceneDocument.Models[
                _sceneDocument.Models.Count - 1];

        _sourceMeshCountByModel[
            loadedModel] =
            loadedScene.Meshes.Count;
    }

    private void UpdateWindowTitle()
    {
        int modelCount =
            _sceneDocument.Models.Count;

        Text = modelCount switch
        {
            0 =>
                "FastViewDX12",

            1 =>
                $"FastViewDX12 - {_sceneDocument.Models[0].Name}",

            _ =>
                $"FastViewDX12 - {modelCount} models"
        };
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
    /// <returns>True when the path exists and its extension is supported.</returns>
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
