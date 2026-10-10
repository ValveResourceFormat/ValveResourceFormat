using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.GLViewers
{
    partial class GLModelViewer
    {
        // Time scale, pause and single step under the parameters of whichever graph plays
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The panel adds the rows to its Controls, which dispose them")]
        private void AddGraphPlaybackControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(animationController != null);

            var controller = animationController;

            graphTimeScaleSlider = RendererControl.CreateRowSlider();
            graphTimeScaleSlider.Value = controller.FrametimeMultiplier;
            graphTimeScaleSlider.ValueChanged = timeScale => controller.FrametimeMultiplier = timeScale;

            graphPlayImage = AppIcons.ImageList.Images[AppIcons.Icons["AudioPlay"]];
            graphPauseImage = AppIcons.ImageList.Images[AppIcons.Icons["AudioPause"]];

            // The rewind arrows, pointing forwards
            graphTickImage = AppIcons.ImageList.Images[AppIcons.Icons["AudioRewindLeft"]];
            graphTickImage.RotateFlip(RotateFlipType.RotateNoneFlipX);

            graphPauseButton = RendererControl.CreateRowButton(string.Empty, () => SetGraphPaused(!controller.IsPaused));
            graphPauseButton.Image = graphPauseImage;

            var tickButton = RendererControl.CreateRowButton(string.Empty);
            tickButton.Image = graphTickImage;

            RendererControl.RepeatWhileHeld(tickButton, () =>
            {
                SetGraphPaused(true);
                controller.StepGraph(GraphTickTime);
            });

            // Icon buttons, larger than the text buttons of the parameter rows
            var buttonSize = new Size(UiControl.AdjustForDPI(40), UiControl.AdjustForDPI(30));
            graphPauseButton.Size = buttonSize;
            tickButton.Size = buttonSize;

            var playbackRow = new PropertyRow(null, [graphTimeScaleSlider], [graphPauseButton, tickButton]);
            playbackRow.Height = buttonSize.Height + UiControl.AdjustForDPI(6);

            var panel = new Panel
            {
                Margin = Padding.Empty,
                Visible = false,
            };

            graphEventList = new EventListControl(GraphEventListLines)
            {
                EmptyText = "No events",
            };

            Control[] rows =
            [
                RendererControl.CreateSectionHeader("System"),
                playbackRow,
                RendererControl.CreateSectionHeader("Events"),
                graphEventList,
            ];

            foreach (var row in rows)
            {
                row.Dock = DockStyle.Top;
                panel.Controls.Add(row);
                row.BringToFront();
            }

            graphPlaybackControls = panel;
            UiControl.AddControl(panel);
            Themer.ThemeControl(panel);
            panel.Height = panel.PreferredSize.Height;
        }

        private void SetGraphPaused(bool paused)
        {
            Debug.Assert(animationController != null);

            animationController.IsPaused = paused;
            graphPauseButton?.Image = paused ? graphPlayImage : graphPauseImage;
        }

        /// <summary>One of the model's animation graphs, with the parameter controls built for it.</summary>
        private sealed class GraphSession(string displayName, AnimationGraph graph)
        {
            public string DisplayName { get; } = displayName;
            public AnimationGraph Graph { get; } = graph;
            public Panel? Controls { get; set; }
            public List<GraphGizmoBinding> GizmoBindings { get; } = [];
        }

        private GraphSession? LoadGraphSession(string identifier, string path)
        {
            Debug.Assert(modelSceneNode != null);

            if (modelSceneNode.LoadAnimationGraph(path) is not { } graph)
            {
                Log.Warn(nameof(GLModelViewer), $"Failed to load animation graph {path}");
                return null;
            }

            ApplyStartingParameters(graph);

            var displayName = string.IsNullOrEmpty(identifier) ? Path.GetFileNameWithoutExtension(path) : identifier;
            return new GraphSession(displayName, graph);
        }

        private void BuildGraphControls(GraphSession session)
        {
            Debug.Assert(graphControlsHost != null);

            var controls = CreateAnimGraphControls(session.Graph, session.GizmoBindings);
            controls.Width = graphControlsHost.ClientSize.Width;
            controls.Height = controls.PreferredSize.Height;
            controls.AutoSize = false;

            graphControlsHost.Controls.Add(controls);
            Themer.ThemeControl(controls);
            session.Controls = controls;
        }

        private void ShowGraphControls(GraphSession? session)
        {
            Debug.Assert(UiControl != null);

            // Every visibility change below relayouts the sidebar, so they are applied in one pass
            var sidebar = graphControlsHost?.Parent;
            sidebar?.SuspendLayout();

            var showGraph = session?.Controls != null;

            if (graphControlsHost != null)
            {
                session?.Controls?.BringToFront();
                graphControlsHost.Height = session?.Controls?.Height ?? 0;
            }

            // Playback controls only apply to clips
            animationTimeLabel?.Visible = !showGraph;
            animationTrackBar?.Visible = !showGraph;
            animationPlayPause?.Parent?.Visible = !showGraph;
            slowmodeTrackBar?.Visible = !showGraph;
            rootMotionCheckBox?.Parent?.Visible = !showGraph;
            additiveCheckBox?.Parent?.Visible = !showGraph;

            graphPlaybackControls?.Visible = showGraph;

            // Clips and graphs have their own pause and speed controls, and neither inherits those of the other
            if ((showGraph || graphControlsShown) && animationController != null)
            {
                animationPlayPause?.Checked = true;
                SetGraphPaused(false);
                animationController.FrametimeMultiplier = 1f;
                slowmodeTrackBar?.Slider.Value = 1f;
                graphTimeScaleSlider?.Value = 1f;
            }

            graphControlsShown = showGraph;

            sidebar?.ResumeLayout();
        }

        private const string UnsetIdText = "(none)";

        private static (Control Editor, Control[] Trailing) CreateFloatParameterEditor(AnimationGraph graph, string paramName)
        {
            var value = graph.FloatParameters[paramName];
            void SetValue(float newValue) => graph.FloatParameters[paramName] = newValue;

            var range = graph.GetFloatParameterRange(paramName) ?? GuessFloatParameterRange(paramName);

            if (range is not { } knownRange)
            {
                return (RendererControl.CreateFloatField(value, 3, SetValue), []);
            }

            if (knownRange.IsDiscrete)
            {
                var options = Enumerable.Range((int)knownRange.Min, (int)(knownRange.Max - knownRange.Min) + 1)
                    .Select(static option => option.ToString(CultureInfo.InvariantCulture));

                return (RendererControl.CreateRowComboBox(options, ((int)value).ToString(CultureInfo.InvariantCulture),
                    option => SetValue(float.Parse(option, CultureInfo.InvariantCulture))), []);
            }

            var (slider, field) = RendererControl.CreateRangedFloatEditor(value, knownRange.Min, knownRange.Max, knownRange.IsWholeNumber, SetValue);
            return (slider, [field]);
        }

        // Starts the unset IDs that are always set at runtime, so a picked graph shows a standing character rather than
        // nothing: an idle state or action, ground movement, and a direction
        private static void ApplyStartingParameters(AnimationGraph graph)
        {
            foreach (var paramName in graph.IdParameters.Keys.ToArray())
            {
                if (graph.IdParameters[paramName].Length == 0 && GuessStartingId(paramName, [.. graph.GetParameterIdOptions(paramName)]) is { } id)
                {
                    graph.IdParameters[paramName] = id;
                }
            }
        }

        private static string? GuessStartingId(string paramName, string[] options)
        {
            // Only the parameter's own idle: "rope_climb_idle" is an action like any other
            var idle = Array.Find(options, option => option == "idle" || option == $"{paramName}_idle");
            if (idle != null)
            {
                return idle;
            }

            if (paramName.Contains("type", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Find(options, static option => option.EndsWith("_ground", StringComparison.Ordinal))
                    ?? Array.Find(options, static option => option == "default");
            }

            if (paramName.Contains("direction", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Find(options, static option => option == "W");
            }

            return null;
        }

        // For parameters only read by code the graph does not describe, such as aim nodes
        private static FloatParameterRange? GuessFloatParameterRange(string paramName)
        {
            if (paramName.Contains("angle", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(-180f, 180f, IsWholeNumber: false, IsDiscrete: false);
            }

            if (paramName.Contains("amount", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(0f, 1f, IsWholeNumber: false, IsDiscrete: false);
            }

            return null;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each row adds its editors to its Controls, which dispose them")]
        private static Panel CreateAnimGraphControls(AnimationGraph graph, List<GraphGizmoBinding> gizmoBindings)
        {
            var panel = new Panel
            {
                AutoSize = true,
                Margin = Padding.Empty,
            };

            panel.SuspendLayout();

            void Add(Control control)
            {
                control.Dock = DockStyle.Top;
                panel.Controls.Add(control);
                control.BringToFront();
            }

            void AddSection(string title, int count)
            {
                if (count > 0)
                {
                    Add(RendererControl.CreateSectionHeader(title));
                }
            }

            static IOrderedEnumerable<string> Sorted<T>(Dictionary<string, T> parameters)
                => parameters.Keys.Order(StringComparer.OrdinalIgnoreCase);

            AddSection("Values", graph.FloatParameters.Count);

            foreach (var paramName in Sorted(graph.FloatParameters))
            {
                var (editor, trailing) = CreateFloatParameterEditor(graph, paramName);
                Add(RendererControl.CreatePropertyRow(paramName, editor, trailing));
            }

            AddSection("Identifiers", graph.IdParameters.Count);

            foreach (var paramName in Sorted(graph.IdParameters))
            {
                // An empty ID is unset, which is how every ID parameter starts
                var options = graph.GetParameterIdOptions(paramName).Order(StringComparer.Ordinal).Prepend(UnsetIdText);
                var value = graph.IdParameters[paramName];

                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowComboBox(options, value.Length == 0 ? UnsetIdText : value, id =>
                    {
                        graph.IdParameters[paramName] = id == UnsetIdText ? string.Empty : id;
                    })));
            }

            AddSection("Flags", graph.BoolParameters.Count);

            foreach (var paramName in Sorted(graph.BoolParameters))
            {
                // A held value is already true, so a pulse could not change it
                var signalButton = RendererControl.CreateRowButton("Signal", () => graph.SignalBoolParameter(paramName));
                signalButton.Enabled = !graph.BoolParameters[paramName];

                var checkBox = RendererControl.CreateRowCheckBox(graph.BoolParameters[paramName], isChecked =>
                {
                    graph.BoolParameters[paramName] = isChecked;
                    signalButton.Enabled = !isChecked;
                }, paramName);

                Add(RendererControl.CreateCheckBoxRow(checkBox, signalButton));
            }

            AddSection("Vectors", graph.VectorParameters.Count);

            foreach (var paramName in Sorted(graph.VectorParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: false);
                var value = graph.VectorParameters[paramName];

                // Only look-at targets report a hint, so only they show a gizmo
                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    RendererControl.CreateRowButton("Reset", () => binding.ResetPending = true)));

                var (row, fields) = RendererControl.CreateVectorRow(new Vector3(value.X, value.Y, value.Z), vector =>
                {
                    graph.VectorParameters[paramName] = new Vector4(vector, 0f);
                    binding.Initialized = true;
                });

                binding.Fields = fields;
                Add(row);
                gizmoBindings.Add(binding);
            }

            AddSection("Targets", graph.TargetParameters.Count);

            foreach (var paramName in Sorted(graph.TargetParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: true);

                // An unset target leaves the IK off and the gizmo on the animated bone
                Add(RendererControl.CreatePropertyRow(paramName,
                    RendererControl.CreateRowCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    RendererControl.CreateRowButton("Reset", () => graph.TargetParameters[paramName] = null)));

                gizmoBindings.Add(binding);
            }

            panel.ResumeLayout(false);
            return panel;
        }

        private sealed class GraphGizmoBinding(string parameterName, bool isTarget)
        {
            public string ParameterName { get; } = parameterName;
            public bool IsTarget { get; } = isTarget;
            public TransformGizmos.Gizmo Gizmo { get; } = new(parameterName, allowRotation: isTarget);
            public ThemedFloatNumeric[]? Fields { get; set; }
            public bool IsWorldSpace { get; set; }
            public volatile bool Initialized;
            public volatile bool ResetPending;
        }

        // Swapped with the active graph under the GL lock, synced into the gizmos on the render thread
        private List<GraphGizmoBinding> graphGizmoBindings = [];
        private List<GraphGizmoBinding>? shownGizmoBindings;
        private TransformGizmos? graphGizmos;

        // Written on the UI thread, read by the render loop
        private long gizmoMousePosition;
        private volatile bool gizmoPressPending;
        private volatile bool gizmoDragging;

        private ReadOnlyMemory<char> FormatGraphDebugText(AnimationGraph graph)
        {
            var capacity = GraphDebugTextCapacity + modelStatsText.Length;

            if (graphDebugText.Length < capacity)
            {
                graphDebugText = new char[capacity];
            }

            var text = graphDebugText.AsSpan(0, GraphDebugTextCapacity);
            var length = 0;
            int written;

            text.TryWrite($"AnimGraph2\n", out written);
            length += written;

            // Inclusive times of the graph and the graphs it references, indented by depth
            foreach (var timing in graph.GraphTimings)
            {
                var indent = Spaces.AsSpan(0, Math.Min(timing.Depth * 2, Spaces.Length));
                text[length..].TryWrite(CultureInfo.InvariantCulture, $"{indent}{timing.GraphName,-48} {timing.Duration.TotalMicroseconds,8:0.0} us\n", out written);
                length += written;
            }

            text[length..].TryWrite($"\n", out written);
            length += written;

            foreach (var clip in graph.SampledClips)
            {
                text[length..].TryWrite(CultureInfo.InvariantCulture, $"{clip.Name,-80} {clip.Time,6:0.00} / {clip.Duration:0.00}\n", out written);
                length += written;
            }

            text[length..].TryWrite($"\n", out written);
            length += written;

            modelStatsText.CopyTo(graphDebugText.AsSpan(length));
            return graphDebugText.AsMemory(0, length + modelStatsText.Length);
        }

        private const float GraphEventLingerSeconds = 1.5f;

        // Events of the last update, and those of earlier ones for a moment longer since most last a single update
        private readonly List<(ValveResourceFormat.Renderer.AnimLib.SampledEvent Event, long LastSeen)> recentGraphEvents = [];
        private AnimationGraph? recentGraphEventsGraph;
        private long recentGraphEventsTime;

        private void UpdateRecentGraphEvents(AnimationGraph graph)
        {
            if (recentGraphEventsGraph != graph)
            {
                recentGraphEventsGraph = graph;
                recentGraphEvents.Clear();
            }

            var now = Stopwatch.GetTimestamp();
            recentGraphEventsTime = now;

            var events = graph.SampledEvents;

            for (var i = 0; i < events.Count; i++)
            {
                var sampled = events[i];
                var index = recentGraphEvents.Count - 1;

                // The same event from the same source is one line, however many branches sampled it
                while (index >= 0 && !IsSameGraphEvent(recentGraphEvents[index].Event, sampled))
                {
                    index--;
                }

                if (index < 0)
                {
                    recentGraphEvents.Add((sampled, now));
                }
                else if (recentGraphEvents[index].LastSeen != now || sampled.IsFromActiveBranch)
                {
                    recentGraphEvents[index] = (sampled, now);
                }
            }

            for (var i = recentGraphEvents.Count - 1; i >= 0; i--)
            {
                if (Stopwatch.GetElapsedTime(recentGraphEvents[i].LastSeen, now).TotalSeconds > GraphEventLingerSeconds)
                {
                    recentGraphEvents.RemoveAt(i);
                }
            }
        }

        private int shownGraphEventsHash;

        // Called every frame, but the list only gets new lines when an event starts, ends or lingers out
        private void UpdateGraphEventList(AnimationGraph graph)
        {
            if (graphEventList is not { IsHandleCreated: true } list)
            {
                return;
            }

            UpdateRecentGraphEvents(graph);

            var hash = new HashCode();

            foreach (var (sampled, lastSeen) in recentGraphEvents)
            {
                hash.Add(sampled.IsGraphEvent);
                hash.Add(sampled.GraphEventType);
                hash.Add(sampled.ID);
                hash.Add(sampled.AnimEvent);
                hash.Add(lastSeen == recentGraphEventsTime);
            }

            var newHash = hash.ToHashCode();

            if (newHash == shownGraphEventsHash)
            {
                return;
            }

            shownGraphEventsHash = newHash;

            var lines = new EventListControl.Line[recentGraphEvents.Count];

            for (var i = 0; i < lines.Length; i++)
            {
                var (sampled, lastSeen) = recentGraphEvents[i];
                var name = sampled.AnimEvent switch
                {
                    NmSoundEvent sound => sound.Name,
                    NmParticleEvent particle => particle.ParticleSystemName,
                    NmLegacyEvent legacy => legacy.AnimEventClassName,
                    _ => sampled.ID.Name,
                };

                // CNmSoundEvent reads as Sound
                var kind = sampled.AnimEvent is { } animEvent
                    ? animEvent.ClassName.Replace("CNm", string.Empty, StringComparison.Ordinal).Replace("Event", string.Empty, StringComparison.Ordinal)
                    : sampled.GraphEventType.ToString();

                lines[i] = new EventListControl.Line(name, kind, lastSeen == recentGraphEventsTime);
            }

            list.BeginInvoke(() => list.SetLines(lines));
        }

        private static bool IsSameGraphEvent(in ValveResourceFormat.Renderer.AnimLib.SampledEvent a, in ValveResourceFormat.Renderer.AnimLib.SampledEvent b)
            => a.IsGraphEvent == b.IsGraphEvent
            && a.GraphEventType == b.GraphEventType
            && a.ID == b.ID
            && a.AnimEvent == b.AnimEvent;

        private void ReadGizmoFromParameter(GraphGizmoBinding binding, Matrix4x4 nodeTransform)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var hasHint = animGraph.ParameterHints.TryGetValue(binding.ParameterName, out var hint);
            binding.IsWorldSpace = hasHint && hint.IsWorldSpace;

            FrameBone parameterValue;

            if (binding.IsTarget)
            {
                if (animGraph.TargetParameters.GetValueOrDefault(binding.ParameterName) is { } target)
                {
                    parameterValue = target;
                }
                else if (hasHint)
                {
                    parameterValue = hint.Transform;
                }
                else
                {
                    gizmo.HasValue = false;
                    return;
                }
            }
            else
            {
                if (!hasHint)
                {
                    gizmo.HasValue = false;
                    return;
                }

                // Look-at targets start ahead of the head rather than at the origin
                if (binding.ResetPending || !binding.Initialized)
                {
                    binding.ResetPending = false;
                    binding.Initialized = true;
                    animGraph.VectorParameters[binding.ParameterName] = new Vector4(hint.Transform.Position, 0f);
                    RefreshVectorFields(binding);
                }

                var vector = animGraph.VectorParameters[binding.ParameterName];
                parameterValue = new FrameBone(new Vector3(vector.X, vector.Y, vector.Z), 1f, Quaternion.Identity);
            }

            var characterValue = binding.IsWorldSpace ? parameterValue * animGraph.WorldTransform.Inverse() : parameterValue;

            if (!Matrix4x4.Decompose(characterValue.ToMatrix() * nodeTransform, out _, out var rotation, out var position))
            {
                gizmo.HasValue = false;
                return;
            }

            gizmo.Position = position;
            gizmo.Rotation = rotation;
            gizmo.HasValue = true;
        }

        private void WriteParameterFromGizmo(GraphGizmoBinding binding, Matrix4x4 nodeInverse)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var scene = Matrix4x4.CreateFromQuaternion(gizmo.Rotation) * Matrix4x4.CreateTranslation(gizmo.Position);

            if (!Matrix4x4.Decompose(scene * nodeInverse, out _, out var rotation, out var position))
            {
                return;
            }

            var characterValue = new FrameBone(position, 1f, rotation);
            var parameterValue = binding.IsWorldSpace ? characterValue * animGraph.WorldTransform : characterValue;

            if (binding.IsTarget)
            {
                animGraph.TargetParameters[binding.ParameterName] = parameterValue;
            }
            else
            {
                animGraph.VectorParameters[binding.ParameterName] = new Vector4(parameterValue.Position, 0f);
            }
        }

        private void RefreshVectorFields(GraphGizmoBinding binding)
        {
            if (animGraph == null || binding.Fields is not { } fields || binding.IsTarget)
            {
                return;
            }

            var vector = animGraph.VectorParameters[binding.ParameterName];
            fields[0].BeginInvoke(() =>
            {
                fields[0].Value = vector.X;
                fields[1].Value = vector.Y;
                fields[2].Value = vector.Z;
            });
        }
    }
}
