using System.Text.Encodings.Web;
using System.Text.Json;

namespace BrowserGuard
{
    // The browser sends its entries as JSON text. They are checked and put back
    // onto a single line here, once, before going to the file and the collector.
    internal static class NetLogEntry
    {
        // The file is read back as text, so the escaping is relaxed to leave
        // Japanese page titles legible rather than as \uXXXX escapes.
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // null when the entry is usable, and `line` holds it. Otherwise the
        // reason it cannot be used.
        internal static string? Compact(string entry, out string line)
        {
            line = "";
            try
            {
                using var document = JsonDocument.Parse(entry);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "entry is not a JSON object";
                }
                // Reserializing puts the entry on a single line whatever the
                // sender did with its whitespace.
                line = JsonSerializer.Serialize(document.RootElement, WriteOptions);
                return null;
            }
            catch (JsonException ex)
            {
                return $"entry is not valid JSON: {ex.Message}";
            }
        }
    }
}
