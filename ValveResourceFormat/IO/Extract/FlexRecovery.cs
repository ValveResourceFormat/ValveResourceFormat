using System.Globalization;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the authoring side of a compiled morph set: the flex controllers, the morph targets each
/// one drives, and the rule of every target as an infix expression.
/// </summary>
internal sealed class FlexRecovery
{
    /// <summary>The side of a split controller a morph target sits on.</summary>
    public enum Side
    {
        /// <summary>The controller drives the target across its whole range.</summary>
        Whole,
        /// <summary>The target follows the controller above zero.</summary>
        Positive,
        /// <summary>The target follows the controller below zero.</summary>
        Negative,
    }

    /// <summary>One recovered flex controller and the morph targets it drives.</summary>
    public sealed class Control
    {
        public string Name { get; init; } = string.Empty;
        public float Min { get; init; }
        public float Max { get; init; }
        public string? Negative { get; set; }
        public string? Positive { get; set; }
        public string? Whole { get; set; }

        /// <summary>
        /// The target names in the order a combination operator expects them, negative side first.
        /// </summary>
        public List<string> RawControlNames
        {
            get
            {
                if (Whole != null)
                {
                    return [Whole];
                }

                var names = new List<string>(2);
                if (Negative != null)
                {
                    names.Add(Negative);
                }

                if (Positive != null)
                {
                    names.Add(Positive);
                }

                return names;
            }
        }
    }

    private abstract record Node;
    private sealed record ConstNode(float Value) : Node;
    private sealed record CtrlNode(int Index) : Node;
    private sealed record OpNode(FlexOpCode Op, Node Left, Node Right) : Node;
    private sealed record RawNode(FlexOpCode Op, int Data) : Node;

    /// <summary>A trapezoid over one controller, scaling the value of another.</summary>
    private sealed record NWayNode(int Selector, int Target, Node E0, Node E1, Node E2, Node E3) : Node;

    /// <summary>Recovered controllers, in flex controller order.</summary>
    public Control[] Controls { get; }

    /// <summary>The rule of each morph target, printed as an infix expression.</summary>
    public Dictionary<string, string> Expressions { get; } = [];

    private readonly string[] flexNames;
    private readonly Node?[] rules;

    public FlexRecovery(Morph morph)
    {
        flexNames = [.. (morph.Data.GetArray("m_FlexDesc") ?? []).Select(d => d.GetStringProperty("m_szFacs") ?? string.Empty)];

        var controllers = morph.Data.GetArray("m_FlexControllers") ?? [];
        Controls = [.. controllers.Select(c => new Control
        {
            Name = c.GetStringProperty("m_szName") ?? string.Empty,
            Min = c.GetFloatProperty("min"),
            Max = c.GetFloatProperty("max"),
        })];

        rules = new Node?[flexNames.Length];

        // A morph set can hold several rules for the same target. They are usually byte identical, but
        // where they are not the engine keeps the last one, so take that.
        foreach (var rule in morph.Data.GetArray("m_FlexRules") ?? [])
        {
            var flexId = rule.GetInt32Property("m_nFlex");
            if (flexId < 0 || flexId >= rules.Length)
            {
                continue;
            }

            rules[flexId] = BuildTree(rule.GetArray("m_FlexOps") ?? []);
        }

        Recover();
    }

    private static Node? BuildTree(IReadOnlyList<KVObject> ops)
    {
        var stack = new Stack<Node>();

        foreach (var op in ops)
        {
            if (!op.TryGetValue("m_OpCode", out var opCodeValue))
            {
                return null;
            }

            var opCode = FlexOp.ParseOpCode(opCodeValue);
            var data = op.GetInt32Property("m_Data");

            switch (opCode)
            {
                case FlexOpCode.Const:
                    stack.Push(new ConstNode(BitConverter.Int32BitsToSingle(data)));
                    break;
                case FlexOpCode.Fetch1:
                    stack.Push(new CtrlNode(data));
                    break;
                case FlexOpCode.Add or FlexOpCode.Sub or FlexOpCode.Mul or FlexOpCode.Div
                    or FlexOpCode.Min or FlexOpCode.Max:
                    if (stack.Count < 2)
                    {
                        return null;
                    }

                    var right = stack.Pop();
                    var left = stack.Pop();
                    stack.Push(new OpNode(opCode.Value, left, right));
                    break;
                case FlexOpCode.NWay:
                    if (stack.Count < 5)
                    {
                        return null;
                    }

                    // The selector rides the stack as a controller index bit-cast into a float.
                    var selector = stack.Pop() is ConstNode s0 ? BitConverter.SingleToInt32Bits(s0.Value) : -1;
                    var e3 = stack.Pop();
                    var e2 = stack.Pop();
                    var e1 = stack.Pop();
                    var e0 = stack.Pop();

                    if (selector < 0)
                    {
                        return null;
                    }

                    stack.Push(new NWayNode(selector, data, e0, e1, e2, e3));
                    break;

                default:
                    stack.Push(new RawNode(opCode ?? FlexOpCode.Const, data));
                    break;
            }
        }

        return stack.Count == 1 ? stack.Pop() : null;
    }

    private static bool IsConst(Node node, float value)
        => node is ConstNode c && MathF.Abs(c.Value - value) < 1e-6f;

    /// <summary>
    /// Peels <c>max(X - sum((X * w) * D), 0)</c> back to the target expression X. The compiler rebuilds
    /// the suppression from the expression itself, so the dominators peeled off here are not kept.
    /// </summary>
    private static Node StripDomination(Node node)
    {
        if (node is not OpNode { Op: FlexOpCode.Max } max || !IsConst(max.Right, 0f)
            || max.Left is not OpNode { Op: FlexOpCode.Sub } sub)
        {
            return node;
        }

        var terms = new List<Node>();
        Flatten(sub.Right, terms);

        if (terms.Count == 0)
        {
            return node;
        }

        foreach (var term in terms)
        {
            if (term is not OpNode { Op: FlexOpCode.Mul } product
                || product.Left is not OpNode { Op: FlexOpCode.Mul } scale
                || scale.Left != sub.Left
                || scale.Right is not ConstNode)
            {
                return node;
            }
        }

        return sub.Left;

        static void Flatten(Node node, List<Node> into)
        {
            if (node is OpNode { Op: FlexOpCode.Add } add)
            {
                Flatten(add.Left, into);
                Flatten(add.Right, into);
                return;
            }

            into.Add(node);
        }
    }

    /// <summary>
    /// Reads a single controller reference, either the whole range or one half of a split control.
    /// </summary>
    private static (int Index, Side Side)? AsRaw(Node node)
    {
        switch (node)
        {
            case CtrlNode ctrl:
                return (ctrl.Index, Side.Whole);

            case OpNode { Op: FlexOpCode.Max } max when IsConst(max.Right, 0f) && max.Left is CtrlNode a:
                return (a.Index, Side.Positive);

            case OpNode { Op: FlexOpCode.Max } max2 when IsConst(max2.Left, 0f) && max2.Right is CtrlNode b:
                return (b.Index, Side.Positive);

            case OpNode { Op: FlexOpCode.Mul } mul when IsConst(mul.Right, -1f)
                && mul.Left is OpNode { Op: FlexOpCode.Min } min:
                if (IsConst(min.Right, 0f) && min.Left is CtrlNode c)
                {
                    return (c.Index, Side.Negative);
                }

                if (IsConst(min.Left, 0f) && min.Right is CtrlNode d)
                {
                    return (d.Index, Side.Negative);
                }

                return null;

            default:
                return null;
        }
    }

    private static void Factors(Node node, List<Node> into)
    {
        if (node is OpNode { Op: FlexOpCode.Mul } mul)
        {
            Factors(mul.Left, into);
            Factors(mul.Right, into);
            return;
        }

        into.Add(node);
    }

    private void Recover()
    {
        for (var flexId = 0; flexId < flexNames.Length; flexId++)
        {
            var tree = rules[flexId];
            var name = flexNames[flexId];

            if (tree == null)
            {
                continue;
            }

            // A rule using an op with no infix form cannot be written back as an expression. Leaving it
            // out is honest; printing a zero would silently claim the flex never fires.
            if (IsPrintable(tree))
            {
                Expressions[name] = Print(tree);
            }

            var factors = new List<Node>();
            Factors(tree, factors);

            var parts = new List<(int Index, Side Side)>(factors.Count);
            var ok = true;

            foreach (var factor in factors)
            {
                var raw = AsRaw(StripDomination(factor));
                if (raw == null)
                {
                    ok = false;
                    break;
                }

                parts.Add(raw.Value);
            }

            if (!ok)
            {
                continue;
            }

            // A target driven by several controls at once is a combination shape, which the compiler
            // rebuilds from the expression rather than from a control of its own.
            if (parts.Count > 1)
            {
                continue;
            }

            var (index, side) = parts[0];
            if (index < 0 || index >= Controls.Length)
            {
                continue;
            }

            var control = Controls[index];

            switch (side)
            {
                case Side.Whole: control.Whole ??= name; break;
                case Side.Positive: control.Positive ??= name; break;
                case Side.Negative: control.Negative ??= name; break;
            }
        }
    }

    private bool IsPrintable(Node node)
    {
        return node switch
        {
            ConstNode or CtrlNode => true,
            OpNode op => IsPrintable(op.Left) && IsPrintable(op.Right),
            NWayNode n => IsExpressibleRamp(n),
            _ => false,
        };
    }

    /// <summary>
    /// A trapezoid edge of zero width is a step, which arithmetic cannot spell. It only matters when
    /// the step sits inside the range the controller can actually take.
    /// </summary>
    private bool IsExpressibleRamp(NWayNode nway)
    {
        if (nway.E0 is not ConstNode e0 || nway.E1 is not ConstNode e1
            || nway.E2 is not ConstNode e2 || nway.E3 is not ConstNode e3
            || nway.Selector < 0 || nway.Selector >= Controls.Length)
        {
            return false;
        }

        var control = Controls[nway.Selector];

        var riseIsStepInRange = e1.Value <= e0.Value && e0.Value >= control.Min;
        var fallIsStepInRange = e3.Value <= e2.Value && e3.Value <= control.Max;

        return !riseIsStepInRange && !fallIsStepInRange;
    }

    /// <summary>
    /// Rewrites a trapezoid as <c>min(rise, fall) * target</c>, which the expression compiler can spell
    /// where a multi way blend cannot be spelled directly.
    /// </summary>
    private static OpNode RewriteNWay(NWayNode nway)
    {
        var e0 = ((ConstNode)nway.E0).Value;
        var e1 = ((ConstNode)nway.E1).Value;
        var e2 = ((ConstNode)nway.E2).Value;
        var e3 = ((ConstNode)nway.E3).Value;

        var selector = new CtrlNode(nway.Selector);

        Node Clamped(Node numerator, float span)
            => new OpNode(FlexOpCode.Min,
                new OpNode(FlexOpCode.Max,
                    new OpNode(FlexOpCode.Div, numerator, new ConstNode(span)),
                    new ConstNode(0f)),
                new ConstNode(1f));

        var rise = e1 > e0
            ? Clamped(new OpNode(FlexOpCode.Sub, selector, new ConstNode(e0)), e1 - e0)
            : new ConstNode(1f);

        var fall = e3 > e2
            ? Clamped(new OpNode(FlexOpCode.Sub, new ConstNode(e3), selector), e3 - e2)
            : new ConstNode(1f);

        return new OpNode(FlexOpCode.Mul,
            new OpNode(FlexOpCode.Min, rise, fall),
            new CtrlNode(nway.Target));
    }

    /// <summary>
    /// The expression grammar only takes identifier characters, and the compiler rewrites a control's
    /// name the same way before matching, so a name carrying a hyphen or a bracket has to be rewritten
    /// here too or the reference does not resolve.
    /// </summary>
    public static string Identifier(string name)
    {
        var chars = name.ToCharArray();

        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private string Print(Node node)
    {
        var sb = new StringBuilder();
        Print(node, sb);
        return sb.ToString();
    }

    private void Print(Node node, StringBuilder sb)
    {
        switch (node)
        {
            case ConstNode c:
                sb.Append(c.Value.ToString("0.######", CultureInfo.InvariantCulture));
                break;

            case CtrlNode ctrl:
                sb.Append(ctrl.Index >= 0 && ctrl.Index < Controls.Length
                    ? Identifier(Controls[ctrl.Index].Name)
                    : FormattableString.Invariant($"controller{ctrl.Index}"));
                break;

            case NWayNode nway:
                Print(RewriteNWay(nway), sb);
                break;

            case OpNode { Op: FlexOpCode.Min or FlexOpCode.Max } fn:
                sb.Append(fn.Op == FlexOpCode.Min ? "min(" : "max(");
                Print(fn.Left, sb);
                sb.Append(", ");
                Print(fn.Right, sb);
                sb.Append(')');
                break;

            case OpNode op:
                sb.Append('(');
                Print(op.Left, sb);
                sb.Append(op.Op switch
                {
                    FlexOpCode.Add => " + ",
                    FlexOpCode.Sub => " - ",
                    FlexOpCode.Mul => " * ",
                    _ => " / ",
                });
                Print(op.Right, sb);
                sb.Append(')');
                break;

            default:
                sb.Append('0');
                break;
        }
    }
}
