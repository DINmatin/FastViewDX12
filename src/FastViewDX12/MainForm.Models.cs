using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace FastViewDX12;

// Model-path validation, scene replacement, additional loading, and last-file persistence.
public sealed partial class MainForm
{
    /// <summary>
    /// Validates and loads a GLB or glTF file as the only model in the scene.
    /// </summary>
    /// <param name="path">Model path selected by the user, drag-and-drop, or startup recovery.</param>
    /// <param name="rememberAsLast">Whether the path becomes the next startup model.</param>
    private async void ReplaceSceneFromPath(
        string path,
        bool rememberAsLast = true)
    {
        await LoadModelFromPathAsync(
            path,
            addToScene: false,
            rememberAsLast: rememberAsLast);
    }

    /// <summary>
    /// Validates and appends a GLB or glTF file without removing existing models.
    /// </summary>
    private async void AddModelFromPath(
        string path)
    {
        await LoadModelFromPathAsync(
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
                    "Supported models (*.glb;*.gltf;*.fbx;*.obj)|*.glb;*.gltf;*.fbx;*.obj|" +
                    "glTF models (*.glb;*.gltf)|*.glb;*.gltf|" +
                    "GLB files (*.glb)|*.glb|" +
                    "glTF files (*.gltf)|*.gltf|" +
                    "Autodesk FBX (*.fbx)|*.fbx|" +
                    "Wavefront OBJ (*.obj)|*.obj",

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
            ReplaceSceneFromPath(
                dialog.FileName,
                true);
        }
    }

    private async Task<SceneModel?> LoadModelFromPathAsync(
        string path,
        bool addToScene,
        bool rememberAsLast)
    {
        if (!IsSupportedModelPath(path) ||
            _isModelLoadInProgress ||
            _isVideoExportInProgress)
        {
            return null;
        }

        CancelTextureRefinement();

        string fileName = Path.GetFileName(path);
        _isModelLoadInProgress = true;
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;
        var performance = new ModelLoadPerformanceReport(path);
        SceneData? loadedScene = null;
        SceneModel? loadedModel = null;
        bool loadCompleted = false;

        _menuStrip.Enabled = false;
        _progressOverlay.BeginOperation("LOADING MODEL", fileName);
        _dropOverlay.HideFeedback();

        try
        {
            var progress = new Progress<ModelLoadProgress>(
                value => _progressOverlay.UpdateProgress(value));

            loadedScene = await Task.Run(
                () => ModelSceneLoader.LoadFromFile(
                    path,
                    progress,
                    performance,
                    cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _progressOverlay.UpdateProgress(
                new ModelLoadProgress(
                    ModelLoadStage.UploadingToGpu,
                    "Uploading scene to GPU",
                    fileName));

            _renderer.MaximumDecodedTextureSize =
                InitialTextureMaximumDimension;

            if (addToScene)
            {
                loadedModel = _sceneDocument.Add(
                    path,
                    loadedScene);
            }
            else
            {
                ClearTransformUndoHistory();
                loadedModel = _sceneDocument.ReplaceWith(
                    path,
                    loadedScene);
            }

            RebuildRenderedScene();
            RefreshSceneSidebar();
            UpdateWindowTitle();

            if (rememberAsLast)
            {
                Directory.CreateDirectory(
                    _stateFolder);

                await File.WriteAllTextAsync(
                    LastModelPathFile,
                    path,
                    CancellationToken.None);
            }

            loadCompleted = true;

            if (!_restoringSceneSession)
            {
                SaveSceneSessionNow();
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Model load cancelled: {path}");
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
        finally
        {
            performance.Publish();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            _isModelLoadInProgress = false;

            if (!IsDisposed)
            {
                _progressOverlay.EndOperation();
                _menuStrip.Enabled = true;
                _renderer.Resize();
            }

            if (loadCompleted &&
                loadedScene != null &&
                !IsDisposed)
            {
                StartTextureRefinement(fileName);
            }

            CloseWhenIdleIfRequested();
        }

        return loadCompleted
            ? loadedModel
            : null;
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

        _sceneDocument.Remove(
            selectedModel.Id);

        RebuildRenderedScene();
        if (_sceneDocument.Models.Count > 0)
        {
            StartTextureRefinement(_sceneDocument.Models[0].Name);
        }
        RefreshSceneSidebar();
        UpdateWindowTitle();
        SaveSceneSessionNow();
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
    private async Task RestoreStartupSceneAsync()
    {
        string? startupPath =
            _startupModelPath;

        if (IsSupportedModelPath(
            startupPath))
        {
            await LoadModelFromPathAsync(
                startupPath!,
                addToScene: false,
                rememberAsLast: true);

            return;
        }

        if (await TryRestoreSceneSessionAsync())
        {
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
            await LoadModelFromPathAsync(
                lastPath,
                addToScene: false,
                rememberAsLast: false);
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

        return ModelSceneLoader.IsSupportedPath(path);
    }
}
