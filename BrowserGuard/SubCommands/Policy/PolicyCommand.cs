using System;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace BrowserGuard.SubCommands.Policy
{
    // Maintains this extension's entry in the Edge ExtensionSettings policy,
    // invoked by the installer as a subcommand.
    //
    // ExtensionInstallForcelist would only supply the update URL for the first
    // install; afterwards the browser looks at update_url inside the extension's
    // own manifest, which a self hosted build does not have. ExtensionSettings
    // with override_update_url keeps the URL in use for update checks too, which
    // is why it is used on its own.
    //
    // The policy value is one JSON object covering every extension, so it is
    // parsed and only this extension's member is touched; whatever another
    // product put there is preserved.
    internal static class PolicyCommand
    {
        internal const string CommandName = "policy";

        // SOFTWARE\Policies is shared between the 32 and 64 bit registry views.
        private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Edge";
        private const string SettingsValue = "ExtensionSettings";

        internal static int Run(string[] args)
        {
            try
            {
                if (args.Length < 3)
                {
                    return Usage();
                }

                var extensionId = args[2];
                switch (args[1].ToLowerInvariant())
                {
                    case "register":
                        if (args.Length < 4)
                        {
                            return Usage();
                        }
                        Register(extensionId, args[3]);
                        return 0;

                    case "unregister":
                        Unregister(extensionId);
                        return 0;

                    default:
                        return Usage();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine(
                $"usage: BrowserGuard.exe {CommandName} register <extension-id> <update-url>\n" +
                $"       BrowserGuard.exe {CommandName} unregister <extension-id>");
            return 1;
        }

        // Separated from the registry access so that it can be exercised by tests.
        internal static void SetEntry(JsonObject settings, string extensionId, string updateUrl)
        {
            settings[extensionId] = new JsonObject
            {
                ["installation_mode"] = "force_installed",
                ["update_url"] = updateUrl,
                ["override_update_url"] = true,
            };
        }

        private static void Register(string extensionId, string updateUrl)
        {
            var settings = ReadSettings();
            SetEntry(settings, extensionId, updateUrl);
            WriteSettings(settings);
        }

        private static void Unregister(string extensionId)
        {
            var settings = ReadSettings();
            if (settings.Remove(extensionId))
            {
                WriteSettings(settings);
            }
        }

        private static JsonObject ReadSettings()
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
            if (key?.GetValue(SettingsValue) is not string raw || string.IsNullOrWhiteSpace(raw))
            {
                return new JsonObject();
            }

            // A value that is not a JSON object cannot be merged into safely.
            return JsonNode.Parse(raw) as JsonObject
                ?? throw new InvalidDataException($"{SettingsValue} is not a JSON object.");
        }

        private static void WriteSettings(JsonObject settings)
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyKey, true)
                ?? throw new InvalidOperationException($"Cannot open HKLM\\{PolicyKey} for writing.");

            if (settings.Count == 0)
            {
                if (key.GetValue(SettingsValue) is not null)
                {
                    key.DeleteValue(SettingsValue);
                }
                return;
            }

            key.SetValue(SettingsValue, settings.ToJsonString(), RegistryValueKind.String);
        }
    }
}
