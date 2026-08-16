using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// The complete render state for a draw, built from the packed
    /// <see href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11"><c>rendersystemdx11</c></see> descriptors.
    /// </summary>
    public record struct RenderState
    {
        // Fields, since a property getter returns a copy and would break state.DepthStencil.DepthFunc = x.
#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>Rasterizer state.</summary>
        public RsRasterizerStateDesc Rasterizer;
        /// <summary>Depth and stencil test state.</summary>
        public RsDepthStencilStateDesc DepthStencil;
        /// <summary>Blend state.</summary>
        public RsBlendStateDesc Blend;
#pragma warning restore CA1051

        /// <summary>Gets the stencil reference value. Some devices bind it on its own, others set it
        /// with the comparison, so <see cref="SetStencilFunc"/> takes both.</summary>
        public byte StencilRef { readonly get; private set; }

        // The renderer draws a single render target, so the members below reach index 0 of Blend.

        /// <summary>Gets or sets whether blending is enabled.</summary>
        public bool BlendEnable
        {
            readonly get => Blend.BlendEnable[0];
            set => Blend.SetBlendEnable(0, value);
        }

        /// <summary>Gets or sets the color channels that are written.</summary>
        public RsColorWriteEnableBits ColorWriteMask
        {
            readonly get => Blend.RenderTargetWriteMask[0];
            set => Blend.SetRenderTargetWriteMask(0, value);
        }

        /// <summary>Gets the source blend factor. Both factors are set at once, by <see cref="SetBlend"/>.</summary>
        public readonly RsBlendMode SrcBlend => Blend.SrcBlend[0];

        /// <summary>Gets the destination blend factor.</summary>
        public readonly RsBlendMode DestBlend => Blend.DestBlend[0];

        /// <summary>Sets the source and destination blend factors.</summary>
        public void SetBlend(RsBlendMode src, RsBlendMode dst)
        {
            Blend.SetSrcBlend(0, src);
            Blend.SetDestBlend(0, dst);
        }

        /// <summary>Sets the stencil comparison and the value it compares against, for both faces.</summary>
        public void SetStencilFunc(RsComparison func, byte reference)
        {
            DepthStencil.FrontStencilFunc = func;
            DepthStencil.BackStencilFunc = func;
            StencilRef = reference;
        }

        /// <summary>Sets the operation both faces take when the stencil and depth tests pass.</summary>
        public void SetStencilPassOp(RsStencilOp op)
        {
            DepthStencil.FrontStencilPassOp = op;
            DepthStencil.BackStencilPassOp = op;
        }

        /// <summary>Gets the renderer default: solid fill, backface culling, depth clip on,
        /// multisampling on, depth test and write on, blending off with alpha factors on render
        /// target 0, all color channels written.</summary>
        public static RenderState Default { get; } = CreateDefault();

        private static RenderState CreateDefault()
        {
            var state = new RenderState
            {
                Rasterizer = new()
                {
                    FillMode = RsFillMode.Solid,
                    CullMode = RsCullMode.Back,
                    DepthClipEnable = true,
                    MultisampleEnable = true,
                },
                DepthStencil = new()
                {
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthFunc = RsComparison.Closer,
                    StencilReadMask = 0xFF,
                    StencilWriteMask = 0xFF, // stencil clears obey this mask even with the test disabled
                },
            };

            state.SetStencilFunc(RsComparison.Always, reference: 0);
            state.SetBlend(RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha);
            state.ColorWriteMask = RsColorWriteEnableBits.All;

            return state;
        }
    }

    /// <summary>
    /// State that is recorded in a command buffer rather than baking into a pipeline object.
    /// </summary>
    public record struct DynamicState
    {
        /// <summary>Window-space depth the near plane maps to.</summary>
        public float DepthRangeNear { get; set; }

        /// <summary>Window-space depth the far plane maps to.</summary>
        public float DepthRangeFar { get; set; }

        /// <summary>Depth value a depth clear writes.</summary>
        public float ClearDepth { get; set; }

        /// <summary>Whether rendering is confined to the scissor rectangle.</summary>
        public bool ScissorTest { get; set; }

        /// <summary>Gets the renderer default: the full depth range, clearing to the reverse-Z far
        /// plane of zero, no scissor.</summary>
        public static DynamicState Default { get; } = new() { DepthRangeNear = 0f, DepthRangeFar = 1f, ClearDepth = 0f };
    }

    /// <summary>
    /// Tracks and applies render state for one device context. Owns the pass baseline
    /// (<see cref="CurrentPass"/>) and a shadow of the last applied state, so <see cref="Apply"/>
    /// only sets what changed. State is per context, so each <see cref="RendererContext"/> owns one.
    /// </summary>
    public class RenderStateTracker
    {
        /// <summary>Gets the state of the innermost enclosing pass. Compose per-draw state by
        /// copying this and overriding fields.</summary>
        public RenderState CurrentPass { get; private set; } = RenderState.Default;

        // What the driver was last set to. Meaningless until the first apply, which pushes everything.
        private RenderState applied;
        private bool appliedKnown;

        /// <summary>Composes a state over <see cref="CurrentPass"/> and applies it for the scope of
        /// the returned <see langword="using"/> guard. Omitted arguments keep the pass value. For
        /// state not covered here (e.g. stencil), build a <see cref="RenderState"/> and open a
        /// <see cref="RenderPassScope"/> directly.</summary>
        /// <returns>The guard that restores the previous baseline on dispose.</returns>
        public RenderPassScope Scope(
            RsFillMode? fillMode = null,
            RsCullMode? cullMode = null,
            bool? multisampleEnable = null,
            bool? depthClipEnable = null,
            int? depthBias = null,
            float? depthBiasClamp = null,
            float? slopeScaledDepthBias = null,
            bool? depthTest = null,
            bool? depthWrite = null,
            RsComparison? depthFunc = null,
            bool? blend = null,
            RsBlendMode? srcBlend = null,
            RsBlendMode? dstBlend = null,
            RsColorWriteEnableBits? colorWriteMask = null)
        {
            var state = CurrentPass;

            state.Rasterizer.FillMode = fillMode ?? state.Rasterizer.FillMode;
            state.Rasterizer.CullMode = cullMode ?? state.Rasterizer.CullMode;
            state.Rasterizer.MultisampleEnable = multisampleEnable ?? state.Rasterizer.MultisampleEnable;
            state.Rasterizer.DepthClipEnable = depthClipEnable ?? state.Rasterizer.DepthClipEnable;
            state.Rasterizer.DepthBias = depthBias ?? state.Rasterizer.DepthBias;
            state.Rasterizer.DepthBiasClamp = depthBiasClamp ?? state.Rasterizer.DepthBiasClamp;
            state.Rasterizer.SlopeScaledDepthBias = slopeScaledDepthBias ?? state.Rasterizer.SlopeScaledDepthBias;
            state.DepthStencil.DepthTestEnable = depthTest ?? state.DepthStencil.DepthTestEnable;
            state.DepthStencil.DepthWriteEnable = depthWrite ?? state.DepthStencil.DepthWriteEnable;
            state.DepthStencil.DepthFunc = depthFunc ?? state.DepthStencil.DepthFunc;

            state.BlendEnable = blend ?? state.BlendEnable;
            state.ColorWriteMask = colorWriteMask ?? state.ColorWriteMask;
            state.SetBlend(srcBlend ?? state.SrcBlend, dstBlend ?? state.DestBlend);

            return new RenderPassScope(this, in state);
        }

        /// <summary>Applies a state as the pass baseline for the scope of the returned
        /// <see langword="using"/> guard.</summary>
        public RenderPassScope Scope(in RenderState state) => new(this, in state);

        /// <summary>Re-applies <see cref="CurrentPass"/>, for draws that cannot set state themselves
        /// and would otherwise inherit whatever the last draw latched.</summary>
        public void RestorePassBaseline() => Apply(CurrentPass);

        /// <summary>Gets the dynamic state currently set.</summary>
        public DynamicState CurrentDynamic { get; private set; } = DynamicState.Default;

        private bool dynamicKnown;

        /// <summary>Sets dynamic state for the scope of the returned <see langword="using"/> guard,
        /// restoring what was set before on dispose. Omitted arguments keep the current value.</summary>
        public DynamicStateScope ScopeDynamic(Renderer.DepthRange? depthRange = null,
            float? clearDepth = null, bool? scissorTest = null)
        {
            var state = CurrentDynamic;

            state.DepthRangeNear = depthRange?.Near ?? state.DepthRangeNear;
            state.DepthRangeFar = depthRange?.Far ?? state.DepthRangeFar;
            state.ClearDepth = clearDepth ?? state.ClearDepth;
            state.ScissorTest = scissorTest ?? state.ScissorTest;

            return new DynamicStateScope(this, in state);
        }

        /// <summary>Sets the depth range, for passes that hand the same range to several draws.</summary>
        public void SetDepthRange(Renderer.DepthRange range)
        {
            var state = CurrentDynamic;
            state.DepthRangeNear = range.Near;
            state.DepthRangeFar = range.Far;
            ApplyDynamic(in state);
        }

        /// <summary>Sets dynamic state, emitting only the calls whose values changed.</summary>
        public void ApplyDynamic(in DynamicState state)
        {
            var pushEverything = !dynamicKnown;

            if (pushEverything || state.DepthRangeNear != CurrentDynamic.DepthRangeNear || state.DepthRangeFar != CurrentDynamic.DepthRangeFar)
            {
                CountDriverCall();
                GL.DepthRange(state.DepthRangeNear, state.DepthRangeFar);
            }

            if (pushEverything || state.ClearDepth != CurrentDynamic.ClearDepth)
            {
                CountDriverCall();
                GL.ClearDepth(state.ClearDepth);
            }

            if (pushEverything || state.ScissorTest != CurrentDynamic.ScissorTest)
            {
                SetEnabled(EnableCap.ScissorTest, state.ScissorTest);
            }

            CurrentDynamic = state;
            dynamicKnown = true;
        }

        /// <summary>Applies a state and makes it <see cref="CurrentPass"/>.</summary>
        public void SetPassBaseline(in RenderState state)
        {
            CurrentPass = state;
            Apply(in state);
        }

        /// <summary>Applies a state. Diffs at two levels: one packed compare per descriptor, then
        /// only the calls whose fields changed inside a changed descriptor.</summary>
        public void Apply(in RenderState state)
        {
            var pushEverything = !appliedKnown;

            PerfStats.Active.Count(Counter.RenderStateApply);

            if (pushEverything || state.Rasterizer != applied.Rasterizer)
            {
                PerfStats.Active.Count(Counter.RenderStateGroupEmit);
                ApplyRasterizer(in state.Rasterizer, in applied.Rasterizer, pushEverything);
            }

            if (pushEverything || state.DepthStencil != applied.DepthStencil || state.StencilRef != applied.StencilRef)
            {
                PerfStats.Active.Count(Counter.RenderStateGroupEmit);
                ApplyDepthStencil(in state, in applied, pushEverything);
            }

            if (pushEverything || state.Blend != applied.Blend)
            {
                PerfStats.Active.Count(Counter.RenderStateGroupEmit);
                ApplyBlend(in state.Blend, in applied.Blend, pushEverything);
            }

            applied = state;
            appliedKnown = true;
        }

        private static void CountDriverCall(int amount = 1) => PerfStats.Active.Count(Counter.RenderStateDriverCall, amount);

        private static void SetEnabled(EnableCap cap, bool enabled)
        {
            CountDriverCall();

            if (enabled)
            {
                GL.Enable(cap);
            }
            else
            {
                GL.Disable(cap);
            }
        }

        private static void ApplyRasterizer(in RsRasterizerStateDesc rasterizer, in RsRasterizerStateDesc prev, bool pushEverything)
        {
            if (pushEverything || rasterizer.FillMode != prev.FillMode)
            {
                CountDriverCall();
                GL.PolygonMode(TriangleFace.FrontAndBack, rasterizer.FillMode == RsFillMode.Wireframe ? PolygonMode.Line : PolygonMode.Fill);
            }

            if (pushEverything || rasterizer.CullMode != prev.CullMode)
            {
                SetEnabled(EnableCap.CullFace, rasterizer.CullMode != RsCullMode.None);

                if (rasterizer.CullMode != RsCullMode.None)
                {
                    CountDriverCall();
                    GL.CullFace(rasterizer.CullMode == RsCullMode.Front ? TriangleFace.Front : TriangleFace.Back);
                }
            }

            if (pushEverything || rasterizer.DepthClipEnable != prev.DepthClipEnable)
            {
                // Depth clamping is the opposite of depth clipping
                SetEnabled(EnableCap.DepthClamp, !rasterizer.DepthClipEnable);
            }

            if (pushEverything || rasterizer.MultisampleEnable != prev.MultisampleEnable)
            {
                SetEnabled(EnableCap.Multisample, rasterizer.MultisampleEnable);
            }

            if (pushEverything
                || rasterizer.DepthBias != prev.DepthBias
                || rasterizer.DepthBiasClamp != prev.DepthBiasClamp
                || rasterizer.SlopeScaledDepthBias != prev.SlopeScaledDepthBias)
            {
                var biased = rasterizer.DepthBias != 0 || rasterizer.SlopeScaledDepthBias != 0f;

                // Bias both polygon modes, Vulkan-style, so a biased material stays biased in wireframe.
                SetEnabled(EnableCap.PolygonOffsetFill, biased);
                SetEnabled(EnableCap.PolygonOffsetLine, biased);

                CountDriverCall();
                GL.PolygonOffsetClamp(rasterizer.SlopeScaledDepthBias, rasterizer.DepthBias, rasterizer.DepthBiasClamp);
            }
        }

        private const ulong FrontStencilOpBits = RsDepthStencilStateDesc.FrontStencilFailOpBits
            | RsDepthStencilStateDesc.FrontStencilDepthFailOpBits
            | RsDepthStencilStateDesc.FrontStencilPassOpBits;

        private const ulong BackStencilOpBits = RsDepthStencilStateDesc.BackStencilFailOpBits
            | RsDepthStencilStateDesc.BackStencilDepthFailOpBits
            | RsDepthStencilStateDesc.BackStencilPassOpBits;

        private const ulong FrontStencilFuncBits = RsDepthStencilStateDesc.FrontStencilFuncBits | RsDepthStencilStateDesc.StencilReadMaskBits;
        private const ulong BackStencilFuncBits = RsDepthStencilStateDesc.BackStencilFuncBits | RsDepthStencilStateDesc.StencilReadMaskBits;

        private static void ApplyDepthStencil(in RenderState state, in RenderState prev, bool pushEverything)
        {
            var depthStencil = state.DepthStencil;

            var delta = pushEverything ? ulong.MaxValue : depthStencil.Delta(prev.DepthStencil);

            // The reference value is not part of the descriptor, but is set with the comparison.
            var stencilRefDelta = pushEverything || state.StencilRef != prev.StencilRef;

            if ((delta & RsDepthStencilStateDesc.DepthTestEnableBits) != 0)
            {
                SetEnabled(EnableCap.DepthTest, depthStencil.DepthTestEnable);
            }

            if ((delta & RsDepthStencilStateDesc.DepthWriteEnableBits) != 0)
            {
                CountDriverCall();
                GL.DepthMask(depthStencil.DepthWriteEnable);
            }

            if ((delta & RsDepthStencilStateDesc.DepthFuncBits) != 0)
            {
                CountDriverCall();
                GL.DepthFunc(ToGL(depthStencil.DepthFunc));
            }

            if ((delta & RsDepthStencilStateDesc.StencilEnableBits) != 0)
            {
                SetEnabled(EnableCap.StencilTest, depthStencil.StencilEnable);
            }

            if ((delta & FrontStencilOpBits) != 0)
            {
                CountDriverCall();
                GL.StencilOpSeparate(StencilFace.Front, ToGL(depthStencil.FrontStencilFailOp), ToGL(depthStencil.FrontStencilDepthFailOp), ToGL(depthStencil.FrontStencilPassOp));
            }

            if ((delta & BackStencilOpBits) != 0)
            {
                CountDriverCall();
                GL.StencilOpSeparate(StencilFace.Back, ToGL(depthStencil.BackStencilFailOp), ToGL(depthStencil.BackStencilDepthFailOp), ToGL(depthStencil.BackStencilPassOp));
            }

            if (stencilRefDelta || (delta & FrontStencilFuncBits) != 0)
            {
                CountDriverCall();
                GL.StencilFuncSeparate(StencilFace.Front, ToGLStencil(depthStencil.FrontStencilFunc), state.StencilRef, depthStencil.StencilReadMask);
            }

            if (stencilRefDelta || (delta & BackStencilFuncBits) != 0)
            {
                CountDriverCall();
                GL.StencilFuncSeparate(StencilFace.Back, ToGLStencil(depthStencil.BackStencilFunc), state.StencilRef, depthStencil.StencilReadMask);
            }

            if ((delta & RsDepthStencilStateDesc.StencilWriteMaskBits) != 0)
            {
                CountDriverCall();
                GL.StencilMask(depthStencil.StencilWriteMask);
            }
        }

        // Alpha factors and blend ops are not applied yet; the renderer always blends with the
        // color factors and the add operation.
        private static void ApplyBlend(in RsBlendStateDesc blend, in RsBlendStateDesc prev, bool pushEverything)
        {
            if (pushEverything || blend.BlendEnable[0] != prev.BlendEnable[0])
            {
                SetEnabled(EnableCap.Blend, blend.BlendEnable[0]);
            }

            if (pushEverything || blend.SrcBlend[0] != prev.SrcBlend[0] || blend.DestBlend[0] != prev.DestBlend[0])
            {
                CountDriverCall();
                GL.BlendFunc(ToGL(blend.SrcBlend[0]), ToGL(blend.DestBlend[0]));
            }

            if (pushEverything || blend.AlphaToCoverageEnable != prev.AlphaToCoverageEnable)
            {
                SetEnabled(EnableCap.SampleAlphaToCoverage, blend.AlphaToCoverageEnable);
            }

            if (pushEverything || blend.RenderTargetWriteMask[0] != prev.RenderTargetWriteMask[0])
            {
                CountDriverCall();
                var mask = blend.RenderTargetWriteMask[0];
                GL.ColorMask(
                    (mask & RsColorWriteEnableBits.R) != 0,
                    (mask & RsColorWriteEnableBits.G) != 0,
                    (mask & RsColorWriteEnableBits.B) != 0,
                    (mask & RsColorWriteEnableBits.A) != 0);
            }
        }

        private static DepthFunction ToGL(RsComparison comparison) => comparison switch
        {
            RsComparison.Never => DepthFunction.Never,
            RsComparison.Less => DepthFunction.Less,
            RsComparison.Equal => DepthFunction.Equal,
            RsComparison.LessEqual => DepthFunction.Lequal,
            RsComparison.Greater => DepthFunction.Greater,
            RsComparison.NotEqual => DepthFunction.Notequal,
            RsComparison.GreaterEqual => DepthFunction.Gequal,
            RsComparison.Always => DepthFunction.Always,

            // reverse-Z, so closer is greater
            RsComparison.Closer => DepthFunction.Greater,
            RsComparison.CloserEqual => DepthFunction.Gequal,
            RsComparison.Farther => DepthFunction.Less,
            RsComparison.FartherEqual => DepthFunction.Lequal,
            _ => throw new NotImplementedException($"Unknown comparison {comparison}"),
        };

        private static StencilFunction ToGLStencil(RsComparison comparison) => (StencilFunction)ToGL(comparison);

        private static StencilOp ToGL(RsStencilOp operation) => operation switch
        {
            RsStencilOp.Keep => StencilOp.Keep,
            RsStencilOp.Zero => StencilOp.Zero,
            RsStencilOp.Replace => StencilOp.Replace,
            RsStencilOp.IncrSat => StencilOp.Incr,
            RsStencilOp.DecrSat => StencilOp.Decr,
            RsStencilOp.Invert => StencilOp.Invert,
            RsStencilOp.Incr => StencilOp.IncrWrap,
            RsStencilOp.Decr => StencilOp.DecrWrap,
            _ => throw new NotImplementedException($"Unknown stencil operation {operation}"),
        };

        private static BlendingFactor ToGL(RsBlendMode factor) => factor switch
        {
            RsBlendMode.Zero => BlendingFactor.Zero,
            RsBlendMode.One => BlendingFactor.One,
            RsBlendMode.SrcColor => BlendingFactor.SrcColor,
            RsBlendMode.InvSrcColor => BlendingFactor.OneMinusSrcColor,
            RsBlendMode.SrcAlpha => BlendingFactor.SrcAlpha,
            RsBlendMode.InvSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
            RsBlendMode.DestAlpha => BlendingFactor.DstAlpha,
            RsBlendMode.InvDestAlpha => BlendingFactor.OneMinusDstAlpha,
            RsBlendMode.DestColor => BlendingFactor.DstColor,
            RsBlendMode.InvDestColor => BlendingFactor.OneMinusDstColor,
            RsBlendMode.SrcAlphaSat => BlendingFactor.SrcAlphaSaturate,
            RsBlendMode.BlendFactor => BlendingFactor.ConstantColor,
            RsBlendMode.InvBlendFactor => BlendingFactor.OneMinusConstantColor,
            _ => throw new NotImplementedException($"Unknown blend factor {factor}"),
        };
    }

    /// <summary>
    /// Sets <see cref="DynamicState"/> for its scope and restores the previous values on dispose.
    /// </summary>
    public readonly ref struct DynamicStateScope
    {
        private readonly RenderStateTracker tracker;
        private readonly DynamicState previous;

        /// <summary>Sets <paramref name="state"/> until the scope is disposed.</summary>
        public DynamicStateScope(RenderStateTracker tracker, scoped in DynamicState state)
        {
            this.tracker = tracker;
            previous = tracker.CurrentDynamic;
            tracker.ApplyDynamic(in state);
        }

        /// <summary>Restores the previous dynamic state.</summary>
        public void Dispose() => tracker?.ApplyDynamic(previous);
    }

    /// <summary>
    /// Sets a render pass baseline for its scope and restores the previous one on dispose.
    /// Nesting-safe: compose the new baseline from <see cref="RenderStateTracker.CurrentPass"/>
    /// so outer overrides (e.g. global wireframe) survive into sub-passes.
    /// </summary>
    public readonly ref struct RenderPassScope
    {
        private readonly RenderStateTracker tracker;
        private readonly RenderState previous;

        /// <summary>Applies <paramref name="state"/> as the pass baseline.</summary>
        public RenderPassScope(RenderStateTracker tracker, scoped in RenderState state)
        {
            this.tracker = tracker;
            previous = tracker.CurrentPass;
            tracker.SetPassBaseline(in state);
        }

        /// <summary>Restores the previous pass baseline. A <see langword="default"/> scope does
        /// nothing, so a scope can be conditional.</summary>
        public void Dispose() => tracker?.SetPassBaseline(in previous);
    }
}
