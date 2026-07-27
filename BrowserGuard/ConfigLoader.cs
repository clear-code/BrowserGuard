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
using System.Threading.Tasks;

namespace BrowserGuard
{
    internal class Config
    {
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

    internal static class ConfigLoader
    {
        internal static Config LoadConfig()
        {
            var ruleFilePath = GetRulefilePath();
            if (string.IsNullOrEmpty(ruleFilePath))
            {
                Console.Error.WriteLine("Rulefile path is not set in the registry.");
                return new Config();
            }
            using (var fileStream = new FileStream(ruleFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, true, 1024, true))
            {
                string data = streamReader.ReadToEnd();
                return ParseConf(data);
            }
        }

        internal static string GetRulefilePath()
        {
            const string registryPath = @"SOFTWARE\BrowserGuard";
            const string valueName = "Rulefile";
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
                    if (value is string rulefile)
                    {
                        return rulefile;
                    }
                    else
                    {
                        Console.Error.WriteLine($"cannot read {registryPath}: 'Rulefile' not found or not string");
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

        internal static Config ParseConf(string data)
        {
            var conf = new Config();
            var lines = data.Split([ "\r\n", "\n" ], StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                switch (line[0])
                {
                    case ';':
                    case '#':
                        // コメント行
                        break;
                    default:
                        if (line.StartsWith("Endpoint="))
                        {
                            conf.Endpoint = line.Substring("Endpoint=".Length);
                        }
                        if (line.StartsWith("UrlAccess="))
                        {
                            conf.UrlAccess = line.Substring("UrlAccess=".Length).Trim().ToLower() == "true";
                        }
                        if (line.StartsWith("Browsing="))
                        {
                            conf.Browsing = line.Substring("Browsing=".Length).Trim().ToLower() == "true";
                        }
                        if (line.StartsWith("Upload="))
                        {
                            conf.Upload = line.Substring("Upload=".Length).Trim().ToLower() == "true";
                        }
                        if (line.StartsWith("Download="))
                        {
                            conf.Download = line.Substring("Download=".Length).Trim().ToLower() == "true";
                        }
                        break;
                }
            }
            return conf;
        }
    }
}
