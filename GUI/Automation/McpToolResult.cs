#if DEBUG
using System.Text.Json.Nodes;

namespace GUI.Automation;

/// <summary>
/// The result of one MCP tool call: content items (text or an inline PNG) plus whether the call
/// failed. Tool failures are reported this way rather than as JSON-RPC errors, which are reserved
/// for protocol problems.
/// </summary>
internal sealed class McpToolResult
{
    private readonly record struct Item(string? Text, byte[]? Data, string? MimeType);

    private readonly List<Item> Items = [];

    private bool isError;

    public static McpToolResult Text(string text)
    {
        var result = new McpToolResult();
        result.Items.Add(new Item(text, null, null));
        return result;
    }

    /// <summary>Serializes <paramref name="node"/> as the tool's text content.</summary>
    public static McpToolResult Json(JsonNode node) => Text(node.ToJsonString());

    public static McpToolResult Error(string message)
    {
        var result = Text(message);
        result.isError = true;
        return result;
    }

    public static McpToolResult Ok() => Text("ok");

    /// <summary>Appends an inline image to the content list.</summary>
    public McpToolResult WithImage(byte[] bytes, string mimeType)
    {
        Items.Add(new Item(null, bytes, mimeType));
        return this;
    }

    public JsonObject ToJson()
    {
        var content = new JsonArray();

        foreach (var item in Items)
        {
            var node = new JsonObject
            {
                ["type"] = item.Data == null ? "text" : "image",
            };

            if (item.Text != null)
            {
                node["text"] = item.Text;
            }

            if (item.Data != null)
            {
                node["data"] = Convert.ToBase64String(item.Data);
                node["mimeType"] = item.MimeType;
            }

            content.Add(node);
        }

        var result = new JsonObject
        {
            ["content"] = content,
        };

        if (isError)
        {
            result["isError"] = true;
        }

        return result;
    }
}
#endif
