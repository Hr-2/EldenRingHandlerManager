using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ERHandlerManager.Models;
using ERHandlerManager.Services;

namespace ERHandlerManager
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settings;
        private readonly DeployService _deploy;
        private readonly ObservableCollection<ModEntry> _mods = new();
        private EngineType _activeEngineTab = EngineType.ME2;

        public MainWindow()
        {
            InitializeComponent();
            _settings = new SettingsService();
            _deploy = new DeployService(_settings);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _settings.AutoDetectIfMissing();
            LoadSettings();
            ChkAutoSave.IsChecked = _settings.Settings.AutoSaveMods;
            ChkAutoConfig.IsChecked = _settings.Settings.AutoConfig;
            RefreshUI();
            LoadLastProfile();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveToSettings();
            _settings.Save();
        }

        // ===================== Title bar =====================

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
                return;
            }
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadSettings()
        {
            var s = _settings.Settings;
            TxtHandlersDir.Text = s.NucleusHandlersDir;
            TxtMe2Base.Text = s.Me2BaseHandler;
            TxtMe3Base.Text = s.Me3BaseHandler;

            _mods.Clear();
            foreach (var m in s.Mods)
                _mods.Add(m);
            RebuildVisibleMods();

            RefreshProfileCombo();
        }

        private bool ModFitsTab(ModEntry m)
        {
            return m.Engine == _activeEngineTab;
        }

        private void RebuildVisibleMods()
        {
            var list = new ObservableCollection<ModEntry>(_mods.Where(ModFitsTab));
            ModList.ItemsSource = list;
        }

        private void EngineTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;
            var tag = (string)btn.Tag;
            _activeEngineTab = tag == "ME2" ? EngineType.ME2 : EngineType.ME3;

            foreach (var tab in new[] { TabME2, TabME3 })
                tab.IsChecked = tab == btn;

            RebuildVisibleMods();
            RefreshModsStatus();
        }

        private void RefreshUI()
        {
            RebuildVisibleMods();
            RefreshEngineBadge();
            RefreshModsStatus();
            RefreshDeploySummary();
        }

        private void RefreshEngineBadge()
        {
            var engine = _settings.DetermineEngine();
            EngineBadgeText.Text = engine.ToString();
            EngineBadge.Background = engine == EngineType.ME2
                ? FindResource("WarningBrush") as System.Windows.Media.SolidColorBrush
                : FindResource("SuccessBrush") as System.Windows.Media.SolidColorBrush;
        }

        private void RefreshModsStatus()
        {
            int enabled = 0, total = 0;
            void Walk(ModEntry m)
            {
                if (!m.IsMod) return; // only count actual mods, not DLLs/subfolders
                total++;
                if (m.Enabled) enabled++;
                foreach (var c in m.Children) Walk(c);
            }
            foreach (var m in _mods) Walk(m);

            ModCountText.Text = total.ToString();
            ModsStatus.Text = $"{enabled} enabled of {total} total. " +
                              "Mark folders as mods with the tag; unmarked folders are just content.";
        }

        // ===================== Sidebar navigation =====================

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton btn) return;
            var tag = (string)btn.Tag;

            foreach (var nav in new[] { NavMods, NavDeploy, NavHandlers })
                nav.IsChecked = nav == btn;

            PageMods.Visibility = tag == "Mods" ? Visibility.Visible : Visibility.Collapsed;
            PageDeploy.Visibility = tag == "Deploy" ? Visibility.Visible : Visibility.Collapsed;
            PageHandlers.Visibility = tag == "Handlers" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===================== Mod toggle =====================

        private void IsMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ModEntry entry)
            {
                entry.IsMod = !entry.IsMod;
                SaveMods();
            }
        }

        private void ModToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ModEntry entry)
            {
                // Toggling a container cascades to all its contents
                CascadeEnabled(entry);

                // If a mod is being enabled, disable all mods from the other engine
                // so you can't mix ME2 and ME3 mods.
                if (entry.Enabled)
                    DisableOtherEngine(entry.Engine);
            }
            SaveMods();
        }

        private void DisableOtherEngine(EngineType keepEngine)
        {
            void Walk(ModEntry m)
            {
                if (m.Engine != keepEngine)
                {
                    m.Enabled = false;
                    CascadeEnabled(m);
                }
                foreach (var c in m.Children) Walk(c);
            }
            foreach (var m in _mods) Walk(m);
        }

        private static void CascadeEnabled(ModEntry entry)
        {
            foreach (var c in entry.Children)
            {
                c.Enabled = entry.Enabled;
                CascadeEnabled(c);
            }
        }

        private void RefreshDeploySummary()
        {
            int me2Enabled = 0, me3Enabled = 0;
            foreach (var m in _mods)
                CountEnabledByEngine(m, ref me2Enabled, ref me3Enabled);

            DeploySummary.Text = $"{me2Enabled} ME2 mod(s) and {me3Enabled} ME3 mod(s) enabled. " +
                                 "Pick the engine you want on the buttons.";
        }

        private static void CountEnabledByEngine(ModEntry m, ref int me2, ref int me3)
        {
            if (!m.IsMod || !m.Enabled) return;
            if (m.Engine == EngineType.ME2) me2++;
            else me3++;
            foreach (var c in m.Children)
                CountEnabledByEngine(c, ref me2, ref me3);
        }

        private void SaveToSettings()
        {
            var s = _settings.Settings;
            s.NucleusHandlersDir = TxtHandlersDir.Text.Trim();
            s.Me2BaseHandler = TxtMe2Base.Text.Trim();
            s.Me3BaseHandler = TxtMe3Base.Text.Trim();
            s.Mods = _mods.ToList();
        }

        /// <summary>Saves settings and, if auto-save is on, updates the active profile.</summary>
        private void SaveMods()
        {
            SaveToSettings();
            AutoSaveModsToProfile();
            _settings.Save();
            RefreshUI();
        }

        // ===================== Mod operations =====================

        private void AddMod_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ModDialog();
            if (dlg.ShowDialog() == true)
            {
                _mods.Add(dlg.Result);
                SaveMods();
            }
        }

        private void EditMod_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetModFromEvent(sender);
            if (selected == null) return;
            var dlg = new ModDialog(selected);
            if (dlg.ShowDialog() == true)
            {
                CopyFromDialog(selected, dlg.Result);
                SaveMods();
            }
        }

        private void RemoveMod_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetModFromEvent(sender);
            if (selected == null) return;
            RemoveEntry(selected);
            SaveMods();
        }

        private ModEntry? GetModFromEvent(object sender)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ModEntry m) return m;
            return ModList.SelectedItem as ModEntry;
        }

        private static void CopyFromDialog(ModEntry target, ModEntry src)
        {
            target.Name = src.Name;
            target.SourcePath = src.SourcePath;
            target.Kind = src.Kind;
            target.HandlerJsPath = src.HandlerJsPath;
            target.UseCustomHandlerJs = src.UseCustomHandlerJs;
            target.Engine = src.Engine;
        }

        /// <summary>When a container's engine is set, apply it to every child.</summary>
        private static void CascadeEngine(ModEntry entry)
        {
            foreach (var c in entry.Children)
            {
                c.Engine = entry.Engine;
                CascadeEngine(c);
            }
        }

        private void RemoveEntry(ModEntry entry)
        {
            if (_mods.Remove(entry)) return;
            foreach (var root in _mods)
            {
                if (RemoveChild(root, entry)) return;
            }
        }

        private static bool RemoveChild(ModEntry parent, ModEntry entry)
        {
            if (parent.Children.Remove(entry)) return true;
            foreach (var c in parent.Children)
            {
                if (RemoveChild(c, entry)) return true;
            }
            return false;
        }

        private void ModList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            EditMod_Click(sender, new RoutedEventArgs());
        }

        // ===================== Drag-drop onto mods list =====================

        private void ModList_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                ModListDropZone.BorderBrush = FindResource("BorderBrush") as System.Windows.Media.SolidColorBrush;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ModList_DragLeave(object sender, DragEventArgs e)
        {
            ModListDropZone.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }

        private void ModList_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;

            foreach (var path in files)
            {
                var entry = ModDetector.BuildTree(path);
                entry.Engine = _activeEngineTab;
                CascadeEngine(entry);
                _mods.Add(entry);
            }

            SaveMods();
        }

        // ===================== Handlers / settings =====================

        private void BrowseHandlersDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (Directory.Exists(TxtHandlersDir.Text)) dlg.SelectedPath = TxtHandlersDir.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtHandlersDir.Text = dlg.SelectedPath;
                SaveToSettings();
                _settings.Save();
            }
        }

        private void BrowseBase_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var tag = (string)btn.Tag;
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var current = tag == "ME2" ? TxtMe2Base.Text : TxtMe3Base.Text;
            if (Directory.Exists(current)) dlg.SelectedPath = current;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (tag == "ME2") TxtMe2Base.Text = dlg.SelectedPath;
                else TxtMe3Base.Text = dlg.SelectedPath;
                SaveToSettings();
                _settings.Save();
            }
        }

        // ===================== Deploy =====================

        private CancellationTokenSource? _deployCts;

        private void DeployME2_Click(object sender, RoutedEventArgs e) => DeployWithEngine(EngineType.ME2);
        private void DeployME3_Click(object sender, RoutedEventArgs e) => DeployWithEngine(EngineType.ME3);

        private async void DeployWithEngine(EngineType engine)
        {
            DeployProgress.Value = 0;
            DeployProgressText.Text = "";
            LogBox.Clear();
            CancelBtn.Visibility = Visibility.Visible;
            DeployME2Btn.IsEnabled = false;
            DeployME3Btn.IsEnabled = false;
            BackupBtn.IsEnabled = false;
            Log($"Starting deploy for {engine}...");

            SaveToSettings();
            _settings.Save();

            _deployCts = new CancellationTokenSource();
            var token = _deployCts.Token;

            try
            {
                var result = await Task.Run(() =>
                    _deploy.Deploy(engine, (msg, pct, done, total) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            DeployProgress.Value = Math.Min(pct, 100);
                            DeployProgressText.Text = FormatBytes(done) + " / " + FormatBytes(total);
                            int mb = (int)(done / (1024 * 1024));
                            int totalMb = Math.Max(1, (int)(total / (1024 * 1024)));
                            Log($"[{mb}/{totalMb} MB] {msg}");
                        });
                    }, token), token);

                foreach (var line in result.Log)
                    Log(line);

                if (result.Success)
                    Log("SUCCESS: Deploy completed.");
                else
                    Log("FAILED: See errors above.");
            }
            catch (OperationCanceledException)
            {
                Log("Deploy cancelled.");
            }

            CancelBtn.Visibility = Visibility.Collapsed;
            DeployME2Btn.IsEnabled = true;
            DeployME3Btn.IsEnabled = true;
            BackupBtn.IsEnabled = true;
            RefreshEngineBadge();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _deployCts?.Cancel();
            Log("Cancelling...");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private void Log(string message)
        {
            LogBox.AppendText(message + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        // ===================== Profiles =====================

        private void RefreshProfileCombo()
        {
            var selected = ProfileCombo.SelectedItem as string;
            ProfileCombo.ItemsSource = _settings.Settings.Profiles.Select(p => p.Name).ToList();
            if (selected != null && ProfileCombo.Items.Contains(selected))
                ProfileCombo.SelectedItem = selected;
        }

        private void LoadLastProfile()
        {
            var last = _settings.Settings.LastProfile;
            if (string.IsNullOrWhiteSpace(last)) return;
            if (ProfileCombo.Items.Contains(last))
                ProfileCombo.SelectedItem = last; // triggers SelectionChanged -> auto-loads
        }

        private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not string name) return;
            _settings.Settings.LastProfile = name;
            _settings.Save();
            LoadProfile(name);
        }

        private void AutoSave_Toggled(object sender, RoutedEventArgs e)
        {
            _settings.Settings.AutoSaveMods = ChkAutoSave.IsChecked == true;
            _settings.Save();
        }

        private void AutoConfig_Toggled(object sender, RoutedEventArgs e)
        {
            _settings.Settings.AutoConfig = ChkAutoConfig.IsChecked == true;
            _settings.Save();
        }

        private void AutoSaveModsToProfile()
        {
            if (!_settings.Settings.AutoSaveMods) return;
            var name = ProfileCombo.SelectedItem as string ?? _settings.Settings.LastProfile;
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = _settings.Settings.Profiles.FirstOrDefault(p => p.Name == name);
            if (existing != null)
                existing.Mods = _mods.Select(CloneEntry).ToList();
            else
                _settings.Settings.Profiles.Add(new NamedProfile { Name = name, Mods = _mods.Select(CloneEntry).ToList() });
        }

        private void LoadProfile(string name)
        {
            var profile = _settings.Settings.Profiles.FirstOrDefault(p => p.Name == name);
            if (profile == null) return;

            _mods.Clear();
            foreach (var m in profile.Mods)
            {
                var copy = CloneEntry(m);
                NormalizeModMarkers(copy);
                _mods.Add(copy);
            }
            SaveToSettings();
            _settings.Save();
            RefreshUI();
        }

        private static void NormalizeModMarkers(ModEntry entry)
        {
            foreach (var c in entry.Children)
            {
                c.IsMod = false;
                NormalizeModMarkers(c);
            }
        }

        private void ProfileSave_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptForProfileName();
            if (string.IsNullOrWhiteSpace(name)) return;

            var existing = _settings.Settings.Profiles.FirstOrDefault(p => p.Name == name);
            if (existing != null)
                existing.Mods = _mods.Select(CloneEntry).ToList();
            else
                _settings.Settings.Profiles.Add(new NamedProfile { Name = name, Mods = _mods.Select(CloneEntry).ToList() });

            _settings.Save();
            RefreshProfileCombo();
            ProfileCombo.SelectedItem = name;
            _settings.Settings.LastProfile = name;
            _settings.Save();
        }

        private void ProfileLoad_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not string name) return;
            LoadProfile(name);
        }

        private void ProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not string name) return;
            var profile = _settings.Settings.Profiles.FirstOrDefault(p => p.Name == name);
            if (profile == null) return;

            var result = MessageBox.Show($"Delete profile '{name}'?", "Delete profile",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _settings.Settings.Profiles.Remove(profile);
            _settings.Save();
            RefreshProfileCombo();
        }

        private string PromptForProfileName()
        {
            var dlg = new ProfileNameDialog();
            if (dlg.ShowDialog() == true)
                return dlg.Result;
            return "";
        }

        private static ModEntry CloneEntry(ModEntry src)
        {
            var c = new ModEntry
            {
                Name = src.Name,
                SourcePath = src.SourcePath,
                Enabled = src.Enabled,
                Engine = src.Engine,
                Kind = src.Kind,
                HandlerJsPath = src.HandlerJsPath,
                UseCustomHandlerJs = src.UseCustomHandlerJs,
                IsMod = src.IsMod
            };
            foreach (var child in src.Children)
            {
                var cc = CloneEntry(child);
                cc.IsNested = child.IsNested;
                c.Children.Add(cc);
            }
            return c;
        }

        // ===================== Backup =====================

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            DeployProgress.Value = 0;
            DeployProgressText.Text = "";
            LogBox.Clear();
            CancelBtn.Visibility = Visibility.Collapsed;
            DeployME2Btn.IsEnabled = false;
            DeployME3Btn.IsEnabled = false;
            BackupBtn.IsEnabled = false;
            Log("Backing up current handler...");
            var result = await Task.Run(() => _deploy.Backup());
            foreach (var line in result.Log)
                Log(line);
            DeployME2Btn.IsEnabled = true;
            DeployME3Btn.IsEnabled = true;
            BackupBtn.IsEnabled = true;
        }
    }
}