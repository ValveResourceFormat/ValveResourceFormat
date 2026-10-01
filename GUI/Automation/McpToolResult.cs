#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GUI.Automation;

/// <summary>
/// The result of one MCP tool call: content items (JSON text or an inline image) plus whether the
/// call failed. Tool failures are reported this way rather than as JSON-RPC errors, which are reserved
/// for protocol problems.
/// </summary>
internal sealed class McpToolResult
{
    private readonly record struct Item(string? Text, byte[]? Data, string? MimeType);

    private readonly List<Item> Items = [];

    private bool isError;

    /// <summary>Whether the call failed.</summary>
    public bool IsError => isError;

    /// <summary>The first line of the first text item, for a one line log entry.</summary>
    public string Summary
    {
        get
        {
            var text = Items.Find(item => item.Text != null).Text ?? string.Empty;
            var end = text.AsSpan().IndexOfAny('\r', '\n');

            return end < 0 ? text : text[..end];
        }
    }

    private static McpToolResult Text(string text)
    {
        var result = new McpToolResult();
        result.Items.Add(new Item(ToAscii(text), null, null));
        return result;
    }

    /// <summary>
    /// Writes every character outside ASCII as a <c>\uXXXX</c> escape, which a JSON reader turns
    /// back into the same text, so that replies are ASCII whatever paths and log lines they quote.
    /// </summary>
    private static string ToAscii(string text)
    {
        var first = text.AsSpan().IndexOfAnyExceptInRange((char)0, (char)0x7F);

        if (first < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        builder.Append(text.AsSpan(0, first));

        for (var i = first; i < text.Length; i++)
        {
            var c = text[i];

            if (c > 0x7F)
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // The text is read by a model, not put in a page, so there is nothing to gain from escaping
    // quotes or angle brackets, and every escape costs tokens. Non-ASCII is escaped afterwards.
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes <paramref name="node"/> as the tool's text content. Every successful call answers this way.</summary>
    public static McpToolResult Json(JsonNode node) => Text(node.ToJsonString(SerializerOptions));

    /// <summary>A failed call, answered with plain text that says what went wrong.</summary>
    public static McpToolResult Error(string message)
    {
        var result = Text(message);
        result.isError = true;
        return result;
    }

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
