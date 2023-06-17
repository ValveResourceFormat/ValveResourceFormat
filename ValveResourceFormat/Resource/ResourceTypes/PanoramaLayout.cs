using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class PanoramaLayout : Panorama
    {
        private BinaryKV3 _layoutContent;

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            _layoutContent = resource.GetBlockByType(BlockType.LaCo) as BinaryKV3;
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
        public static string Print(IKeyValueCollection layoutRoot)
        {
            using var writer = new IndentedTextWriter();

            writer.WriteLine("<!-- xml reconstructed by ValveResourceFormat: https://vrf.steamdb.info/ -->");

            var root = layoutRoot.GetSubCollection("m_AST")?.GetSubCollection("m_pRoot");

            if (root == default)
            {
                throw new InvalidDataException("Unknown LaCo format, unable to format to XML");
            }

            PrintNode(root, writer);

            return writer.ToString();
        }

        private static void PrintNode(IKeyValueCollection node, IndentedTextWriter writer)
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

        private static void PrintPanel(IKeyValueCollection node, IndentedTextWriter writer)
        {
            var name = node.GetProperty<string>("name");
            PrintPanelBase(name, node, writer);
        }

        private static void PrintPanelBase(string name, IKeyValueCollection node, IndentedTextWriter writer)
        {
            var attributes = NodeAttributes(node);
            var nodeChildren = NodeChildren(node).ToList();

            if (!nodeChildren.Any())
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

        private static void PrintInclude(IKeyValueCollection node, IndentedTextWriter writer)
        {
            var reference = node.GetSubCollection("child");

            writer.Write($"<include src=");
            PrintAttributeOrReferenceValue(reference, writer);
            writer.WriteLine(" />");
        }

        private static void PrintScriptBody(IKeyValueCollection node, IndentedTextWriter writer)
        {
            var content = node.GetProperty<string>("name");

            writer.Write("<script><![CDATA[");
            writer.Write(content);
            writer.WriteLine("]]></script>");
        }

        private static void PrintSnippet(IKeyValueCollection node, IndentedTextWriter writer)
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

        private static void PrintOpenNode(string name, IEnumerable<IKeyValueCollection> attributes, string nodeEnding, IndentedTextWriter writer)
        {
            writer.Write($"<{name}");
            PrintAttributes(attributes, writer);
            writer.WriteLine(nodeEnding);
        }

        private static void PrintAttributes(IEnumerable<IKeyValueCollection> attributes, IndentedTextWriter writer)
        {
            foreach (var attribute in attributes)
            {
                var name = attribute.GetProperty<string>("name");
                var value = attribute.GetSubCollection("child");

                writer.Write($" {name}=");
                PrintAttributeOrReferenceValue(value, writer);
            }
        }

        private static void PrintAttributeOrReferenceValue(IKeyValueCollection attributeValue, IndentedTextWriter writer)
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

        private static bool IsAttribute(IKeyValueCollection node) => node.GetProperty<string>("eType") == "PANEL_ATTRIBUTE";
        private static IEnumerable<IKeyValueCollection> NodeAttributes(IKeyValueCollection node) => SubNodes(node).Where(n => IsAttribute(n));
        private static IEnumerable<IKeyValueCollection> NodeChildren(IKeyValueCollection node) => SubNodes(node).Where(n => !IsAttribute(n));


        private static IEnumerable<IKeyValueCollection> SubNodes(IKeyValueCollection node)
        {
            if (node.ContainsKey("vecChildren"))
            {
                return node.GetArray("vecChildren");
            }

            if (node.ContainsKey("child"))
            {
                return new[] { node.GetSubCollection("child") };
            }

            return Array.Empty<IKeyValueCollection>();
        }
    }
}
