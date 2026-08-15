using System.Diagnostics;
using System.Runtime;

namespace FastViewDX12;

// Progressive texture refinement, video export, cancellation, and shutdown coordination.
public sealed partial class MainForm
{
    private const int InitialTextureMaximumDimension = 512;

    private readonly record struct TextureRefinementTarget(
        MaterialData Material,
        MaterialTextureKind Kind,
        byte[] EncodedBytes,
        Size Dimensions);

    private void StartTextureRefinement(string fileName)
    {
        CancelTextureRefinement();

        var cancellation = new CancellationTokenSource();
        _textureRefinementCancellation = cancellation;
        _textureRefinementTask = RefineTexturesAsync(fileName, cancellation);
    }

    private async Task RefineTexturesAsync(
        string fileName,
        CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;

        try
        {
            List<TextureRefinementTarget> targets = await Task.Run(
                () => BuildTextureRefinementTargets(token),
                token);

            for (int index = 0; index < targets.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                TextureRefinementTarget target = targets[index];

                if (Math.Max(target.Dimensions.Width, target.Dimensions.Height) <=
                    InitialTextureMaximumDimension)
                {
                    continue;
                }

                DecodedTexture decoded = await Task.Run(
                    () => TextureDecoder.Decode(
                        target.EncodedBytes,
                        maximumDimension: null),
                    token);

                token.ThrowIfCancellationRequested();

                if (!_renderer.ReplaceMaterialTexture(
                        target.Material,
                        target.Kind,
                        decoded))
                {
                    return;
                }

                Text =
                    $"FastViewDX12 - {fileName} " +
                    $"(refining textures {index + 1}/{targets.Count})";

                await Task.Yield();
            }

            UpdateWindowTitle();
            CompactTextureDecodeMemory();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Texture refinement cancelled: {fileName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Texture refinement failed for '{fileName}': {ex}");
            if (!IsDisposed)
            {
                UpdateWindowTitle();
            }
        }
        finally
        {
            if (ReferenceEquals(_textureRefinementCancellation, cancellation))
            {
                _textureRefinementCancellation = null;
                _textureRefinementTask = null;
            }

            cancellation.Dispose();
        }
    }

    private List<TextureRefinementTarget> BuildTextureRefinementTargets(
        CancellationToken token)
    {
        var targets = new List<TextureRefinementTarget>();

        foreach (SceneModel model in _sceneDocument.Models)
        {
            foreach (MaterialData material in model.SourceScene.Materials)
            {
                AddTextureTarget(targets, material, MaterialTextureKind.BaseColor,
                    material.BaseColorTextureBytes, token);
                AddTextureTarget(targets, material, MaterialTextureKind.Normal,
                    material.NormalTextureBytes, token);
                AddTextureTarget(targets, material, MaterialTextureKind.MetallicRoughness,
                    material.MetallicRoughnessTextureBytes, token);
                AddTextureTarget(targets, material, MaterialTextureKind.Emissive,
                    material.EmissiveTextureBytes, token);
            }
        }

        return targets;
    }

    private static void AddTextureTarget(
        List<TextureRefinementTarget> targets,
        MaterialData material,
        MaterialTextureKind kind,
        byte[]? encodedBytes,
        CancellationToken token)
    {
        if (encodedBytes is not { Length: > 0 })
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        Size dimensions = TextureDecoder.GetDimensions(encodedBytes);
        targets.Add(new TextureRefinementTarget(
            material,
            kind,
            encodedBytes,
            dimensions));
    }

    private void CancelTextureRefinement()
    {
        _textureRefinementCancellation?.Cancel();
    }

    private static void CompactTextureDecodeMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode =
            GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Aggressive,
            blocking: true,
            compacting: true);
    }

    private async Task CreatePreviewVideoAsync()
    {
        if (_isModelLoadInProgress ||
            _isVideoExportInProgress ||
            _sceneDocument.Models.Count == 0)
        {
            return;
        }

        SceneModel sourceModel =
            _sceneDocument.SelectedModel ??
            _sceneDocument.Models[0];

        string modelPath = sourceModel.SourcePath;
        string outputPath = Path.Combine(
            Path.GetDirectoryName(modelPath) ?? AppContext.BaseDirectory,
            Path.GetFileNameWithoutExtension(modelPath) + "_preview.mp4");

        if (File.Exists(outputPath) &&
            MessageBox.Show(
                this,
                $"The preview video already exists:\n\n{outputPath}\n\nOverwrite it?",
                "Overwrite preview video?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _isVideoExportInProgress = true;
        _videoExportCancellation = new CancellationTokenSource();
        CancellationToken token = _videoExportCancellation.Token;
        bool completed = false;

        _menuStrip.Enabled = false;
        _progressOverlay.BeginOperation(
            "CREATING PREVIEW VIDEO",
            Path.GetFileName(outputPath));

        try
        {
            if (_textureRefinementTask != null)
            {
                _progressOverlay.UpdateProgress(
                    new ModelLoadProgress(
                        ModelLoadStage.DecodingTextures,
                        "Preparing detailed textures",
                        Path.GetFileName(modelPath)));

                try
                {
                    await _textureRefinementTask.WaitAsync(token);
                }
                catch (OperationCanceledException)
                    when (!token.IsCancellationRequested)
                {
                }
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var progress = new Progress<ModelLoadProgress>(
                value => _progressOverlay.UpdateProgress(value));

            await PreviewVideoExporter.ExportAsync(
                _renderer,
                outputPath,
                1920,
                1080,
                30,
                8,
                progress,
                token);

            completed = true;
            MessageBox.Show(
                this,
                $"Preview video created:\n\n{outputPath}",
                "Preview video complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            TryDeleteIncompleteVideo(outputPath);
        }
        catch (Exception ex)
        {
            TryDeleteIncompleteVideo(outputPath);
            if (!_closeWhenIdle && !IsDisposed)
            {
                MessageBox.Show(
                    this,
                    $"The preview video could not be created.\n\n{ex.Message}",
                    "Preview video failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _progressOverlay.EndOperation();
                _menuStrip.Enabled = true;
            }

            _videoExportCancellation?.Dispose();
            _videoExportCancellation = null;
            _isVideoExportInProgress = false;

            if (!completed)
            {
                TryDeleteIncompleteVideo(outputPath);
            }

            CloseWhenIdleIfRequested();
        }
    }

    private static void TryDeleteIncompleteVideo(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        CancelTextureRefinement();

        if (!_isModelLoadInProgress && !_isVideoExportInProgress)
        {
            return;
        }

        e.Cancel = true;
        _closeWhenIdle = true;
        _loadCancellation?.Cancel();
        _videoExportCancellation?.Cancel();
    }

    private void CloseWhenIdleIfRequested()
    {
        if (_closeWhenIdle &&
            !_isModelLoadInProgress &&
            !_isVideoExportInProgress &&
            !IsDisposed)
        {
            BeginInvoke(new Action(Close));
        }
    }
}
