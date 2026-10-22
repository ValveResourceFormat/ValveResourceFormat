using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>The kinds of value a particle system named value holds.</summary>
    internal enum ParticleNamedValueType
    {
        /// <summary>A type particle inputs cannot read, such as an entity handle.</summary>
        Other,
        /// <summary>PVAL_FLOAT.</summary>
        Float,
        /// <summary>PVAL_VEC3.</summary>
        Vector,
        /// <summary>PVAL_TRANSFORM, a position and a rotation.</summary>
        Transform,
    }

    /// <summary>A typed named value. A transform keeps its position in <see cref="Vector"/>.</summary>
    internal readonly record struct ParticleNamedValue(ParticleNamedValueType Type, float Float, Vector3 Vector, Quaternion Rotation)
    {
        public static ParticleNamedValue FromFloat(float value) => new(ParticleNamedValueType.Float, value, Vector3.Zero, Quaternion.Identity);

        public static ParticleNamedValue FromVector(Vector3 value) => new(ParticleNamedValueType.Vector, 0f, value, Quaternion.Identity);

        public static ParticleNamedValue FromTransform(Vector3 position, Quaternion rotation) => new(ParticleNamedValueType.Transform, 0f, position, rotation);

        public static ParticleNamedValue Default(ParticleNamedValueType type) => new(type, 0f, Vector3.Zero, Quaternion.Identity);

        /// <summary>This value as <paramref name="type"/>, or null when it does not convert.</summary>
        public ParticleNamedValue? ConvertTo(ParticleNamedValueType type) => (Type, type) switch
        {
            (ParticleNamedValueType.Other, _) or (_, ParticleNamedValueType.Other) => null,
            _ when Type == type => this,
            (ParticleNamedValueType.Vector, ParticleNamedValueType.Transform) => FromTransform(Vector, Quaternion.Identity),
            (ParticleNamedValueType.Transform, ParticleNamedValueType.Vector) => FromVector(Vector),
            _ => null,
        };
    }

    /// <summary>
    /// The named values of one system. The names are the definition's <c>m_NamedValueLocals</c>, the
    /// names its inputs read, and the names of every child below it, each with the first type it was
    /// declared with. Three tables hold the values: the current one, the one captured on the first
    /// step after the system starts or restarts, and the previous step's.
    /// </summary>
    internal sealed class ParticleNamedValues
    {
        private static readonly string[] FunctionLists =
        [
            "m_PreEmissionOperators", "m_Emitters", "m_Initializers", "m_Operators",
            "m_ForceGenerators", "m_Constraints", "m_Renderers",
        ];

        private readonly Dictionary<string, int> slots = [];
        private readonly List<(string Name, ParticleNamedValueType Type)> members = [];
        private readonly List<(string Name, ParticleNamedValue Value)> locals = [];

        private ParticleNamedValue[] current;
        private ParticleNamedValue[] previous;
        private ParticleNamedValue[] initial;
        private readonly bool[] assigned;

        private bool capturePending = true;

        /// <summary>The declared names and their types.</summary>
        public IReadOnlyList<(string Name, ParticleNamedValueType Type)> Members => members;

        /// <summary>The <c>m_NamedValueLocals</c> that carry a default value, in definition order.</summary>
        public IReadOnlyList<(string Name, ParticleNamedValue Value)> Locals => locals;

        public ParticleNamedValues(KVObject definition, IEnumerable<ParticleNamedValues> children)
        {
            foreach (var local in definition.GetArray("m_NamedValueLocals") ?? [])
            {
                var name = local.GetStringProperty("m_Name");
                var type = local.GetStringProperty("m_ValueType") switch
                {
                    "PVAL_FLOAT" => ParticleNamedValueType.Float,
                    "PVAL_VEC3" => ParticleNamedValueType.Vector,
                    "PVAL_TRANSFORM" => ParticleNamedValueType.Transform,
                    null or "PVAL_INVALID" or "PVAL_VOID" => (ParticleNamedValueType?)null,
                    _ => ParticleNamedValueType.Other,
                };

                if (string.IsNullOrEmpty(name) || type is not { } valueType)
                {
                    continue;
                }

                AddMember(name, valueType);

                var config = local.GetSubCollection("m_DefaultConfig");

                if (config != null && config.TryGetValue("m_ConfigValue", out var configValue) && ReadDefault(valueType, configValue) is { } value)
                {
                    locals.Add((name, value));
                }
            }

            foreach (var list in FunctionLists)
            {
                foreach (var function in definition.GetArray(list) ?? [])
                {
                    AddInputReferences(function);
                }
            }

            foreach (var child in children)
            {
                foreach (var (name, type) in child.Members)
                {
                    AddMember(name, type);
                }
            }

            current = new ParticleNamedValue[members.Count];
            previous = new ParticleNamedValue[members.Count];
            initial = new ParticleNamedValue[members.Count];
            assigned = new bool[members.Count];
            Reset();
        }

        private void AddMember(string name, ParticleNamedValueType type)
        {
            if (slots.TryAdd(name, members.Count))
            {
                members.Add((name, type));
            }
        }

        private void AddInputReferences(KVObject node)
        {
            if (node.ValueType == KVValueType.Collection
                && node.GetStringProperty("m_NamedValue") is { Length: > 0 } name
                && node.GetStringProperty("m_nType") is { } inputType)
            {
                switch (inputType)
                {
                    case "PF_TYPE_NAMED_VALUE":
                        AddMember(name, ParticleNamedValueType.Float);
                        break;
                    case "PVEC_TYPE_NAMED_VALUE":
                        AddMember(name, ParticleNamedValueType.Vector);
                        break;
                    case "PT_TYPE_NAMED_VALUE":
                        AddMember(name, ParticleNamedValueType.Transform);
                        break;
                    case "PM_TYPE_NAMED_VALUE_MODEL" or "PM_TYPE_NAMED_VALUE_EHANDLE":
                        AddMember(name, ParticleNamedValueType.Other);
                        break;
                }
            }

            foreach (var (_, value) in node.Children)
            {
                if (value is { ValueType: KVValueType.Collection or KVValueType.Array })
                {
                    AddInputReferences(value);
                }
            }
        }

        private static ParticleNamedValue? ReadDefault(ParticleNamedValueType type, KVObject value)
        {
            switch (type)
            {
                case ParticleNamedValueType.Float when value.ValueType is KVValueType.FloatingPoint or KVValueType.FloatingPoint64
                    or KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64 or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64:
                    return ParticleNamedValue.FromFloat((float)value);
                case ParticleNamedValueType.Vector when value.IsArray && value.Count >= 3:
                    return ParticleNamedValue.FromVector(value.ToVector3());
                case ParticleNamedValueType.Transform when value.ValueType == KVValueType.Collection:
                {
                    var position = value.ContainsKey("position") ? value.GetSubCollection("position").ToVector3() : Vector3.Zero;
                    var angles = value.ContainsKey("angles") ? value.GetSubCollection("angles").ToVector3() : Vector3.Zero;
                    return ParticleNamedValue.FromTransform(position, EntityTransformHelper.EulerAnglesToQuaternion(angles));
                }
                case ParticleNamedValueType.Transform when value.IsArray && value.Count is 7 or 8:
                {
                    var (position, _, rotation) = value.ToTransform();
                    return ParticleNamedValue.FromTransform(position, rotation);
                }
                default:
                    return null;
            }
        }

        /// <summary>Puts every value back to its type's default, to be captured again on the next step.</summary>
        public void Reset()
        {
            for (var i = 0; i < members.Count; i++)
            {
                current[i] = previous[i] = initial[i] = ParticleNamedValue.Default(members[i].Type);
            }

            Array.Clear(assigned);

            capturePending = true;
        }

        /// <summary>Makes the next step capture the values it starts with again, as after a restart.</summary>
        public void Recapture() => capturePending = true;

        /// <summary>
        /// Writes <paramref name="value"/> into the current table, converted to the declared type.
        /// </summary>
        /// <returns>Whether the name is declared and the value converts.</returns>
        public bool Set(string name, ParticleNamedValue value)
        {
            if (!slots.TryGetValue(name, out var slot) || value.ConvertTo(members[slot].Type) is not { } converted)
            {
                return false;
            }

            current[slot] = converted;
            assigned[slot] = true;
            return true;
        }

        /// <summary>Run at the start of every frame: the first one after a start or restart captures the values it starts with.</summary>
        public void BeginFrame()
        {
            if (!capturePending)
            {
                return;
            }

            capturePending = false;
            Array.Copy(current, previous, current.Length);
            Array.Copy(current, initial, current.Length);
        }

        /// <summary>Run at the end of every simulated frame: the current and previous tables trade places.</summary>
        public void EndFrame() => (current, previous) = (previous, current);

        /// <summary>
        /// The current float value, when a local default or a host has given the name one and it converts.
        /// </summary>
        public bool TryGetFloat(string name, out float value)
        {
            if (slots.TryGetValue(name, out var slot) && assigned[slot] && current[slot].ConvertTo(ParticleNamedValueType.Float) is { } found)
            {
                value = found.Float;
                return true;
            }

            value = 0f;
            return false;
        }

        /// <summary>
        /// The current vector value, or the one captured when the system started unless
        /// <paramref name="follow"/>. Zero when it is not declared or does not convert.
        /// </summary>
        public Vector3 GetVector(string name, bool follow)
            => Get(follow ? current : initial, name, ParticleNamedValueType.Vector)?.Vector ?? Vector3.Zero;

        /// <summary>
        /// The current transform, or the one captured when the system started unless
        /// <paramref name="follow"/>, and the previous step's.
        /// </summary>
        /// <returns>Whether the name is declared and the value converts.</returns>
        public bool TryGetTransform(string name, bool follow, out ParticleNamedValue value, out ParticleNamedValue previousValue)
        {
            if (Get(follow ? current : initial, name, ParticleNamedValueType.Transform) is { } found
                && Get(previous, name, ParticleNamedValueType.Transform) is { } foundPrevious)
            {
                value = found;
                previousValue = foundPrevious;
                return true;
            }

            value = previousValue = ParticleNamedValue.Default(ParticleNamedValueType.Transform);
            return false;
        }

        private ParticleNamedValue? Get(ParticleNamedValue[] table, string name, ParticleNamedValueType type)
            => slots.TryGetValue(name, out var slot) ? table[slot].ConvertTo(type) : null;
    }
}
