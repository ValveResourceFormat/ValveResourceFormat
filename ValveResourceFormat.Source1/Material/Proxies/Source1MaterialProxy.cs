namespace ValveResourceFormat.Source1.Material.Proxies;

/// <summary>
/// A material proxy that sets variables of its material from the time. Proxies that read an entity write the
/// value they have without one, or leave the variables as the material has them when they write nothing then.
/// </summary>
internal abstract class Source1MaterialProxy
{
    protected Source1MaterialProxies Owner { get; private set; } = null!;

    public static Source1MaterialProxy? Create(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "sine": return new SineProxy();
            case "linearramp": return new LinearRampProxy();
            case "currenttime": return new CurrentTimeProxy();
            case "uniformnoise": return new UniformNoiseProxy();
            case "gaussiannoise": return new GaussianNoiseProxy();
            case "add": return new AddProxy();
            case "subtract": return new SubtractProxy();
            case "multiply": return new MultiplyProxy();
            case "divide": return new DivideProxy();
            case "clamp": return new ClampProxy();
            case "equals": return new EqualsProxy();
            case "abs": return new AbsProxy();
            case "frac": return new FracProxy();
            case "int": return new IntProxy();
            case "exponential": return new ExponentialProxy();
            case "lessorequal": return new LessOrEqualProxy();
            case "wrapminmax": return new WrapMinMaxProxy();
            case "selectfirstifnonzero": return new SelectFirstIfNonZeroProxy();
            case "itemtintcolor": return new ResultVectorProxy(0f, 0f, 0f);
            case "yellowlevel": return new ResultVectorProxy(1f, 1f, 1f);
            case "modelglowcolor": return new ResultVectorProxy(1f, 1f, 1f);
            case "stickybombglowcolor": return new ResultVectorProxy(1f, 1f, 1f);
            case "burnlevel": return new ResultFloatProxy(0f);
            case "invulnlevel": return new ResultFloatProxy(1f);
            case "communityweapon": return new ResultIntProxy(0);
            case "invis":
            case "weapon_invis":
            case "building_invis":
            case "vm_invis":
                return new CloakProxy(tint: false);
            case "spy_invis": return new CloakProxy(tint: true);
            case "texturescroll": return new TextureScrollProxy();
            case "texturetransform": return new TextureTransformProxy();
            case "matrixrotate": return new MatrixRotateProxy();
            case "animatedtexture": return new AnimatedTextureProxy();
        }

        return IsInactive(name) ? new InactiveProxy() : null;
    }

    public bool Init(Source1MaterialProxies owner, IReadOnlyDictionary<string, string> keys)
    {
        Owner = owner;
        return OnInit(keys);
    }

    public abstract void OnBind();

    /// <summary>
    /// Returns how fast a variable of a material scrolls per second when this proxy makes it grow linearly with the time, or
    /// <see langword="null"/> when it does not.
    /// </summary>
    public virtual Vector2? GetScrollVelocity(string variable) => null;

    /// <summary>
    /// Returns the frames this proxy steps through at a constant rate for the frame variable of a texture variable, or
    /// <see langword="null"/> when it does not.
    /// </summary>
    public virtual Source1TextureAnimation? GetAnimation(string frameVariable, string textureVariable) => null;

    protected abstract bool OnInit(IReadOnlyDictionary<string, string> keys);

    // Proxies of a viewer without entities or a console keep the value the material file gives
    private static bool IsInactive(string name)
    {
        if (name.StartsWith("player", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("portal", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("materialmodify", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.ToLowerInvariant() is "empty" or "dummy" or "convar" or "waterlod" or "worlddims"
            or "animatedentitytexture" or "animatedoffsettexture" or "animatespecifictexture" or "toggletexture"
            or "camo" or "cycle" or "entityorigin" or "entityrandom" or "entityspeed" or "health" or "isnpc"
            or "animatedweaponsheen" or "weaponskin";
    }

    protected static void WriteFloat(Source1VarReference result, float value)
    {
        var target = result.Var;

        if (target.Type == Source1MaterialVarType.Vector && result.Component >= 0)
        {
            target.SetVectorComponent(result.Component, value);
        }
        else if (target.Type == Source1MaterialVarType.Vector)
        {
            target.FillVector(value);
        }
        else
        {
            target.SetFloat(value);
        }
    }

    private sealed class InactiveProxy : Source1MaterialProxy
    {
        protected override bool OnInit(IReadOnlyDictionary<string, string> keys) => true;

        public override void OnBind()
        {
        }
    }

    private abstract class ResultProxy : Source1MaterialProxy
    {
        protected Source1VarReference Result { get; private set; }

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (Owner.ReadResult(keys) is not { } reference)
            {
                return false;
            }

            Result = reference;
            return true;
        }
    }

    private abstract class ScalarProxy : ResultProxy
    {
        protected abstract void InitInputs(IReadOnlyDictionary<string, string> keys);

        protected abstract float Compute();

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (!base.OnInit(keys))
            {
                return false;
            }

            InitInputs(keys);
            return true;
        }

        public override void OnBind() => WriteFloat(Result, Compute());
    }

    private sealed class SineProxy : ScalarProxy
    {
        private Source1FloatInput period;
        private Source1FloatInput max;
        private Source1FloatInput min;
        private Source1FloatInput timeOffset;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            period = Owner.ReadFloat(keys, "sinePeriod", 1f);
            max = Owner.ReadFloat(keys, "sineMax", 1f);
            min = Owner.ReadFloat(keys, "sineMin", 0f);
            timeOffset = Owner.ReadFloat(keys, "timeOffset", 0f);
        }

        protected override float Compute()
        {
            var periodValue = period.Get();

            if (periodValue == 0f)
            {
                periodValue = 1f;
            }

            var minValue = min.Get();
            var phase = (Owner.Time - timeOffset.Get()) * MathF.Tau / periodValue;
            return ((MathF.Sin(phase) * 0.5f) + 0.5f) * (max.Get() - minValue) + minValue;
        }
    }

    private sealed class LinearRampProxy : ScalarProxy
    {
        private Source1FloatInput rate;
        private Source1FloatInput initialValue;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            rate = Owner.ReadFloat(keys, "rate", 1f);
            initialValue = Owner.ReadFloat(keys, "initialValue", 0f);
        }

        protected override float Compute() => initialValue.Get() + (rate.Get() * Owner.Time);
    }

    private sealed class CurrentTimeProxy : ScalarProxy
    {
        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
        }

        protected override float Compute() => Owner.Time;
    }

    private sealed class UniformNoiseProxy : ScalarProxy
    {
        private Source1FloatInput min;
        private Source1FloatInput max;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            min = Owner.ReadFloat(keys, "minVal", 0f);
            max = Owner.ReadFloat(keys, "maxVal", 1f);
        }

        protected override float Compute()
        {
            var minValue = min.Get();
            return minValue + ((max.Get() - minValue) * Owner.Random.NextSingle());
        }
    }

    private sealed class GaussianNoiseProxy : ScalarProxy
    {
        private Source1FloatInput mean;
        private Source1FloatInput halfWidth;
        private Source1FloatInput min;
        private Source1FloatInput max;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            mean = Owner.ReadFloat(keys, "mean", 0f);
            halfWidth = Owner.ReadFloat(keys, "halfwidth", 1f);
            min = Owner.ReadFloat(keys, "minVal", -float.MaxValue);
            max = Owner.ReadFloat(keys, "maxVal", float.MaxValue);
        }

        protected override float Compute()
        {
            var u1 = 1f - Owner.Random.NextSingle();
            var u2 = Owner.Random.NextSingle();
            var gaussian = MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(MathF.Tau * u2);
            var value = mean.Get() + (halfWidth.Get() * gaussian);

            var minValue = min.Get();
            var maxValue = max.Get();

            if (minValue > maxValue)
            {
                (minValue, maxValue) = (maxValue, minValue);
            }

            return value < minValue ? minValue : MathF.Min(maxValue, value);
        }
    }

    private sealed class ResultVectorProxy(float x, float y, float z) : ResultProxy
    {
        public override void OnBind() => Result.Var.SetVector([x, y, z]);
    }

    private sealed class ResultFloatProxy(float value) : ResultProxy
    {
        public override void OnBind() => Result.Var.SetFloat(value);
    }

    private sealed class ResultIntProxy(int value) : ResultProxy
    {
        public override void OnBind() => Result.Var.SetInt(value);
    }

    // The cloak proxies always write $cloakfactor, and the spy one also resets $cloakColorTint
    private sealed class CloakProxy(bool tint) : Source1MaterialProxy
    {
        private Source1MaterialVar factor = null!;
        private Source1MaterialVar? colorTint;

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            factor = FindResult("$cloakfactor");

            if (tint)
            {
                colorTint = FindResult("$cloakColorTint");
            }

            return true;
        }

        public override void OnBind()
        {
            factor.SetFloat(0f);
            colorTint?.SetVector([1f, 1f, 1f]);
        }

        private Source1MaterialVar FindResult(string name)
        {
            var reference = Owner.FindReference(name, create: true)!.Value;
            Owner.MarkWritten(reference.Name);
            return reference.Var;
        }
    }

    private abstract class SourceVarProxy : Source1MaterialProxy
    {
        private Source1VarReference result;
        private Source1MaterialVar source1 = null!;
        private Source1MaterialVar? source2;

        protected virtual bool RequiresSource2 => false;

        protected virtual bool SupportsInt => true;

        protected abstract float Compute(float a, float b);

        protected virtual void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
        }

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (Owner.ReadResult(keys) is not { } resultReference)
            {
                return false;
            }

            keys.TryGetValue("srcVar1", out var source1Name);
            keys.TryGetValue("srcVar2", out var source2Name);

            var first = Owner.FindReference(source1Name, create: true);
            var second = Owner.FindReference(source2Name, create: RequiresSource2);

            if (first is null || (RequiresSource2 && second is null))
            {
                return false;
            }

            result = resultReference;
            source1 = first.Value.Var;
            source2 = second?.Var;
            InitInputs(keys);
            return true;
        }

        public override void OnBind()
        {
            var sized = result.Var;
            var type = sized.Type;

            if (type == Source1MaterialVarType.Undefined)
            {
                sized = source1;
                type = source1.Type;
            }

            if (type == Source1MaterialVarType.Undefined && source2 is not null)
            {
                sized = source2;
                type = source2.Type;
            }

            switch (type)
            {
                case Source1MaterialVarType.Float:
                    WriteFloat(result, Compute(source1.GetFloat(), source2?.GetFloat() ?? 0f));
                    break;
                case Source1MaterialVarType.Vector when result.Component >= 0:
                    WriteFloat(result, Compute(source1.GetFloat(), source2?.GetFloat() ?? 0f));
                    break;
                case Source1MaterialVarType.Vector:
                    BindVector(sized.VectorSize);
                    break;
                case Source1MaterialVarType.Int when SupportsInt:
                    var value = Compute(source1.GetInt(), source2?.GetInt() ?? 0);
                    result.Var.SetInt((int)value);
                    break;
            }
        }

        // A source that is not a vector reads as the same value in every component, and three components are
        // computed whatever the size is. A fourth component keeps the value the result has.
        private void BindVector(int size)
        {
            if (size == 0)
            {
                return;
            }

            Span<float> first = stackalloc float[4];
            Span<float> second = stackalloc float[4];
            Span<float> values = stackalloc float[4];
            source1.CopyVector(first);
            source2?.CopyVector(second);
            result.Var.CopyVector(values);

            ComputeVector(first, second, values);
            result.Var.SetVector(values[..size]);
        }

        protected virtual void ComputeVector(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> values)
        {
            for (var i = 0; i < 3; i++)
            {
                values[i] = Compute(a[i], b[i]);
            }
        }
    }

    private sealed class SelectFirstIfNonZeroProxy : SourceVarProxy
    {
        private const float VectorThreshold = 0.01f;

        protected override bool RequiresSource2 => true;

        protected override float Compute(float a, float b) => a == 0f ? b : a;

        protected override void ComputeVector(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> values)
        {
            var chosen = b;

            for (var i = 0; i < 3; i++)
            {
                if (MathF.Abs(a[i]) >= VectorThreshold)
                {
                    chosen = a;
                    break;
                }
            }

            chosen[..3].CopyTo(values);
        }
    }

    private sealed class AddProxy : SourceVarProxy
    {
        protected override bool RequiresSource2 => true;

        protected override float Compute(float a, float b) => a + b;
    }

    private sealed class SubtractProxy : SourceVarProxy
    {
        protected override bool RequiresSource2 => true;

        protected override float Compute(float a, float b) => a - b;
    }

    private sealed class MultiplyProxy : SourceVarProxy
    {
        protected override bool RequiresSource2 => true;

        protected override float Compute(float a, float b) => a * b;
    }

    private sealed class DivideProxy : SourceVarProxy
    {
        protected override bool RequiresSource2 => true;

        protected override float Compute(float a, float b) => b == 0f ? a : a / b;

        protected override void ComputeVector(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> values)
        {
            for (var i = 0; i < 3; i++)
            {
                values[i] = a[i] / b[i];
            }
        }
    }

    private sealed class EqualsProxy : SourceVarProxy
    {
        protected override float Compute(float a, float b) => a;
    }

    private sealed class AbsProxy : SourceVarProxy
    {
        protected override bool SupportsInt => false;

        protected override float Compute(float a, float b) => MathF.Abs(a);
    }

    private sealed class FracProxy : SourceVarProxy
    {
        protected override float Compute(float a, float b) => a - MathF.Truncate(a);
    }

    private sealed class IntProxy : SourceVarProxy
    {
        protected override float Compute(float a, float b) => MathF.Truncate(a);
    }

    private sealed class ClampProxy : SourceVarProxy
    {
        private Source1FloatInput min;
        private Source1FloatInput max;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            min = Owner.ReadFloat(keys, "min", 0f);
            max = Owner.ReadFloat(keys, "max", 1f);
        }

        protected override float Compute(float a, float b)
        {
            var minValue = min.Get();
            var maxValue = max.Get();

            if (minValue > maxValue)
            {
                (minValue, maxValue) = (maxValue, minValue);
            }

            return Math.Clamp(a, minValue, maxValue);
        }
    }

    private sealed class ExponentialProxy : SourceVarProxy
    {
        private Source1FloatInput scale;
        private Source1FloatInput offset;
        private Source1FloatInput min;
        private Source1FloatInput max;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            scale = Owner.ReadFloat(keys, "scale", 1f);
            offset = Owner.ReadFloat(keys, "offset", 0f);
            min = Owner.ReadFloat(keys, "minVal", -float.MaxValue);
            max = Owner.ReadFloat(keys, "maxVal", float.MaxValue);
        }

        protected override float Compute(float a, float b)
        {
            var value = scale.Get() * MathF.Exp(a + offset.Get());
            var minValue = min.Get();
            var maxValue = max.Get();

            if (minValue > maxValue)
            {
                (minValue, maxValue) = (maxValue, minValue);
            }

            return Math.Clamp(value, minValue, maxValue);
        }
    }

    private sealed class WrapMinMaxProxy : SourceVarProxy
    {
        private Source1FloatInput min;
        private Source1FloatInput max;

        protected override void InitInputs(IReadOnlyDictionary<string, string> keys)
        {
            min = Owner.ReadFloat(keys, "minVal", 0f);
            max = Owner.ReadFloat(keys, "maxVal", 1f);
        }

        protected override float Compute(float a, float b)
        {
            var minValue = min.Get();
            var maxValue = max.Get();

            if (minValue >= maxValue)
            {
                return minValue;
            }

            var t = (a - minValue) / (maxValue - minValue);
            t -= MathF.Floor(t);
            return minValue + ((maxValue - minValue) * t);
        }
    }

    private sealed class LessOrEqualProxy : Source1MaterialProxy
    {
        private Source1VarReference result;
        private Source1MaterialVar source1 = null!;
        private Source1MaterialVar source2 = null!;
        private Source1MaterialVar lessOrEqual = null!;
        private Source1MaterialVar greater = null!;

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (Owner.ReadResult(keys) is not { } resultReference)
            {
                return false;
            }

            keys.TryGetValue("srcVar1", out var source1Name);
            keys.TryGetValue("srcVar2", out var source2Name);
            keys.TryGetValue("lessEqualVar", out var lessOrEqualName);
            keys.TryGetValue("greaterVar", out var greaterName);

            var first = Owner.FindReference(source1Name, create: true);
            var second = Owner.FindReference(source2Name, create: true);
            var less = Owner.FindReference(lessOrEqualName, create: true);
            var more = Owner.FindReference(greaterName, create: true);

            if (first is null || second is null || less is null || more is null)
            {
                return false;
            }

            result = resultReference;
            source1 = first.Value.Var;
            source2 = second.Value.Var;
            lessOrEqual = less.Value.Var;
            greater = more.Value.Var;
            return true;
        }

        public override void OnBind()
        {
            var chosen = source1.GetFloat() > source2.GetFloat() ? greater : lessOrEqual;
            var resultVar = result.Var;

            switch (resultVar.Type)
            {
                case Source1MaterialVarType.Float:
                    WriteFloat(result, chosen.GetFloat());
                    break;
                case Source1MaterialVarType.Int:
                    resultVar.SetInt(chosen.GetInt());
                    break;
                case Source1MaterialVarType.Vector when result.Component >= 0:
                    WriteFloat(result, chosen.GetFloat());
                    break;
                case Source1MaterialVarType.Vector:
                    Span<float> values = stackalloc float[4];
                    chosen.CopyVector(values);
                    resultVar.SetVector(values[..resultVar.VectorSize]);
                    break;
            }
        }
    }

    private sealed class TextureScrollProxy : Source1MaterialProxy
    {
        private Source1MaterialVar target = null!;
        private Source1FloatInput rate;
        private Source1FloatInput angle;
        private Source1FloatInput scale;
        private string targetName = "";

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            keys.TryGetValue("textureScrollVar", out var name);

            if (Owner.FindReference(name, create: false) is not { } reference)
            {
                return false;
            }

            target = reference.Var;
            targetName = reference.Name;
            Owner.MarkWritten(reference.Name);
            rate = Owner.ReadFloat(keys, "textureScrollRate", 1f);
            angle = Owner.ReadFloat(keys, "textureScrollAngle", 0f);
            scale = Owner.ReadFloat(keys, "textureScale", 1f);
            return true;
        }

        public override Vector2? GetScrollVelocity(string variable)
        {
            if (!variable.Equals(targetName, StringComparison.OrdinalIgnoreCase) || rate.Reference != null || angle.Reference != null)
            {
                return null;
            }

            var radians = angle.Constant * MathF.PI / 180f;
            return new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * rate.Constant;
        }

        public override void OnBind()
        {
            var radians = angle.Get() * MathF.PI / 180f;
            var distance = Owner.Time * rate.Get();
            var u = MathF.Cos(radians) * distance;
            var v = MathF.Sin(radians) * distance;
            u -= MathF.Floor(u);
            v -= MathF.Floor(v);

            if (target.Type != Source1MaterialVarType.Matrix)
            {
                target.SetVector([u, v, 0f]);
                return;
            }

            var scaleValue = scale.Get();
            target.SetMatrix(
            [
                scaleValue, 0f, 0f, u,
                0f, scaleValue, 0f, v,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f,
            ]);
        }
    }

    private sealed class TextureTransformProxy : Source1MaterialProxy
    {
        private Source1VarReference result;
        private Source1MaterialVar? center;
        private Source1MaterialVar? scale;
        private Source1MaterialVar? rotate;
        private Source1MaterialVar? translate;
        private readonly Dictionary<string, string> inputNames = [];

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (Owner.ReadResult(keys) is not { } resultReference)
            {
                return false;
            }

            result = resultReference;
            center = FindOptional(keys, "centerVar");
            scale = FindOptional(keys, "scaleVar");
            rotate = FindOptional(keys, "rotateVar");
            translate = FindOptional(keys, "translateVar");
            return true;
        }

        private Source1MaterialVar? FindOptional(IReadOnlyDictionary<string, string> keys, string key)
        {
            keys.TryGetValue(key, out var name);

            if (Owner.FindReference(name, create: false) is not { } reference)
            {
                return null;
            }

            inputNames[key] = reference.Name;
            return reference.Var;
        }

        // The translation is added after the rotation and scale, so a translation that grows with the time scrolls the result
        public override Vector2? GetScrollVelocity(string variable)
        {
            if (!variable.Equals(result.Name, StringComparison.OrdinalIgnoreCase) || result.Component >= 0)
            {
                return null;
            }

            foreach (var (key, name) in inputNames)
            {
                if (key != "translateVar" && Owner.IsWritten(name))
                {
                    return null;
                }
            }

            if (!inputNames.TryGetValue("translateVar", out var translateName))
            {
                return null;
            }

            return Owner.GetScrollVelocity(translateName);
        }

        public override void OnBind()
        {
            var centerValue = ReadPair(center, new Vector2(0.5f));
            var scaleValue = ReadPair(scale, Vector2.One);
            var translateValue = ReadPair(translate, Vector2.Zero);
            var radians = (rotate?.GetFloat() ?? 0f) * MathF.PI / 180f;
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);

            var m0 = cos * scaleValue.X;
            var m1 = -sin * scaleValue.Y;
            var m4 = sin * scaleValue.X;
            var m5 = cos * scaleValue.Y;
            var m3 = centerValue.X + translateValue.X - ((m0 * centerValue.X) + (m1 * centerValue.Y));
            var m7 = centerValue.Y + translateValue.Y - ((m4 * centerValue.X) + (m5 * centerValue.Y));

            result.Var.SetMatrix(
            [
                m0, m1, 0f, m3,
                m4, m5, 0f, m7,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f,
            ]);
        }

        private static Vector2 ReadPair(Source1MaterialVar? variable, Vector2 fallback)
        {
            if (variable is null || variable.Type == Source1MaterialVarType.Undefined)
            {
                return fallback;
            }

            if (variable.Type == Source1MaterialVarType.Vector && variable.VectorSize >= 2)
            {
                return new Vector2(variable.GetFloat(0), variable.GetFloat(1));
            }

            return new Vector2(variable.GetFloat());
        }
    }

    private sealed class MatrixRotateProxy : Source1MaterialProxy
    {
        private Source1VarReference result;
        private Source1MaterialVar? axis;
        private Source1FloatInput angle;

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            if (Owner.ReadResult(keys) is not { } resultReference)
            {
                return false;
            }

            keys.TryGetValue("axisVar", out var axisName);

            result = resultReference;
            axis = Owner.FindReference(axisName, create: false)?.Var;
            angle = Owner.ReadFloat(keys, "angle", 0f);
            return true;
        }

        public override void OnBind()
        {
            var direction = Vector3.UnitZ;

            if (axis is { Type: Source1MaterialVarType.Vector, VectorSize: >= 3 })
            {
                var candidate = new Vector3(axis.GetFloat(0), axis.GetFloat(1), axis.GetFloat(2));

                if (candidate.Length() >= 0.001f)
                {
                    direction = Vector3.Normalize(candidate);
                }
            }

            var radians = angle.Get() * MathF.PI / 180f;
            var c = MathF.Cos(radians);
            var s = MathF.Sin(radians);
            var t = 1f - c;
            var x = direction.X;
            var y = direction.Y;
            var z = direction.Z;

            result.Var.SetMatrix(
            [
                c + (x * x * t), (x * y * t) - (z * s), (x * z * t) + (y * s), 0f,
                (x * y * t) + (z * s), c + (y * y * t), (y * z * t) - (x * s), 0f,
                (x * z * t) - (y * s), (y * z * t) + (x * s), c + (z * z * t), 0f,
                0f, 0f, 0f, 1f,
            ]);
        }
    }

    private sealed class AnimatedTextureProxy : Source1MaterialProxy
    {
        private Source1MaterialVar texture = null!;
        private Source1MaterialVar frameVar = null!;
        private Source1FloatInput frameRate;
        private Source1FloatInput noWrap;
        private string textureVariable = "";
        private string frameVariable = "";
        private int frameCount = -1;

        protected override bool OnInit(IReadOnlyDictionary<string, string> keys)
        {
            keys.TryGetValue("animatedTextureVar", out var textureName);
            keys.TryGetValue("animatedTextureFrameNumVar", out var frameName);

            var textureReference = Owner.FindReference(textureName, create: false);
            var frameReference = Owner.FindReference(frameName, create: true);

            if (textureReference is null || frameReference is null)
            {
                return false;
            }

            Owner.MarkWritten(frameReference.Value.Name);
            textureVariable = textureReference.Value.Name;
            frameVariable = frameReference.Value.Name;
            texture = textureReference.Value.Var;
            frameVar = frameReference.Value.Var;
            frameRate = Owner.ReadFloat(keys, "animatedTextureFrameRate", 15f);
            noWrap = Owner.ReadFloat(keys, "animationNoWrap", 0f);
            return true;
        }

        // A wrapping animation at a constant rate is a repeating walk through the frames
        public override Source1TextureAnimation? GetAnimation(string frameName, string textureName)
        {
            var isTarget = frameName.Equals(frameVariable, StringComparison.OrdinalIgnoreCase)
                && textureName.Equals(textureVariable, StringComparison.OrdinalIgnoreCase);
            var isConstant = frameRate.Reference == null && noWrap.Reference == null;

            if (!isTarget || !isConstant || noWrap.Constant != 0f || frameRate.Constant <= 0f || frameCount < 2)
            {
                return null;
            }

            return new Source1TextureAnimation(frameCount, frameRate.Constant);
        }

        public override void OnBind()
        {
            if (frameCount < 0)
            {
                var texturePath = texture.Type == Source1MaterialVarType.String ? VmtFile.TryNormalizePath(texture.Text, Source1Paths.TextureExtension) : null;
                frameCount = texturePath != null ? Owner.GetFrameCount(texturePath) : 0;
            }

            if (frameCount <= 0)
            {
                return;
            }

            var index = (int)MathF.Floor(frameRate.Get() * Owner.Time);

            if (noWrap.Get() != 0f)
            {
                index = Math.Clamp(index, 0, frameCount - 1);
            }
            else
            {
                index = ((index % frameCount) + frameCount) % frameCount;
            }

            frameVar.SetInt(index);
        }
    }
}
