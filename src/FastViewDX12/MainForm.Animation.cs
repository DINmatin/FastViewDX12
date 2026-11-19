using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FastViewDX12;

public sealed partial class MainForm
{
    private bool _animationInspectorExpanded = true;
    private Label? _animationTimeLabel;
    private TrackBar? _animationProgress;

    private void UpdateSceneAnimations()
    {
        long now = _animationClock.ElapsedTicks;
        if (_lastAnimationTimestamp == 0)
        {
            _lastAnimationTimestamp = now;
            return;
        }

        double elapsed = (now - _lastAnimationTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed < 1.0 / 60.0)
        {
            return;
        }

        _lastAnimationTimestamp = now;
        float delta = (float)Math.Min(elapsed, 0.1);
        bool changed = false;

        foreach (SceneModel model in _sceneDocument.Models)
        {
            changed |= model.AdvanceAnimation(delta);
        }

        if (changed)
        {
            _renderer.UpdateAnimatedSceneGeometry(
                _sceneDocument.BuildRenderScene());

            UpdateAnimationInspectorReadout();
        }
    }

    private Control CreateAnimationInspector(SceneModel model, int width)
    {
        var content = CreateInspectorSectionContent(width);
        SceneAnimationClip selectedClip = model.SourceScene.Animations[
            Math.Clamp(model.AnimationIndex, 0, model.SourceScene.Animations.Count - 1)];

        var clipLabel = new Label
        {
            ForeColor = Color.Silver,
            Height = 22,
            Margin = new Padding(0, 5, 0, 0),
            Text = "Clip",
            TextAlign = ContentAlignment.MiddleLeft,
            Width = width - 20
        };
        content.Controls.Add(clipLabel);

        var clipSelector = new ComboBox
        {
            BackColor = Color.FromArgb(43, 43, 50),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gainsboro,
            Height = 28,
            Margin = new Padding(0, 0, 0, 4),
            Width = width - 20
        };
        foreach (SceneAnimationClip clip in model.SourceScene.Animations)
        {
            clipSelector.Items.Add(clip.Name);
        }
        clipSelector.SelectedIndex = model.AnimationIndex;
        clipSelector.SelectedIndexChanged += (_, _) =>
        {
            if (clipSelector.SelectedIndex < 0) return;
            model.AnimationIndex = clipSelector.SelectedIndex;
            model.SetAnimationTime(0.0f);
            model.AnimationPlaying = true;
            UpdateAnimatedGeometryNow();
            RefreshSceneSidebar();
        };
        content.Controls.Add(clipSelector);

        var transport = new TableLayoutPanel
        {
            ColumnCount = 4,
            Height = 34,
            Margin = Padding.Empty,
            RowCount = 1,
            Width = width - 20
        };
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        Button playButton = CreateInspectorChoiceButton(
            "Play",
            model.AnimationPlaying,
            () =>
            {
                if (!model.AnimationLooping && model.AnimationTime >= selectedClip.Duration)
                {
                    model.SetAnimationTime(0.0f);
                }
                model.AnimationPlaying = true;
                RefreshSceneSidebar();
            });
        transport.Controls.Add(playButton, 0, 0);
        transport.Controls.Add(CreateInspectorChoiceButton(
            "Pause",
            !model.AnimationPlaying,
            () =>
            {
                model.AnimationPlaying = false;
                RefreshSceneSidebar();
            }), 1, 0);
        transport.Controls.Add(CreateInspectorChoiceButton("Restart", false, () =>
        {
            model.SetAnimationTime(0.0f);
            model.AnimationPlaying = true;
            UpdateAnimatedGeometryNow();
            RefreshSceneSidebar();
        }), 2, 0);
        transport.Controls.Add(CreateInspectorChoiceButton(
            "Loop",
            model.AnimationLooping,
            () =>
            {
                model.AnimationLooping = !model.AnimationLooping;
                RefreshSceneSidebar();
            }), 3, 0);
        content.Controls.Add(transport);

        _animationProgress = new TrackBar
        {
            AutoSize = false,
            Height = 28,
            LargeChange = 100,
            Maximum = 1000,
            Margin = new Padding(0, 5, 0, 0),
            SmallChange = 10,
            TickStyle = TickStyle.None,
            Value = selectedClip.Duration > 0.0f
                ? Math.Clamp((int)(model.AnimationTime / selectedClip.Duration * 1000.0f), 0, 1000)
                : 0,
            Width = width - 20
        };
        _animationProgress.Scroll += (_, _) =>
        {
            model.SetAnimationTime(selectedClip.Duration * _animationProgress.Value / 1000.0f);
            UpdateAnimatedGeometryNow();
            UpdateAnimationInspectorReadout();
        };
        content.Controls.Add(_animationProgress);

        _animationTimeLabel = new Label
        {
            ForeColor = Color.Silver,
            Height = 22,
            Margin = Padding.Empty,
            Text = FormatAnimationTime(model, selectedClip),
            TextAlign = ContentAlignment.MiddleCenter,
            Width = width - 20
        };
        content.Controls.Add(_animationTimeLabel);

        return CreateInspectorSection(
            "Animation",
            width,
            _animationInspectorExpanded,
            () =>
            {
                _animationInspectorExpanded = !_animationInspectorExpanded;
                RefreshSceneSidebar();
            },
            content);
    }

    private void UpdateAnimatedGeometryNow()
    {
        _renderer.UpdateAnimatedSceneGeometry(_sceneDocument.BuildRenderScene());
    }

    private void UpdateAnimationInspectorReadout()
    {
        SceneModel? model = _sceneDocument.SelectedModel;
        if (model == null || !model.HasAnimations) return;
        SceneAnimationClip clip = model.SourceScene.Animations[
            Math.Clamp(model.AnimationIndex, 0, model.SourceScene.Animations.Count - 1)];

        if (_animationTimeLabel is { IsDisposed: false })
        {
            _animationTimeLabel.Text = FormatAnimationTime(model, clip);
        }
        if (_animationProgress is { IsDisposed: false } && clip.Duration > 0.0f)
        {
            int value = Math.Clamp((int)(model.AnimationTime / clip.Duration * 1000.0f), 0, 1000);
            if (_animationProgress.Value != value) _animationProgress.Value = value;
        }
    }

    private static string FormatAnimationTime(SceneModel model, SceneAnimationClip clip)
    {
        return $"{model.AnimationTime:0.00} / {clip.Duration:0.00} s";
    }
}
