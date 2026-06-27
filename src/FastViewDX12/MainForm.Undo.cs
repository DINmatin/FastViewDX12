using System;
using System.Collections.Generic;
using System.Numerics;

namespace FastViewDX12;

// Lightweight transform undo history. Each gizmo drag, inspector edit session,
// or reset operation contributes at most one Ctrl+Z step.
public sealed partial class MainForm
{
    private const int MaximumTransformUndoSteps =
        100;

    private readonly List<TransformUndoEntry> _transformUndoHistory =
        new();

    private TransformUndoEntry? _pendingTransformUndo;

    private sealed class TransformUndoEntry
    {
        public required SceneModel Model { get; init; }

        public required string Description { get; init; }

        public required Vector3 Position { get; init; }

        public required Matrix4x4 LinearTransform { get; init; }
    }

    /// <summary>
    /// Begins one transform transaction. Repeated live updates then modify the
    /// same model without creating additional history entries.
    /// </summary>
    private void BeginTransformUndo(
        SceneModel model,
        string description)
    {
        if (_pendingTransformUndo != null)
        {
            if (ReferenceEquals(
                    _pendingTransformUndo.Model,
                    model) &&
                string.Equals(
                    _pendingTransformUndo.Description,
                    description,
                    StringComparison.Ordinal))
            {
                return;
            }

            CommitTransformUndo();
        }

        _pendingTransformUndo =
            new TransformUndoEntry
            {
                Model = model,
                Description = description,
                Position = model.Position,
                LinearTransform = model.LinearTransform
            };
    }

    /// <summary>
    /// Commits the pending transaction only when the model really changed.
    /// </summary>
    private void CommitTransformUndo()
    {
        TransformUndoEntry? entry =
            _pendingTransformUndo;

        _pendingTransformUndo =
            null;

        if (entry == null ||
            !IsSceneModelStillPresent(
                entry.Model) ||
            TransformStateMatches(
                entry.Model,
                entry.Position,
                entry.LinearTransform))
        {
            return;
        }

        if (_transformUndoHistory.Count >=
            MaximumTransformUndoSteps)
        {
            _transformUndoHistory.RemoveAt(
                0);
        }

        _transformUndoHistory.Add(
            entry);
    }

    /// <summary>
    /// Restores the previous exact transform for the newest valid entry.
    /// </summary>
    private void UndoLastTransform()
    {
        // If Ctrl+Z is pressed while a NumericUpDown still has focus, first turn
        // its current edit session into one history item and then undo it.
        CommitTransformUndo();

        while (_transformUndoHistory.Count > 0)
        {
            int lastIndex =
                _transformUndoHistory.Count - 1;

            TransformUndoEntry entry =
                _transformUndoHistory[lastIndex];

            _transformUndoHistory.RemoveAt(
                lastIndex);

            if (!IsSceneModelStillPresent(
                    entry.Model))
            {
                continue;
            }

            entry.Model.Position =
                entry.Position;

            entry.Model.SetLinearTransform(
                entry.LinearTransform);

            _sceneDocument.Select(
                entry.Model);

            ApplySceneModelTransform();
            RefreshSceneSidebar();
            UpdateWindowTitle();
            UpdateMoveGizmoOverlay();

            _renderer.Render();
            _moveGizmoOverlayForm?.Update();

            return;
        }
    }

    private void ClearTransformUndoHistory()
    {
        _pendingTransformUndo =
            null;

        _transformUndoHistory.Clear();
    }

    private void ForgetTransformUndoForModel(
        SceneModel model)
    {
        if (_pendingTransformUndo != null &&
            ReferenceEquals(
                _pendingTransformUndo.Model,
                model))
        {
            _pendingTransformUndo =
                null;
        }

        _transformUndoHistory.RemoveAll(
            entry =>
                ReferenceEquals(
                    entry.Model,
                    model));
    }

    private bool IsSceneModelStillPresent(
        SceneModel model)
    {
        foreach (SceneModel currentModel in
                 _sceneDocument.Models)
        {
            if (ReferenceEquals(
                    currentModel,
                    model))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TransformStateMatches(
        SceneModel model,
        Vector3 position,
        Matrix4x4 linearTransform)
    {
        return
            Vector3.DistanceSquared(
                model.Position,
                position) <=
            0.0000000001f &&
            LinearTransformsNearlyEqual(
                model.LinearTransform,
                linearTransform);
    }
}
