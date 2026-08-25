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

    private bool _sceneTransformsDirty;

    private sealed class TransformUndoEntry
    {
        public required ISceneTransformTarget Target { get; init; }

        public required string Description { get; init; }

        public required Vector3 Position { get; init; }

        public required Matrix4x4 LinearTransform { get; init; }
    }

    /// <summary>
    /// Begins one transform transaction. Repeated live updates then modify the
    /// same model without creating additional history entries.
    /// </summary>
    private void BeginTransformUndo(
        ISceneTransformTarget model,
        string description)
    {
        if (_pendingTransformUndo != null)
        {
            if (ReferenceEquals(
                    _pendingTransformUndo.Target,
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
                Target = model,
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
            !IsTransformTargetStillPresent(
                entry.Target) ||
            TransformStateMatches(
                entry.Target,
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

        _sceneTransformsDirty =
            true;

        SaveSceneSessionNow();
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

            if (!IsTransformTargetStillPresent(
                    entry.Target))
            {
                continue;
            }

            entry.Target.Position =
                entry.Position;

            entry.Target.SetLinearTransform(
                entry.LinearTransform);

            SelectTransformTarget(
                entry.Target);

            ApplySceneModelTransform();
            RefreshSceneSidebar();
            UpdateWindowTitle();
            UpdateMoveGizmoOverlay();

            _renderer.Render();
            _moveGizmoOverlayForm?.Update();
            _sceneTransformsDirty = true;
            SaveSceneSessionNow();

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
                _pendingTransformUndo.Target,
                model) ||
            _pendingTransformUndo?.Target is SceneMeshPart pendingPart &&
            ReferenceEquals(pendingPart.Parent, model))
        {
            _pendingTransformUndo =
                null;
        }

        _transformUndoHistory.RemoveAll(
            entry =>
                ReferenceEquals(entry.Target, model) ||
                entry.Target is SceneMeshPart part &&
                ReferenceEquals(part.Parent, model));
    }

    private bool IsTransformTargetStillPresent(
        ISceneTransformTarget target)
    {
        foreach (SceneModel currentModel in
                 _sceneDocument.Models)
        {
            if (ReferenceEquals(currentModel, target) ||
                target is SceneMeshPart part &&
                ReferenceEquals(part.Parent, currentModel) &&
                currentModel.Parts.Contains(part))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TransformStateMatches(
        ISceneTransformTarget model,
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

    private void SelectTransformTarget(ISceneTransformTarget target)
    {
        if (target is SceneMeshPart part)
        {
            _sceneDocument.SelectPart(part.Parent, part);
        }
        else if (target is SceneModel model)
        {
            _sceneDocument.Select(model);
        }
    }
}
