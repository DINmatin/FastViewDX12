namespace FastViewDX12;

public static class ModelSceneLoader
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".glb",
            ".gltf",
            ".fbx",
            ".obj"
        };

    public static bool IsSupportedPath(string? path)
    {
        return
            !string.IsNullOrWhiteSpace(path) &&
            SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static SceneData LoadFromFile(
        string path,
        IProgress<ModelLoadProgress>? progress = null,
        ModelLoadPerformanceReport? performance = null,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(path);

        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            return AssimpFbxSceneLoader.LoadFromFile(
                path,
                progress,
                performance,
                cancellationToken);
        }

        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            return ObjSceneLoader.LoadFromFile(
                path,
                progress,
                performance,
                cancellationToken);
        }

        if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(
                new ModelLoadProgress(
                    ModelLoadStage.ParsingScene,
                    "Parsing glTF scene",
                    Path.GetFileName(path)));

            using (performance?.Measure("Parsing glTF scene"))
            {
                return GltfSceneLoader.LoadFromFile(path);
            }
        }

        throw new NotSupportedException(
            $"The model format '{extension}' is not supported.");
    }
}
