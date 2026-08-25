using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace FastViewDX12;

// Autosaved scene state. This deliberately stores references to source model
// files rather than duplicating their geometry, like an unsaved editor session.
public sealed partial class MainForm
{
    private const int SceneSessionVersion =
        2;

    private bool _restoringSceneSession;

    private string SceneSessionFile =>
        Path.Combine(
            _stateFolder,
            "session.json");

    private sealed class SceneSessionState
    {
        public int Version { get; set; } =
            SceneSessionVersion;

        public int SelectedModelIndex { get; set; } =
            -1;

        public int SelectedPartIndex { get; set; } =
            -1;

        public bool HasUnexportedTransformChanges { get; set; }

        public List<SceneSessionModelState> Models { get; set; } =
            new();
    }

    private sealed class SceneSessionModelState
    {
        public string Path { get; set; } =
            string.Empty;

        public string Name { get; set; } =
            string.Empty;

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float M11 { get; set; } = 1.0f;
        public float M12 { get; set; }
        public float M13 { get; set; }
        public float M21 { get; set; }
        public float M22 { get; set; } = 1.0f;
        public float M23 { get; set; }
        public float M31 { get; set; }
        public float M32 { get; set; }
        public float M33 { get; set; } = 1.0f;

        public List<SceneSessionPartState> Parts { get; set; } =
            new();
    }

    private sealed class SceneSessionPartState
    {
        public int SourceMeshIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float M11 { get; set; } = 1.0f;
        public float M12 { get; set; }
        public float M13 { get; set; }
        public float M21 { get; set; }
        public float M22 { get; set; } = 1.0f;
        public float M23 { get; set; }
        public float M31 { get; set; }
        public float M32 { get; set; }
        public float M33 { get; set; } = 1.0f;
    }

    /// <summary>
    /// Restores every available source model in scene order. Missing or invalid
    /// files are skipped so one broken path does not prevent the rest reopening.
    /// </summary>
    private async Task<bool> TryRestoreSceneSessionAsync()
    {
        if (!File.Exists(SceneSessionFile))
        {
            return false;
        }

        SceneSessionState? state;

        try
        {
            string json =
                await File.ReadAllTextAsync(
                    SceneSessionFile);

            state =
                JsonSerializer.Deserialize<SceneSessionState>(
                    json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Scene session could not be read: {ex}");

            return false;
        }

        if (state == null ||
            state.Version < 1 ||
            state.Version > SceneSessionVersion ||
            state.Models == null)
        {
            return false;
        }

        _restoringSceneSession =
            true;

        _sceneTransformsDirty =
            state.HasUnexportedTransformChanges;

        SceneModel? selectedModel =
            null;

        SceneMeshPart? selectedPart =
            null;

        try
        {
            bool addToScene =
                false;

            for (int index = 0;
                 index < state.Models.Count;
                 index++)
            {
                SceneSessionModelState savedModel =
                    state.Models[index];

                if (!IsSupportedModelPath(savedModel.Path))
                {
                    continue;
                }

                SceneModel? model =
                    await LoadModelFromPathAsync(
                        savedModel.Path,
                        addToScene,
                        rememberAsLast: false);

                if (model == null)
                {
                    continue;
                }

                addToScene =
                    true;

                RestoreSceneModelState(
                    model,
                    savedModel);

                foreach (SceneSessionPartState savedPart in savedModel.Parts)
                {
                    if ((uint)savedPart.SourceMeshIndex >=
                        (uint)model.Parts.Count)
                    {
                        continue;
                    }

                    RestoreScenePartState(
                        model.Parts[savedPart.SourceMeshIndex],
                        savedPart);
                }

                if (index == state.SelectedModelIndex)
                {
                    selectedModel =
                        model;

                    if ((uint)state.SelectedPartIndex <
                        (uint)model.Parts.Count)
                    {
                        selectedPart =
                            model.Parts[state.SelectedPartIndex];
                    }
                }
            }

            if (_sceneDocument.Models.Count > 0)
            {
                if (selectedModel != null &&
                    selectedPart != null)
                {
                    _sceneDocument.SelectPart(
                        selectedModel,
                        selectedPart);
                }
                else
                {
                    _sceneDocument.Select(
                        selectedModel ??
                        _sceneDocument.Models[0]);
                }

                RebuildRenderedScene();
                RefreshSceneSidebar();
                UpdateWindowTitle();
            }
        }
        finally
        {
            _restoringSceneSession =
                false;
        }

        // Persist the cleaned session after skipping paths that no longer exist.
        SaveSceneSessionNow();
        return true;
    }

    private static void RestoreSceneModelState(
        SceneModel model,
        SceneSessionModelState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Name))
        {
            model.Name =
                state.Name;
        }

        model.Position =
            AllFinite(
                state.PositionX,
                state.PositionY,
                state.PositionZ)
                ? new Vector3(
                    state.PositionX,
                    state.PositionY,
                    state.PositionZ)
                : Vector3.Zero;

        Matrix4x4 linearTransform =
            AllFinite(
                state.M11,
                state.M12,
                state.M13,
                state.M21,
                state.M22,
                state.M23,
                state.M31,
                state.M32,
                state.M33)
                ? new Matrix4x4(
                    state.M11, state.M12, state.M13, 0.0f,
                    state.M21, state.M22, state.M23, 0.0f,
                    state.M31, state.M32, state.M33, 0.0f,
                    0.0f, 0.0f, 0.0f, 1.0f)
                : Matrix4x4.Identity;

        model.SetLinearTransform(
            linearTransform);
    }

    private static void RestoreScenePartState(
        SceneMeshPart part,
        SceneSessionPartState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Name))
        {
            part.Name = state.Name;
        }

        part.Position = AllFinite(
            state.PositionX,
            state.PositionY,
            state.PositionZ)
            ? new Vector3(state.PositionX, state.PositionY, state.PositionZ)
            : Vector3.Zero;

        Matrix4x4 linearTransform = AllFinite(
            state.M11, state.M12, state.M13,
            state.M21, state.M22, state.M23,
            state.M31, state.M32, state.M33)
            ? new Matrix4x4(
                state.M11, state.M12, state.M13, 0.0f,
                state.M21, state.M22, state.M23, 0.0f,
                state.M31, state.M32, state.M33, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f)
            : Matrix4x4.Identity;

        part.SetLinearTransform(linearTransform);
    }

    private void SaveSceneSessionNow()
    {
        if (_restoringSceneSession)
        {
            return;
        }

        try
        {
            var state =
                new SceneSessionState
                {
                    HasUnexportedTransformChanges =
                        _sceneTransformsDirty
                };

            for (int index = 0;
                 index < _sceneDocument.Models.Count;
                 index++)
            {
                SceneModel model =
                    _sceneDocument.Models[index];

                if (ReferenceEquals(
                        model,
                        _sceneDocument.SelectedModel))
                {
                    state.SelectedModelIndex =
                        index;

                    if (_sceneDocument.SelectedPart != null)
                    {
                        state.SelectedPartIndex =
                            model.Parts.IndexOf(
                                _sceneDocument.SelectedPart);
                    }
                }

                Matrix4x4 transform =
                    model.LinearTransform;

                var savedModel =
                    new SceneSessionModelState
                    {
                        Path = model.SourcePath,
                        Name = model.Name,
                        PositionX = model.Position.X,
                        PositionY = model.Position.Y,
                        PositionZ = model.Position.Z,
                        M11 = transform.M11,
                        M12 = transform.M12,
                        M13 = transform.M13,
                        M21 = transform.M21,
                        M22 = transform.M22,
                        M23 = transform.M23,
                        M31 = transform.M31,
                        M32 = transform.M32,
                        M33 = transform.M33
                    };

                foreach (SceneMeshPart part in model.Parts)
                {
                    Matrix4x4 partTransform = part.LinearTransform;
                    savedModel.Parts.Add(
                        new SceneSessionPartState
                        {
                            SourceMeshIndex = part.SourceMeshIndex,
                            Name = part.Name,
                            PositionX = part.Position.X,
                            PositionY = part.Position.Y,
                            PositionZ = part.Position.Z,
                            M11 = partTransform.M11,
                            M12 = partTransform.M12,
                            M13 = partTransform.M13,
                            M21 = partTransform.M21,
                            M22 = partTransform.M22,
                            M23 = partTransform.M23,
                            M31 = partTransform.M31,
                            M32 = partTransform.M32,
                            M33 = partTransform.M33
                        });
                }

                state.Models.Add(savedModel);
            }

            Directory.CreateDirectory(
                _stateFolder);

            string json =
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            string temporaryPath =
                SceneSessionFile +
                ".tmp";

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                SceneSessionFile,
                overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Scene session could not be saved: {ex}");
        }
    }

    private static bool AllFinite(params float[] values)
    {
        foreach (float value in values)
        {
            if (!float.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }
}
