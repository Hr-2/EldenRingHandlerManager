using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ERHandlerManager.Models
{
    public enum EngineType { ME2, ME3 }
    public enum ModKind { Folder, Dll }

    public class ModEntry : INotifyPropertyChanged
    {
        private string _name = "";
        private string _sourcePath = "";
        private bool _enabled = true;
        private EngineType _engine = EngineType.ME3;
        private ModKind _kind = ModKind.Folder;
        private string _handlerJsPath = "";
        private bool _useCustomHandlerJs = true;
        private bool _isMod = true;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string SourcePath
        {
            get => _sourcePath;
            set { _sourcePath = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public EngineType Engine
        {
            get => _engine;
            set { _engine = value; OnPropertyChanged(); }
        }

        public ModKind Kind
        {
            get => _kind;
            set { _kind = value; OnPropertyChanged(); }
        }

        public string HandlerJsPath
        {
            get => _handlerJsPath;
            set { _handlerJsPath = value; OnPropertyChanged(); }
        }

        public bool UseCustomHandlerJs
        {
            get => _useCustomHandlerJs;
            set { _useCustomHandlerJs = value; OnPropertyChanged(); }
        }

        public bool IsMod
        {
            get => _isMod;
            set { _isMod = value; OnPropertyChanged(); }
        }

        public List<ModEntry> Children { get; set; } = new();

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExpanded { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsNested { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsContainer => Children.Count > 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowToggle => true;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasHandlerJs => UseCustomHandlerJs && !string.IsNullOrWhiteSpace(HandlerJsPath);

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasDlls => Kind == ModKind.Folder && CountDllsRecursive(this) > 0;

        private static int CountDllsRecursive(ModEntry entry)
        {
            int n = entry.Kind == ModKind.Dll ? 1 : 0;
            foreach (var c in entry.Children)
                n += CountDllsRecursive(c);
            return n;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string KindLabel => Kind == ModKind.Dll ? "DLL" : "Folder";

        [System.Text.Json.Serialization.JsonIgnore]
        public string DllInfo
        {
            get
            {
                if (Kind == ModKind.Dll) return "";
                var count = CountEnabledDlls(this);
                return count > 0 ? $"({count} DLL{(count != 1 ? "s" : "")} inside)" : "";
            }
        }

        public static int CountEnabledDlls(ModEntry entry)
        {
            if (!entry.Enabled) return 0;
            var n = entry.Kind == ModKind.Dll ? 1 : 0;
            foreach (var c in entry.Children)
                n += CountEnabledDlls(c);
            return n;
        }

        public static IEnumerable<ModEntry> FlattenEnabled(ModEntry entry)
        {
            if (!entry.Enabled) yield break;
            yield return entry;
            foreach (var c in entry.Children)
                foreach (var d in FlattenEnabled(c))
                    yield return d;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class NamedProfile
    {
        public string Name { get; set; } = "";
        public List<ModEntry> Mods { get; set; } = new();
    }

    public class AppSettings
    {
        public string NucleusHandlersDir { get; set; } = "D:\\nucleus\\handlers";
        public string Me2BaseHandler { get; set; } = "";
        public string Me3BaseHandler { get; set; } = "";
        public List<ModEntry> Mods { get; set; } = new();
        public List<NamedProfile> Profiles { get; set; } = new();
        public string LastProfile { get; set; } = "";
        public bool AutoSaveMods { get; set; } = true;
        public bool AutoConfig { get; set; } = true;
        public string LastPage { get; set; } = "Mods";
        public string LastEngineTab { get; set; } = "ME2";
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
        public bool SkipDeployConfirm { get; set; }
    }
}