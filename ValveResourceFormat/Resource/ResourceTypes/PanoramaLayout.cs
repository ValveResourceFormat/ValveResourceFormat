using System.IO;
using System.Linq;
using System.Security;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class PanoramaLayout : Panorama
    {
        private BinaryKV3 _layoutContent;

        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

            _layoutContent = Resource.GetBlockByType(BlockType.LaCo) as BinaryKV3;
        }

        public override string ToString()
        {
            if (_layoutContent == default)
            {
                return base.ToString();
            }
            else
            {
                return PanoramaLayoutPrinter.Print(_layoutContent.AsKeyValueCollection());
            }
        }
    }

    static class PanoramaLayoutPrinter
    {
        public static string Print(KVObject layoutRoot)
        {
            using var writer = new IndentedTextWriter();

            writer.WriteLine($"<!-- xml reconstructed by {StringToken.VRF_GENERATOR} -->");

            var root = layoutRoot.GetSubCollection("m_AST")?.GetSubCollection("m_pRoot");

            if (root == default)
            {
                throw new InvalidDataException("Unknown LaCo format, unable to format to XML");
            }

            PrintNode(root, writer);

            return writer.ToString();
        }

        private static void PrintNode(KVObject node, IndentedTextWriter writer)
        {
            var type = node.GetProperty<string>("eType");
            switch (type)
            {
                case "ROOT": PrintPanelBase("root", node, writer); break;
                case "STYLES": PrintPanelBase("styles", node, writer); break;
                case "INCLUDE": PrintInclude(node, writer); break;
                case "PANEL": PrintPanel(node, writer); break;
                case "SCRIPT_BODY": PrintScriptBody(node, writer); break;
                case "SCRIPTS": PrintPanelBase("scripts", node, writer); break;
                case "SNIPPET": PrintSnippet(node, writer); break;
                case "SNIPPETS": PrintPanelBase("snippets", node, writer); break;
                default: throw new UnexpectedMagicException("Unknown node type", type, nameof(type));
            };
        }

        private static void PrintPanel(KVObject node, IndentedTextWriter writer)
        {
            var name = node.GetProperty<string>("name");
            PrintPanelBase(name, node, writer);
        }

        private static void PrintPanelBase(string name, KVObject node, IndentedTextWriter writer)
        {
            var attributes = NodeAttributes(node);
            var nodeChildren = NodeChildren(node).ToList();

            if (nodeChildren.Count == 0)
            {
                PrintOpenNode(name, attributes, " />", writer);
                return;
            }

            PrintOpenNode(name, attributes, ">", writer);
            writer.Indent++;

            foreach (var child in nodeChildren)
            {
                PrintNode(child, writer);
            }

            writer.Indent--;
            writer.WriteLine($"</{name}>");
        }

        private static void PrintInclude(KVObject node, IndentedTextWriter writer)
        {
            var reference = node.GetSubCollection("child");

            writer.Write($"<include src=");
            PrintAttributeOrReferenceValue(reference, writer);
            writer.WriteLine(" />");
        }

        private static void PrintScriptBody(KVObject node, IndentedTextWriter writer)
        {
            var content = node.GetProperty<string>("name");

            writer.Write("<script><![CDATA[");
            writer.Write(content);
            writer.WriteLine("]]></script>");
        }

        private static void PrintSnippet(KVObject node, IndentedTextWriter writer)
        {
            var nodeChildren = NodeChildren(node);

            var name = node.GetProperty<string>("name");

            writer.WriteLine($"<snippet name=\"{name}\">");
            writer.Indent++;

            foreach (var child in nodeChildren)
            {
                PrintNode(child, writer);
            }

            writer.Indent--;
            writer.WriteLine("</snippet>");
        }

        private static void PrintOpenNode(string name, IEnumerable<KVObject> attributes, string nodeEnding, IndentedTextWriter writer)
        {
            writer.Write($"<{name}");
            PrintAttributes(attributes, writer);
            writer.WriteLine(nodeEnding);
        }

        private static void PrintAttributes(IEnumerable<KVObject> attributes, IndentedTextWriter writer)
        {
            foreach (var attribute in attributes)
            {
                var name = attribute.GetProperty<string>("name");
                var value = attribute.GetSubCollection("child");

                writer.Write($" {name}=");
                PrintAttributeOrReferenceValue(value, writer);
            }
        }

        private static void PrintAttributeOrReferenceValue(KVObject attributeValue, IndentedTextWriter writer)
        {
            var value = attributeValue.GetProperty<string>("name");
            var type = attributeValue.GetProperty<string>("eType");

            value = type switch
            {
                "REFERENCE_COMPILED" => "s2r://" + value,
                "REFERENCE_PASSTHROUGH" => "file://" + value,
                "PANEL_ATTRIBUTE_VALUE" => SecurityElement.Escape(value),
                _ => throw new UnexpectedMagicException("Unknown node type", type, nameof(type)),
            };

            writer.Write($"\"{value}\"");
        }

        private static bool IsAttribute(KVObject node) => node.GetProperty<string>("eType") == "PANEL_ATTRIBUTE";
        private static IEnumerable<KVObject> NodeAttributes(KVObject node) => SubNodes(node).Where(n => IsAttribute(n));
        private static IEnumerable<KVObject> NodeChildren(KVObject node) => SubNodes(node).Where(n => !IsAttribute(n));

        private static KVObject[] SubNodes(KVObject node)
        {
            if (node.ContainsKey("vecChildren"))
            {
                return node.GetArray("vecChildren");
            }

            if (node.ContainsKey("child"))
            {
                return [node.GetSubCollection("child")];
            }

            return [];
        }
    }
}
