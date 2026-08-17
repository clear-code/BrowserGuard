/*
This Source Code Form is subject to the terms of the Mozilla Public
License, v. 2.0. If a copy of the MPL was not distributed with this
file, You can obtain one at http://mozilla.org/MPL/2.0/.

Copyright (c) 2025 ClearCode Inc.
*/
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BrowserGuard
{
    internal class Config
    {
        public NetLoggerConfig NetLogger { get; set; } = new();
        public UploadGuardConfig UploadGuard { get; set; } = new();
        public SettingPageFilterConfig SettingPageFilter { get; set; } = new();
        public StartupLauncherConfig StartupLauncher { get; set; } = new();
    }

    internal class NetLoggerConfig
    {
        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = "";
        public bool UrlAccess { get; set; }
        public bool Browsing { get; set; }
        public bool Upload { get; set; }
        public bool Download { get; set; }
        public bool Auth { get; set; }
        public bool Print { get; set; }
        public string UserName { get; set; } = Environment.UserName;
        public string MachineName { get; set; } = Environment.MachineName;
        public NetLogFileConfig LocalFile { get; set; } = new();
        public NetLogFailureConfig OnSendFailure { get; set; } = new();
    }

    // What becomes of an entry the collector would not take, including one
    // dropped because the queue waiting for the collector was full.
    internal class NetLogFailureConfig
    {
        // Kept beside the local log, in netlog-pending.jsonl.
        public bool SaveLocally { get; set; }
        // How often the kept entries are offered to the collector again.
        // 0 keeps them without ever retrying, for collection by hand.
        public int RetryIntervalMinutes { get; set; } = 5;
        // Beyond this the kept entries are dropped, so that a collector that
        // stays down cannot fill the disk. 0 keeps them without any limit.
        public int MaxSizeMB { get; set; } = 10;
    }

    // Keeping the log on this machine, as one JSON object per line.
    internal class NetLogFileConfig
    {
        internal const int DefaultMaxSizeMB = 10;

        public bool Enabled { get; set; }
        // Empty means %ProgramData%\BrowserGuard\netlog.
        public string Directory { get; set; } = "";
        // The size at which the log is rotated. The file has to be rotated at
        // some size, so anything but a positive number falls back to the
        // default rather than meaning "no limit" as it does for the spool.
        public int MaxSizeMB { get; set; } = DefaultMaxSizeMB;
        // How many rotated files are kept. 0 discards the log instead.
        public int MaxGenerations { get; set; } = 10;
    }

    // Controls which local files may be uploaded.
    // The blocked lists are checked first, so they win over the allowed ones.
    // An empty allowed list means "no restriction from this rule".
    internal class UploadGuardConfig
    {
        public bool Enabled { get; set; }
        public string[] BlockedExtensions { get; set; } = [".exe", ".bat", ".cmd", ".js", ".vbs"];
        public string[] AllowedExtensions { get; set; } = [];
        public string[] AllowedPaths { get; set; } = [];
        public string[] BlockedPaths { get; set; } = [];
    }

    internal class SettingPageFilterConfig
    {
        public bool Enabled { get; set; }
        public bool NotifyOnBlocked { get; set; }
        public string[] BlockedPrefixes { get; set; } = [
                "edge://settings",
                "edge://flags",
                "edge://policy"
            ];
    }

    // Programs started when the browser launches.
    internal class StartupLauncherConfig
    {
        public bool Enabled { get; set; }
        public StartupProgramConfig[] Programs { get; set; } = [];
    }

    internal class StartupProgramConfig
    {
        public string Path { get; set; } = "";
        public string[] Arguments { get; set; } = [];
        public string WorkingDirectory { get; set; } = "";
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public string Sha256 { get; set; } = "";
    }

    internal static class ConfigLoader
    {
        internal static Config LoadConfig()
        {
            var configFilePath = GetConfigPath();
            if (string.IsNullOrEmpty(configFilePath))
            {
                Console.Error.WriteLine("ConfigFile path is not set in the registry.");
                return new Config();
            }
            using (var fileStream = new FileStream(configFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, true, 1024, true))
            {
                string data = streamReader.ReadToEnd();
                return ParseConf(data);
            }
        }

        internal static string GetConfigPath()
        {
            const string registryPath = @"SOFTWARE\BrowserGuard";
            const string valueName = "ConfigFile";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key == null)
                    {
                        Console.Error.WriteLine($"cannot read {registryPath}: key not found");
                        return null;
                    }
                    object value = key.GetValue(valueName);
                    if (value is string configFile)
                    {
                        return configFile;
                    }
                    else
                    {
                        Console.Error.WriteLine($"cannot read {registryPath}: 'ConfigFile' not found or not string");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"cannot read {registryPath}: {ex.Message}");
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        internal static Config ParseConf(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return new Config();

            return JsonSerializer.Deserialize<Config>(data, JsonOptions) ?? new Config();
        }
    }
}
