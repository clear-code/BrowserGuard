using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BrowserGuard.NetLogger
{
    // The browser sends its entries as JSON text. They are checked and put back
    // onto a single line here, once, before going to the file and the collector.
    internal static class NetLogEntry
    {
        // The names the operation log read beside this one uses, so that the
        // two can be lined up. It has no field for the machine, so "host" is
        // this side's own.
        internal const string HostProperty = "host";
        internal const string UserProperty = "user";
        internal const string DisplayNameProperty = "user_displayName";
        internal const string SessionProperty = "session";

        // The log is read back as text, so the escaping is relaxed to leave
        // Japanese page titles legible rather than as \uXXXX escapes.
        private static readonly JsonWriterOptions WriterOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // An entry the host makes for itself, in the shape the browser sends
        // its own. It goes back through Compact like any other, so that the
        // machine and the user are stamped on it in one place.
        internal static string UploadFileBridgeFailed(
            string file, string url, string reason, DateTime at)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("operation", "upload-file-bridge");
                writer.WriteString("name", file);
                writer.WriteString("url", url);
                writer.WriteString("timestamp",
                    at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                writer.WriteString("reason", reason);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

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

                // Rewriting puts the entry on a single line whatever the sender
                // did with its whitespace.
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                {
                    writer.WriteStartObject();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        // Whatever the sender put here is replaced below: only
                        // this side can say where the entry was recorded.
                        if (property.NameEquals(HostProperty) ||
                            property.NameEquals(UserProperty) ||
                            property.NameEquals(DisplayNameProperty) ||
                            property.NameEquals(SessionProperty))
                        {
                            continue;
                        }
                        property.WriteTo(writer);
                    }
                    writer.WriteString(HostProperty, NetLogIdentity.Host);
                    writer.WriteString(UserProperty, NetLogIdentity.Account);
                    // Left out when it cannot be had, as the operation log does.
                    if (NetLogIdentity.DisplayName.Length > 0)
                    {
                        writer.WriteString(DisplayNameProperty, NetLogIdentity.DisplayName);
                    }
                    writer.WriteNumber(SessionProperty, NetLogIdentity.Session);
                    writer.WriteEndObject();
                }
                line = Encoding.UTF8.GetString(buffer.ToArray());
                return null;
            }
            catch (JsonException ex)
            {
                return $"entry is not valid JSON: {ex.Message}";
            }
        }
    }
}
