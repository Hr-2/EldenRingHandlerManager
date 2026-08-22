using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ERHandlerManager.Models;

namespace ERHandlerManager.Services
{
    public class DeployResult
    {
        public bool Success { get; set; }
        public List<string> Log { get; set; } = new();
        public EngineType UsedEngine { get; set; }
    }

    /// <summary>Progress callback: (message, percent 0-100, current/total bytes).</summary>
    public delegate void DeployProgress(string message, double percent, long doneBytes, long totalBytes);

    public class DeployService
    {
        private readonly SettingsService _settings;

        public DeployService(SettingsService settings)
        {
            _settings = settings;
        }

        public DeployResult Deploy(EngineType? overrideEngine = null, DeployProgress? progress = null, CancellationToken? ct = null)
        {
            var result = new DeployResult();
            var s = _settings.Settings;

            if (string.IsNullOrWhiteSpace(s.NucleusHandlersDir) || !Directory.Exists(s.NucleusHandlersDir))
            {
                result.Log.Add("ERROR: Nucleus handlers folder not found. Set it in the Handlers tab.");
                return result;
            }

            var engine = overrideEngine ?? _settings.DetermineEngine();
            result.UsedEngine = engine;

            var baseHandler = _settings.GetBaseHandlerDir(engine);
            if (string.IsNullOrWhiteSpace(baseHandler) || !Directory.Exists(baseHandler))
            {
                result.Log.Add($"ERROR: Base {engine} handler folder not found: '{baseHandler}'. Set it in the Handlers tab.");
                return result;
            }

            var handlersDir = s.NucleusHandlersDir;
            const string gameName = "Elden Ring";
            var curJs = Path.Combine(handlersDir, gameName + ".js");
            var curFolder = Path.Combine(handlersDir, gameName);

            try
            {
                var modEngineDir = Path.Combine(handlersDir, gameName, "ModEngine");
                var enabledMods = _settings.Settings.Mods.Where(m => m.Enabled && Compatible(m, engine)).ToList();

                // Pre-compute total bytes to copy so we can show a real %.
                // Totals only count what will actually be copied (disabled
                // child folders are skipped), so the bar tracks reality.
                long totalBytes = 0;
                var baseFolderPath = Path.Combine(baseHandler, gameName);
                if (Directory.Exists(baseFolderPath))
                    totalBytes += DirBytes(baseFolderPath);
                foreach (var mod in enabledMods)
                    totalBytes += ModBytes(mod);
                if (totalBytes <= 0) totalBytes = 1;

                long doneBytes = 0;

                // --- 1. Deploy base handler (js + folder) ---
                var baseJs = Path.Combine(baseHandler, gameName + ".js");
                var baseFolder = Path.Combine(baseHandler, gameName);
                if (File.Exists(baseJs))
                {
                    File.Copy(baseJs, curJs, true);
                    result.Log.Add($"Handler js: {gameName}.js");
                }
                else
                {
                    result.Log.Add($"WARNING: Base handler js not found at '{baseJs}'.");
                }
                if (Directory.Exists(baseFolder))
                {
                    if (Directory.Exists(curFolder))
                        Directory.Delete(curFolder, true);
                    CopyDirectoryProgress(baseFolder, curFolder, ref doneBytes, totalBytes,
                        progress, ct);
                    result.Log.Add($"Handler folder: {gameName}\\ (from base {engine} template)");
                }
                else
                {
                    result.Log.Add($"WARNING: Base handler folder not found at '{baseFolder}'.");
                }

                // --- 3a. Custom handler js override ---
                var customJs = enabledMods.FirstOrDefault(m =>
                    m.UseCustomHandlerJs && !string.IsNullOrWhiteSpace(m.HandlerJsPath) && File.Exists(m.HandlerJsPath));
                if (customJs != null)
                {
                    File.Copy(customJs.HandlerJsPath, curJs, true);
                    result.Log.Add($"Handler js overridden by '{customJs.Name}' -> {Path.GetFileName(curJs)}");
                }

                // --- 3b. Deploy enabled mods ---
                foreach (var mod in enabledMods)
                {
                    if (string.IsNullOrWhiteSpace(mod.SourcePath) || !Directory.Exists(mod.SourcePath))
                    {
                        result.Log.Add($"WARNING: Mod '{mod.Name}' source folder not found, skipping.");
                        continue;
                    }

                    progress?.Invoke($"Deploying {mod.Name}...", doneBytes * 100.0 / totalBytes, doneBytes, totalBytes);
                    ct?.ThrowIfCancellationRequested();
                    if (engine == EngineType.ME2)
                        DeployModME2(mod, modEngineDir, result, ref doneBytes, totalBytes, progress, ct);
                    else
                        DeployModME3(mod, modEngineDir, result, ref doneBytes, totalBytes, progress, ct);
                }

                // --- 4. Write config ---
                if (_settings.Settings.AutoConfig)
                {
                    var configPath = Path.Combine(modEngineDir, engine == EngineType.ME2 ? "config_eldenring.toml" : "me3.toml");
                    if (engine == EngineType.ME2)
                        WriteME2Config(configPath, enabledMods);
                    else
                        WriteME3Config(configPath, enabledMods);
                    result.Log.Add($"Config written: ModEngine\\{(engine == EngineType.ME2 ? "config_eldenring.toml" : "me3.toml")}");
                }
                else
                {
                    result.Log.Add("Auto-config off: keeping existing config file from base handler.");
                }

                progress?.Invoke("Deploy complete.", 100, doneBytes, totalBytes);
                result.Success = true;
                result.Log.Add("Deploy complete.");
            }
            catch (OperationCanceledException)
            {
                result.Log.Add("Deploy cancelled by user.");
                // Remove the half-built handler so Nucleus doesn't try to run a
                // partial build. A fresh deploy rebuilds it from the template.
                try
                {
                    if (Directory.Exists(curFolder)) Directory.Delete(curFolder, true);
                    if (File.Exists(curJs)) File.Delete(curJs);
                    result.Log.Add("Partial handler removed.");
                }
                catch (Exception delEx)
                {
                    result.Log.Add($"WARNING: could not remove partial handler: {delEx.Message}");
                }
            }
            catch (Exception ex)
            {
                result.Log.Add($"ERROR: {ex.Message}");
            }

            return result;
        }

        private static long DirBytes(string dir)
        {
            long n = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    n += new FileInfo(f).Length;
                foreach (var d in Directory.GetDirectories(dir))
                    n += DirBytes(d);
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Total bytes that will actually be copied for a mod, mirroring the
        /// CopyModTree logic (disabled children are skipped). DLL mods count
        /// the single file or top-level dlls.
        /// </summary>
        private static long ModBytes(ModEntry mod)
        {
            long n = 0;
            if (mod.Kind == ModKind.Dll)
            {
                if (File.Exists(mod.SourcePath)) return new FileInfo(mod.SourcePath).Length;
                if (Directory.Exists(mod.SourcePath))
                    foreach (var f in Directory.GetFiles(mod.SourcePath))
                        n += new FileInfo(f).Length;
                return n;
            }
            if (!Directory.Exists(mod.SourcePath)) return 0;

            foreach (var f in Directory.GetFiles(mod.SourcePath))
                n += new FileInfo(f).Length;
            foreach (var dir in Directory.GetDirectories(mod.SourcePath))
            {
                var name = Path.GetFileName(dir);
                var child = mod.Children.FirstOrDefault(c => c.Kind == ModKind.Folder && c.Name == name);
                if (child != null && !child.Enabled) continue; // disabled, skipped
                n += child != null && child.Children.Count > 0 ? ModBytes(child) : DirBytes(dir);
            }
            return n;
        }

        private static void CopyDirectoryProgress(string source, string dest, ref long doneBytes, long totalBytes, DeployProgress? report, CancellationToken? ct = null)
        {
            ct?.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                ct?.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dest, name), overwrite: true);
                doneBytes += new FileInfo(file).Length;
                report?.Invoke($"Copying {name}...", doneBytes * 100.0 / totalBytes, doneBytes, totalBytes);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                ct?.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                CopyDirectoryProgress(dir, Path.Combine(dest, name), ref doneBytes, totalBytes, report, ct);
            }
        }

        private static bool Compatible(ModEntry mod, EngineType engine)
        {
            return mod.Engine == engine;
        }

        // ---------- ME2 ----------
        private void DeployModME2(ModEntry mod, string modEngineDir, DeployResult result,
            ref long doneBytes, long totalBytes, DeployProgress? report, CancellationToken? ct = null)
        {
            ct?.ThrowIfCancellationRequested();
            if (mod.Kind == ModKind.Dll)
            {
                var dest = Path.Combine(modEngineDir, mod.Name, "dll");
                if (File.Exists(mod.SourcePath))
                {
                    Directory.CreateDirectory(dest);
                    File.Copy(mod.SourcePath, Path.Combine(dest, Path.GetFileName(mod.SourcePath)), true);
                    doneBytes += new FileInfo(mod.SourcePath).Length;
                    report?.Invoke($"ME2: {mod.Name}", doneBytes * 100.0 / totalBytes, doneBytes, totalBytes);
                    result.Log.Add($"ME2: deployed '{Path.GetFileName(mod.SourcePath)}' -> ModEngine\\{mod.Name}\\dll");
                }
                else
                {
                    CopyDirectoryProgress(mod.SourcePath, dest, ref doneBytes, totalBytes, report, ct);
                    result.Log.Add($"ME2: deployed dll mod '{mod.Name}' -> ModEngine\\{mod.Name}\\dll");
                }
            }
            else
            {
                var dest = Path.Combine(modEngineDir, mod.Name);
                CopyModTree(mod, dest, ref doneBytes, totalBytes, report, ct);
                result.Log.Add($"ME2: deployed '{mod.Name}' -> ModEngine\\{mod.Name}");
            }
        }

        // ---------- ME3 ----------
        private void DeployModME3(ModEntry mod, string modEngineDir, DeployResult result,
            ref long doneBytes, long totalBytes, DeployProgress? report, CancellationToken? ct = null)
        {
            ct?.ThrowIfCancellationRequested();
            if (mod.Kind == ModKind.Dll)
            {
                var dest = Path.Combine(modEngineDir, "Dlls", mod.Name);
                if (File.Exists(mod.SourcePath))
                {
                    Directory.CreateDirectory(dest);
                    File.Copy(mod.SourcePath, Path.Combine(dest, Path.GetFileName(mod.SourcePath)), true);
                    doneBytes += new FileInfo(mod.SourcePath).Length;
                    report?.Invoke($"ME3: {mod.Name}", doneBytes * 100.0 / totalBytes, doneBytes, totalBytes);
                    result.Log.Add($"ME3: deployed '{Path.GetFileName(mod.SourcePath)}' -> ModEngine\\Dlls\\{mod.Name}");
                }
                else
                {
                    CopyDirectoryProgress(mod.SourcePath, dest, ref doneBytes, totalBytes, report, ct);
                    result.Log.Add($"ME3: deployed dll mod '{mod.Name}' -> ModEngine\\Dlls\\{mod.Name}");
                }
            }
            else
            {
                var dest = Path.Combine(modEngineDir, mod.Name);
                CopyModTree(mod, dest, ref doneBytes, totalBytes, report, ct);
                result.Log.Add($"ME3: deployed folder mod '{mod.Name}' -> ModEngine\\{mod.Name}");
            }
        }

        // ---------- Config generation (tree-aware) ----------

        private void WriteME2Config(string path, List<ModEntry> enabledMods)
        {
            // Only mod-marked entries get configured; unmarked ones are copied but not loaded.
            enabledMods = enabledMods.Where(m => m.IsMod).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Global mod engine configuration");
            sb.AppendLine("[modengine]");
            sb.AppendLine("debug = false");
            sb.AppendLine();
            sb.AppendLine("external_dlls = [");
            sb.AppendLine("    \"..\\\\Mod Seamlesscoop\\\\SeamlessCoop\\\\ersc.dll\",");

            // ME2 dll paths use // as separator
            var dllEntries = new List<string>();
            foreach (var root in enabledMods)
            {
                // Root DLL mod → <Name>/dll/<file>
                if (root.Kind == ModKind.Dll)
                {
                    foreach (var f in ModDetector.GetEnabledDllFiles(root))
                        dllEntries.Add($"{root.Name}//dll//{Path.GetFileName(f)}");
                    continue;
                }
                // Root folder mod → register only the top-level dlls and the
                // flat "dll" subfolder dlls (never nested dll\X\X.dll duplicates).
                foreach (var f in ModDetector.GetEnabledDllFiles(root))
                {
                    var rel = ModDetector.RelativeDllPath(root, f).Replace("/", "//");
                    dllEntries.Add($"{root.Name}//{rel}");
                }
            }
            foreach (var e in dllEntries)
                sb.AppendLine($"    \"{e}\",");
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("[extension.mod_loader]");
            sb.AppendLine("enabled = true");
            sb.AppendLine("loose_params = false");
            sb.AppendLine();
            sb.AppendLine("mods = [");

            var folderMods = enabledMods.Where(m => m.Kind == ModKind.Folder).ToList();
            if (folderMods.Count == 0)
            {
                sb.AppendLine("    { enabled = true, name = \"default\", path = \"mod\" },");
            }
            foreach (var root in folderMods)
            {
                // Only the root folder is registered; the engine loads its subfolders automatically
                sb.AppendLine($"    {{ enabled = true, name = \"{root.Name}\", path = \"{root.Name}\" }},");
            }
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("[extension.scylla_hide]");
            sb.AppendLine("enabled = false");

            File.WriteAllText(path, sb.ToString());
        }

        private void WriteME3Config(string path, List<ModEntry> enabledMods)
        {
            // Only mod-marked entries get configured; unmarked ones are copied but not loaded.
            enabledMods = enabledMods.Where(m => m.IsMod).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("profileVersion = \"v1\"");
            sb.AppendLine();
            sb.AppendLine("savefile = \"ER0000.co2\"");
            sb.AppendLine();
            sb.AppendLine("[[supports]]");
            sb.AppendLine("game = \"elden-ring\"");
            sb.AppendLine();
            sb.AppendLine("#DLLs");
            sb.AppendLine("[[natives]]");
            sb.AppendLine("enabled = true");
            sb.AppendLine("path = \"../Mod Seamlesscoop/SeamlessCoop/ersc.dll\"");
            sb.AppendLine("load_early = true");

            foreach (var root in enabledMods)
            {
                // Root DLL mod → Dlls/<Name>/<file>
                if (root.Kind == ModKind.Dll)
                {
                    foreach (var f in ModDetector.GetEnabledDllFiles(root))
                    {
                        sb.AppendLine();
                        sb.AppendLine("[[natives]]");
                        sb.AppendLine("enabled = true");
                        sb.AppendLine($"path = \"Dlls/{root.Name}/{Path.GetFileName(f)}\"");
                    }
                    continue;
                }
                // Root folder mod → register only the top-level dlls and the
                // flat "dll" subfolder dlls (never nested dll\X\X.dll duplicates).
                foreach (var f in ModDetector.GetEnabledDllFiles(root))
                {
                    var rel = ModDetector.RelativeDllPath(root, f);
                    sb.AppendLine();
                    sb.AppendLine("[[natives]]");
                    sb.AppendLine("enabled = true");
                    sb.AppendLine($"path = \"{root.Name}/{rel}\"");
                }
            }

            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("#Mods Folders");
            sb.AppendLine("[[packages]]");
            sb.AppendLine("enabled = true");
            sb.AppendLine("id = \"SeamlessCoop\"");
            sb.AppendLine("path = \"../Mod Seamlesscoop/SeamlessCoop\"");

            foreach (var root in enabledMods)
            {
                if (root.Kind != ModKind.Folder) continue;
                // Only the root folder is registered; the engine loads its subfolders automatically
                sb.AppendLine();
                sb.AppendLine("[[packages]]");
                sb.AppendLine("enabled = true");
                sb.AppendLine($"id = \"{root.Name}\"");
                sb.AppendLine($"path = \"{root.Name}\"");
            }

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Copies the current deployed handler (js + folder) into a single manual
        /// backup location under handlers\_handler_backup\.
        /// </summary>
        public DeployResult Backup()
        {
            var result = new DeployResult { Success = false };
            var s = _settings.Settings;

            if (string.IsNullOrWhiteSpace(s.NucleusHandlersDir) || !Directory.Exists(s.NucleusHandlersDir))
            {
                result.Log.Add("ERROR: Nucleus handlers folder not found.");
                return result;
            }

            const string gameName = "Elden Ring";
            var handlersDir = s.NucleusHandlersDir;
            var curJs = Path.Combine(handlersDir, gameName + ".js");
            var curFolder = Path.Combine(handlersDir, gameName);
            var backupDir = Path.Combine(handlersDir, "_handler_backup");

            try
            {
                Directory.CreateDirectory(backupDir);
                if (!File.Exists(curJs) && !Directory.Exists(curFolder))
                {
                    result.Log.Add("Nothing to back up — no handler deployed yet.");
                    return result;
                }

                if (File.Exists(curJs))
                {
                    File.Copy(curJs, Path.Combine(backupDir, gameName + ".js"), overwrite: true);
                    result.Log.Add($"Backed up {gameName}.js");
                }
                if (Directory.Exists(curFolder))
                {
                    var bakFolder = Path.Combine(backupDir, gameName);
                    if (Directory.Exists(bakFolder))
                        Directory.Delete(bakFolder, true);
                    CopyDirectory(curFolder, bakFolder);
                    result.Log.Add($"Backed up {gameName}\\");
                }

                result.Success = true;
                result.Log.Add("Backup saved to handlers\\_handler_backup\\");
            }
            catch (Exception ex)
            {
                result.Log.Add($"ERROR: {ex.Message}");
            }

            return result;
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dest, name), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                var name = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(dest, name));
            }
        }

        /// <summary>Copies a folder mod while skipping disabled child subfolders.</summary>
        private static void CopyModTree(ModEntry mod, string dest, ref long doneBytes, long totalBytes, DeployProgress? report, CancellationToken? ct = null)
        {
            ct?.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(mod.SourcePath))
            {
                ct?.ThrowIfCancellationRequested();
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
                doneBytes += new FileInfo(f).Length;
                report?.Invoke($"Copying {Path.GetFileName(f)}...", doneBytes * 100.0 / totalBytes, doneBytes, totalBytes);
            }

            foreach (var dir in Directory.GetDirectories(mod.SourcePath))
            {
                ct?.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                var child = mod.Children.FirstOrDefault(c =>
                    c.Kind == ModKind.Folder && c.Name == name);
                if (child != null && !child.Enabled) continue; // disabled, skip

                if (child != null && child.Children.Count > 0)
                    CopyModTree(child, Path.Combine(dest, name), ref doneBytes, totalBytes, report, ct);
                else
                    CopyDirectoryProgress(dir, Path.Combine(dest, name), ref doneBytes, totalBytes, report, ct);
            }
        }
    }
}