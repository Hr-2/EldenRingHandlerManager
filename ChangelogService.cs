using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ERHandlerManager.Services
{
    public static class ChangelogService
    {
        private static readonly Regex VersionHeader = new(@"^## \[([\d.]+)\]", RegexOptions.Multiline);

        public static string ChangelogPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md");

        /// <summary>Returns the raw changelog text, or a message if the file is missing.</summary>
        public static string GetFullText()
        {
            try
            {
                if (File.Exists(ChangelogPath))
                    return File.ReadAllText(ChangelogPath);
                return "No changelog file found.";
            }
            catch (Exception ex)
            {
                return $"Could not read changelog: {ex.Message}";
            }
        }

        /// <summary>Returns the top-most version in the changelog (the newest release).</summary>
        public static string? GetTopVersion()
        {
            var m = VersionHeader.Match(GetFullText());
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Returns the markdown section text for a specific version, or null if not found.
        /// </summary>
        public static string? GetSection(string version)
        {
            try
            {
                if (!File.Exists(ChangelogPath)) return null;
                var lines = File.ReadAllLines(ChangelogPath);
                bool inSection = false;
                var section = new System.Collections.Generic.List<string>();
                foreach (var line in lines)
                {
                    var m = VersionHeader.Match(line);
                    if (m.Success)
                    {
                        if (inSection) break; // found next section, stop
                        if (m.Groups[1].Value == version) inSection = true;
                        continue;
                    }
                    if (inSection) section.Add(line);
                }
                return inSection ? string.Join("\n", section).Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>Returns sections for all versions newer than the given version, in changelog order (newest first).</summary>
        public static List<(string version, string text)> GetNewerSections(string? sinceVersion)
        {
            var result = new List<(string, string)>();
            try
            {
                if (!File.Exists(ChangelogPath)) return result;
                var lines = File.ReadAllLines(ChangelogPath);
                string? currentVersion = null;
                var currentLines = new List<string>();
                bool collecting = string.IsNullOrEmpty(sinceVersion); // if no last seen, collect all

                void Flush()
                {
                    if (currentVersion != null && collecting)
                        result.Add((currentVersion, string.Join("\n", currentLines).Trim()));
                    currentLines.Clear();
                }

                foreach (var line in lines)
                {
                    var m = VersionHeader.Match(line);
                    if (m.Success)
                    {
                        Flush();
                        currentVersion = m.Groups[1].Value;
                        if (!string.IsNullOrEmpty(sinceVersion) && Version.TryParse(currentVersion, out var cv) &&
                            Version.TryParse(sinceVersion, out var sv) && cv > sv)
                            collecting = true;
                        else if (!string.IsNullOrEmpty(sinceVersion) && currentVersion == sinceVersion)
                            collecting = false;
                        continue;
                    }
                    currentLines.Add(line);
                }
                Flush();
            }
            catch { }
            return result;
        }
    }
}