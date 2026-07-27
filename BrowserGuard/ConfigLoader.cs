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
        public BlockSettingPageConfig BlockSettingPage { get; set; } = new();
    }

    // 各種操作を Endpoint へ記録する機能。
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
    }

    // 特定拡張子のアップロードを遮断する機能。
    internal class UploadGuardConfig
    {
        public bool Enabled { get; set; }
        public string[] BlockedExtensions { get; set; } = [".exe", ".bat", ".cmd", ".js", ".vbs"];
    }

    // 設定画面などへのアクセスを遮断する機能。
    internal class BlockSettingPageConfig
    {
        public bool Enabled { get; set; }
        public string[] UrlPrefixes { get; set; } = ["edge://settings/"];
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
