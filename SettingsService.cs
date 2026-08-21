using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ERHandlerManager.Models;

namespace ERHandlerManager.Services
{
    public class SettingsService
    {
        private static readonly string AppDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERHandlerManager");

        public string SettingsPath => Path.Combine(AppDir, "settings.json");

        public AppSettings Settings { get; private set; }

        public SettingsService()
        {
            Settings = Load();
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { }
            return Defaults();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var json = System.Text.Json.JsonSerializer.Serialize(Settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to save settings: " + ex.Message, ex);
            }
        }

        private static AppSettings Defaults()
        {
            return new AppSettings
            {
                Me2BaseHandler = "",
                Me3BaseHandler = ""
            };
        }

        public void AutoDetectIfMissing()
        {
            var s = Settings;
            if (!string.IsNullOrWhiteSpace(s.NucleusHandlersDir) && Directory.Exists(s.NucleusHandlersDir))
            {
                var bck = Path.Combine(s.NucleusHandlersDir, "bck");
                if (Directory.Exists(bck))
                {
                    if (string.IsNullOrWhiteSpace(s.Me2BaseHandler))
                    {
                        var d1 = Directory.GetDirectories(bck).FirstOrDefault(d =>
                            Path.GetFileName(d).Equals("me2 handler", StringComparison.OrdinalIgnoreCase));
                        if (d1 != null) s.Me2BaseHandler = d1;
                    }
                    if (string.IsNullOrWhiteSpace(s.Me3BaseHandler))
                    {
                        var d2 = Directory.GetDirectories(bck).FirstOrDefault(d =>
                            Path.GetFileName(d).Equals("me3 handler", StringComparison.OrdinalIgnoreCase));
                        if (d2 != null) s.Me3BaseHandler = d2;
                    }
                }
            }

            RebuildTreesIfEmpty();
        }

        /// <summary>
        /// Mods saved before the tree feature have no children. Rebuild the
        /// hierarchy for any folder mod whose tree is empty, and normalize the
        /// IsMod marker so only root mods are marked.
        /// </summary>
        public void RebuildTreesIfEmpty()
        {
            bool changed = false;
            foreach (var m in Settings.Mods)
            {
                if (m.IsMod != true) { m.IsMod = true; changed = true; }
                if (m.Kind == ModKind.Folder && m.Children.Count == 0 && Directory.Exists(m.SourcePath))
                {
                    var rebuilt = ModDetector.BuildTree(m.SourcePath, m.Name);
                    m.Children = rebuilt.Children;
                    m.Kind = rebuilt.Kind;
                    changed = true;
                }
                if (NormalizeModMarkers(m)) changed = true;
            }
            if (changed) Save();
        }

        private static bool NormalizeModMarkers(ModEntry entry)
        {
            bool changed = false;
            foreach (var c in entry.Children)
            {
                if (c.IsMod != false) { c.IsMod = false; changed = true; }
                if (NormalizeModMarkers(c)) changed = true;
            }
            return changed;
        }

        public string GetBaseHandlerDir(EngineType engine)
        {
            return engine == EngineType.ME2 ? Settings.Me2BaseHandler : Settings.Me3BaseHandler;
        }

        public EngineType DetermineEngine()
        {
            bool anyME2 = false, anyME3 = false;
            void Walk(ModEntry m)
            {
                if (!m.Enabled) return;
                if (m.Engine == EngineType.ME2) anyME2 = true;
                else anyME3 = true;
                foreach (var c in m.Children) Walk(c);
            }
            foreach (var m in Settings.Mods) Walk(m);

            if (anyME2) return EngineType.ME2;
            if (anyME3) return EngineType.ME3;
            return EngineType.ME3;
        }
    }
}
// force rebuild