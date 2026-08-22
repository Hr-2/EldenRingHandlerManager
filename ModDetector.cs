using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ERHandlerManager.Models;

namespace ERHandlerManager.Services
{
    public static class ModDetector
    {
        private static readonly HashSet<string> AssetDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "action", "asset", "chr", "cs", "event", "grass", "item", "map", "mapstudio",
            "material", "menu", "msg", "param", "parts", "script", "sd", "sfx",
            "systemparam", "talk", "wem", "enus", "deploy", "hi", "projects"
        };

        private static readonly HashSet<string> JunkDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "logs", "modengine2", "modengine3", ".smithbox", "crashpad", "locale",
            "backup", "backup_chr", "include", "lib", "share", "tools", "assets"
        };

        public static ModKind DetectKind(string path)
        {
            if (File.Exists(path))
                return Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                    ? ModKind.Dll
                    : ModKind.Folder;

            if (!Directory.Exists(path))
                return ModKind.Folder;

            if (File.Exists(Path.Combine(path, "regulation.bin")))
                return ModKind.Folder;

            if (Directory.GetFiles(path, "*.dcx", SearchOption.TopDirectoryOnly).Length > 0)
                return ModKind.Folder;

            if (Directory.GetDirectories(path).Any(d => AssetDirs.Contains(Path.GetFileName(d))))
                return ModKind.Folder;

            if (Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly).Length > 0)
                return ModKind.Dll;

            return ModKind.Folder;
        }

        /// <summary>
        /// Builds a mod entry tree from a dropped file/folder. Folders containing
        /// dlls or nested mod folders get children, each toggleable independently.
        /// </summary>
        public static ModEntry BuildTree(string path, string? name = null)
        {
            var rootName = name ?? Path.GetFileNameWithoutExtension(path);
            var entry = new ModEntry
            {
                Name = rootName,
                SourcePath = path,
                Kind = DetectKind(path),
                Enabled = true,
                Engine = EngineType.ME3,
                IsMod = true
            };

            if (Directory.Exists(path))
                PopulateChildren(path, entry);

            return entry;
        }

        private static void PopulateChildren(string dir, ModEntry parent)
        {
            foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                parent.Children.Add(new ModEntry
                {
                    Name = Path.GetFileNameWithoutExtension(dll),
                    SourcePath = dll,
                    Kind = ModKind.Dll,
                    Enabled = true,
                    Engine = EngineType.ME3,
                    IsNested = true
                });
            }

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var subName = Path.GetFileName(sub);
                if (JunkDirs.Contains(subName)) continue;

                var child = BuildTree(sub, subName);
                child.IsNested = true;
                child.IsMod = false; // sub-folders are not mods by default; user can mark them
                if (child.Kind == ModKind.Folder && File.Exists(Path.Combine(sub, "regulation.bin")))
                    child.Enabled = true;
                parent.Children.Add(child);
            }
        }

        public static IEnumerable<string> GetDllFiles(ModEntry mod)
        {
            if (mod.Kind == ModKind.Dll)
            {
                if (File.Exists(mod.SourcePath))
                    return new[] { mod.SourcePath };
                if (Directory.Exists(mod.SourcePath))
                    return Directory.GetFiles(mod.SourcePath, "*.dll", SearchOption.TopDirectoryOnly);
                return Enumerable.Empty<string>();
            }

            // Folder mod: register any .dll found anywhere inside, skipping junk dirs
            var found = new List<string>();
            if (Directory.Exists(mod.SourcePath))
                CollectDllsRecursive(mod.SourcePath, found);
            return found;
        }

        /// <summary>
        /// Like GetDllFiles, but only for files that will actually be copied at
        /// deploy time — disabled child folders are skipped, matching CopyModTree.
        /// </summary>
        public static IEnumerable<string> GetEnabledDllFiles(ModEntry mod)
        {
            if (mod.Kind == ModKind.Dll)
            {
                if (File.Exists(mod.SourcePath))
                    return new[] { mod.SourcePath };
                if (Directory.Exists(mod.SourcePath))
                    return Directory.GetFiles(mod.SourcePath, "*.dll", SearchOption.TopDirectoryOnly);
                return Enumerable.Empty<string>();
            }

            var found = new List<string>();
            if (Directory.Exists(mod.SourcePath))
                CollectEnabledDlls(mod, found);
            return found;
        }

        private static void CollectEnabledDlls(ModEntry entry, List<string> found)
        {
            try
            {
                found.AddRange(Directory.GetFiles(entry.SourcePath, "*.dll", SearchOption.TopDirectoryOnly));
                foreach (var sub in Directory.GetDirectories(entry.SourcePath))
                {
                    var name = Path.GetFileName(sub);
                    if (JunkDirs.Contains(name)) continue;
                    var child = entry.Children.FirstOrDefault(c =>
                        c.Kind == ModKind.Folder && c.Name == name);
                    if (child != null && !child.Enabled) continue; // disabled, not copied

                    if (child != null && child.Children.Count > 0)
                        CollectEnabledDlls(child, found);
                    else
                        CollectDllsRecursive(sub, found);
                }
            }
            catch { }
        }

        private static void CollectDllsRecursive(string dir, List<string> found)
        {
            try
            {
                found.AddRange(Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly));
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (JunkDirs.Contains(name)) continue;
                    // A "dll" subfolder is a flat convention: register only its
                    // top-level dlls, never recursing into its own subfolders
                    // (which would duplicate them, e.g. dll\X.dll + dll\X\X.dll).
                    if (name.Equals("dll", StringComparison.OrdinalIgnoreCase))
                    {
                        found.AddRange(Directory.GetFiles(sub, "*.dll", SearchOption.TopDirectoryOnly));
                        continue;
                    }
                    CollectDllsRecursive(sub, found);
                }
            }
            catch { }
        }

        public static string RelativeDllPath(ModEntry mod, string dllPath)
        {
            var folder = mod.SourcePath;
            var rel = Path.GetRelativePath(folder, dllPath);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>
        /// Returns a safe name for use as a folder and in the generated config.
        /// Keeps letters, digits, spaces and a few harmless characters; anything
        /// else (quotes, slashes, etc.) is removed.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unnamed Mod";
            var sb = new StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ||
                    c == '.' || c == '(' || c == ')')
                    sb.Append(c);
            }
            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "Unnamed Mod" : result;
        }

        /// <summary>True when the name is already safe (no changes needed).</summary>
        public static bool IsNameSafe(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name == SanitizeName(name);
        }
    }
}