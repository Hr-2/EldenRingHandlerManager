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
        private bool _updating;
        private readonly System.Windows.Threading.DispatcherTimer _saveDebounce = new()
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        public MainWindow()
        {
            InitializeComponent();
            _settings = new SettingsService();
            _deploy = new DeployService(_settings);
            _saveDebounce.Tick += (s, e) => { _saveDebounce.Stop(); FlushSettings(); };
        }

        private void FlushSettings()
        {
            SaveToSettings();
            AutoSaveModsToProfile();
            try { _settings.Save(); } catch (Exception ex) { Log("WARN: settings save failed: " + ex.Message); }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"Elden Ring Handler Manager v{AppInfo.Version}";
            TitleText.Text = Title;
            _settings.AutoDetectIfMissing();
            LoadSettings();
            ChkAutoSave.IsChecked = _settings.Settings.AutoSaveMods;
            ChkAutoConfig.IsChecked = _settings.Settings.AutoConfig;
            EnsureDefaultProfile();
            RefreshUI();
            RestoreUiState();
            LoadLastProfile();
            RefreshModSizes();
            ShowWhatsNewIfUpdated();
            _ = CheckForUpdateOnStartup();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _saveDebounce.Stop();
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
            var query = TxtModSearch?.Text.Trim() ?? "";
            var list = new ObservableCollection<ModEntry>(_mods.Where(m =>
                ModFitsTab(m) && (query.Length == 0 || m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))));
            ModList.ItemsSource = list;
        }

        private void TxtModSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RebuildVisibleMods();
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
            ShowPage((string)btn.Tag);
        }

        private void ShowPage(string tag)
        {
            foreach (var nav in new[] { NavMods, NavDeploy, NavHandlers })
                nav.IsChecked = nav.Tag?.ToString() == tag;

            PageMods.Visibility = tag == "Mods" ? Visibility.Visible : Visibility.Collapsed;
            PageDeploy.Visibility = tag == "Deploy" ? Visibility.Visible : Visibility.Collapsed;
            PageHandlers.Visibility = tag == "Handlers" ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Restores the last used page, engine tab and window size.</summary>
        private void RestoreUiState()
        {
            var s = _settings.Settings;
            if (s.WindowWidth > 0 && s.WindowHeight > 0)
            {
                Width = s.WindowWidth;
                Height = s.WindowHeight;
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;

            if (s.LastEngineTab == "ME3")
            {
                _activeEngineTab = EngineType.ME3;
                TabME3.IsChecked = true;
                TabME2.IsChecked = false;
            }
            else
            {
                _activeEngineTab = EngineType.ME2;
                TabME2.IsChecked = true;
                TabME3.IsChecked = false;
            }
            RebuildVisibleMods();

            ShowPage(string.IsNullOrEmpty(s.LastPage) ? "Mods" : s.LastPage);
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

        private void OpenModFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ModEntry entry)
            {
                var path = entry.SourcePath;
                if (Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", path);
                else if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }

        private void OpenHandlersFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = TxtHandlersDir.Text.Trim();
            if (Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
            else
                MessageBox.Show("Nucleus handlers folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            s.LastPage = NavMods.IsChecked == true ? "Mods" : (NavDeploy.IsChecked == true ? "Deploy" : "Handlers");
            s.LastEngineTab = _activeEngineTab == EngineType.ME2 ? "ME2" : "ME3";
            s.WindowWidth = (int)ActualWidth;
            s.WindowHeight = (int)ActualHeight;
            s.WindowMaximized = WindowState == WindowState.Maximized;
        }

        /// <summary>Saves settings and, if auto-save is on, updates the active profile.
        /// Writes are debounced so rapid changes don't hammer the disk.</summary>
        private void SaveMods()
        {
            SaveToSettings();
            AutoSaveModsToProfile();
            RefreshUI();
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        // ===================== Mod operations =====================

        private void AddMod_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ModDialog();
            if (dlg.ShowDialog() == true)
            {
                dlg.Result.Engine = _activeEngineTab;
                CascadeEngine(dlg.Result);
                dlg.Result.Name = UniqueModName(dlg.Result.Name, _activeEngineTab);
                _mods.Add(dlg.Result);
                SaveMods();
                RefreshModSizes();
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

        /// <summary>
        /// Returns the given name, or a suffixed variant ("name (2)", "name (3)", …)
        /// if another mod of the same engine already uses it. Deploy writes mods to
        /// ModEngine\&lt;name&gt;, so distinct names prevent silent overwrites.
        /// </summary>
        private string UniqueModName(string name, EngineType engine)
        {
            var candidate = name;
            int n = 2;
            while (_mods.Any(m =>
                m.Engine == engine && string.Equals(m.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{name} ({n++})";
            }
            return candidate;
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

        private void CopyToOtherEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem mi) return;
            var entry = mi.DataContext as ModEntry;
            if (entry == null) return;
            var target = (string)mi.Tag == "ME2" ? EngineType.ME2 : EngineType.ME3;
            if (entry.Engine == target) return; // already on that engine

            var copy = CloneEntry(entry);
            copy.Engine = target;
            CascadeEngine(copy);
            copy.Name = UniqueModName(copy.Name, target);
            _mods.Add(copy);
            SaveMods();
            RefreshModSizes();
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in _mods) SetExpanded(m, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var m in _mods) SetExpanded(m, false);
        }

        private static void SetExpanded(ModEntry entry, bool expanded)
        {
            entry.IsExpanded = expanded;
            foreach (var c in entry.Children) SetExpanded(c, expanded);
        }

        private void MoveModUp_Click(object sender, RoutedEventArgs e) => MoveMod(-1);

        private void MoveModDown_Click(object sender, RoutedEventArgs e) => MoveMod(1);

        /// <summary>Moves the selected root mod up/down in the load order.</summary>
        private void MoveMod(int direction)
        {
            if (ModList.SelectedItem is not ModEntry selected) return;
            var index = _mods.IndexOf(selected);
            if (index < 0) return; // only root mods can be reordered
            var target = index + direction;
            if (target < 0 || target >= _mods.Count) return;
            _mods.Move(index, target);
            SaveMods();
            // Re-select the moved item after the list rebuilds.
            ModList.UpdateLayout();
            var container = ModList.ItemContainerGenerator.ContainerFromItem(selected) as TreeViewItem;
            if (container != null) container.IsSelected = true;
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
                var entry = ModDetector.BuildTree(path,
                    ModDetector.SanitizeName(Path.GetFileNameWithoutExtension(path)));
                entry.Engine = _activeEngineTab;
                CascadeEngine(entry);
                entry.Name = UniqueModName(entry.Name, _activeEngineTab);
                _mods.Add(entry);
            }

            SaveMods();
            RefreshModSizes();
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

        private System.Diagnostics.Stopwatch? _deployStopwatch;

        private async void DeployWithEngine(EngineType engine)
        {
            if (!_settings.Settings.SkipDeployConfirm)
            {
                var dlg = new ConfirmDialog("Confirm deploy",
                    $"This will REPLACE the current \"Elden Ring\" handler in your Nucleus handlers folder " +
                    $"with a fresh build from the {engine} template, then copy your enabled {engine} mods into it.\n\n" +
                    "Nothing is backed up automatically — if you want to keep the current handler, use Backup first.\n\nContinue?");
                dlg.Owner = this;
                if (dlg.ShowDialog() != true) return;
                if (dlg.DontAskAgain)
                {
                    _settings.Settings.SkipDeployConfirm = true;
                    _settings.Save();
                }
            }

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
            _deployStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var result = await Task.Run(() =>
                    _deploy.Deploy(engine, (msg, pct, done, total) =>
                    {
                        if (Dispatcher.HasShutdownStarted || !IsLoaded) return;
                        Dispatcher.Invoke(() =>
                        {
                            DeployProgress.Value = Math.Min(pct, 100);
                            var eta = "";
                            if (done > 0 && done < total && _deployStopwatch != null)
                            {
                                var elapsed = _deployStopwatch.Elapsed.TotalSeconds;
                                var remaining = elapsed * ((double)(total - done) / done);
                                eta = $"  ETA {FormatEta(remaining)}";
                            }
                            DeployProgressText.Text = FormatBytes(done) + " / " + FormatBytes(total) + eta;
                            int mb = (int)(done / (1024 * 1024));
                            int totalMb = Math.Max(1, (int)(total / (1024 * 1024)));
                            Log($"[{mb}/{totalMb} MB] {msg}");
                        });
                    }, token), token);

                if (!IsLoaded) return;
                foreach (var line in result.Log)
                    Log(line);

                if (result.Success)
                    Log("SUCCESS: Deploy completed.");
                else
                    Log("FAILED: See errors above.");
                PersistDeployLog(result, $"Deploy {engine}");
            }
            catch (OperationCanceledException)
            {
                if (IsLoaded) Log("Deploy cancelled.");
            }

            if (!IsLoaded) return;
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

        private static string FormatEta(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "…";
            if (seconds < 60) return $"{Math.Ceiling(seconds)}s";
            if (seconds < 3600) return $"{Math.Floor(seconds / 60)}m {Math.Ceiling(seconds % 60)}s";
            return $"{Math.Floor(seconds / 3600)}h {Math.Floor((seconds % 3600) / 60)}m";
        }

        private static long DirSizeBytes(string dir)
        {
            long n = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir)) n += new FileInfo(f).Length;
                foreach (var d in Directory.GetDirectories(dir)) n += DirSizeBytes(d);
            }
            catch { }
            return n;
        }

        private static long ModSizeBytes(ModEntry mod)
        {
            if (mod.Kind == ModKind.Dll && File.Exists(mod.SourcePath)) return new FileInfo(mod.SourcePath).Length;
            if (Directory.Exists(mod.SourcePath)) return DirSizeBytes(mod.SourcePath);
            return 0;
        }

        /// <summary>Computes each mod's on-disk size in the background and updates the list.</summary>
        private async void RefreshModSizes()
        {
            foreach (var mod in _mods)
            {
                if (!IsLoaded) return;
                var size = await Task.Run(() => ModSizeBytes(mod));
                if (IsLoaded) mod.SizeLabel = FormatBytes(size);
            }
        }

        private void Log(string message)
        {
            LogBox.AppendText(message + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private static readonly string DeployLogPath =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ERHandlerManager", "deploy.log");

        /// <summary>Appends a completed deploy/backup log to disk for later debugging.</summary>
        private static void PersistDeployLog(DeployResult result, string header)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(DeployLogPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                System.Text.StringBuilder sb = new();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {header}");
                foreach (var line in result.Log)
                    sb.AppendLine("  " + line);
                sb.AppendLine();
                File.AppendAllText(DeployLogPath, sb.ToString());
            }
            catch { }
        }

        // ===================== Profiles =====================

        private void RefreshProfileCombo()
        {
            var selected = ProfileCombo.SelectedItem as string;
            ProfileCombo.ItemsSource = _settings.Settings.Profiles.Select(p => p.Name).ToList();
            if (selected != null && ProfileCombo.Items.Contains(selected))
                ProfileCombo.SelectedItem = selected;
        }

        /// <summary>Creates a "Default" profile if no profiles exist, and selects one.</summary>
        private void EnsureDefaultProfile()
        {
            var s = _settings.Settings;
            if (s.Profiles.Count == 0)
            {
                s.Profiles.Add(new NamedProfile { Name = "Default" });
                s.LastProfile = "Default";
                _settings.Save();
            }
            // Ensure the combo has a selection
            RefreshProfileCombo();
            if (ProfileCombo.SelectedItem == null && ProfileCombo.Items.Count > 0)
                ProfileCombo.SelectedItem = ProfileCombo.Items[0];
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
            RefreshModSizes();
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
            EnsureDefaultProfile(); // never allow zero profiles
            RefreshUI();
        }

        private void ProfileExport_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileCombo.SelectedItem is not string name) return;
            var profile = _settings.Settings.Profiles.FirstOrDefault(p => p.Name == name);
            if (profile == null) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export profile",
                Filter = "Profile files (*.json)|*.json",
                FileName = name + ".json"
            };
            if (dlg.ShowDialog() != true) return;

            var json = System.Text.Json.JsonSerializer.Serialize(profile,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(dlg.FileName, json);
        }

        private void ProfileImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import profile",
                Filter = "Profile files (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = System.IO.File.ReadAllText(dlg.FileName);
                var profile = System.Text.Json.JsonSerializer.Deserialize<NamedProfile>(json);
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
                    throw new InvalidOperationException("Not a valid profile file.");

                var unique = profile.Name;
                int n = 2;
                while (_settings.Settings.Profiles.Any(p => p.Name == unique))
                    unique = $"{profile.Name} ({n++})";
                profile.Name = unique;

                _settings.Settings.Profiles.Add(profile);
                _settings.Save();
                RefreshProfileCombo();
                ProfileCombo.SelectedItem = unique;
                _settings.Settings.LastProfile = unique;
                _settings.Save();
                LoadProfile(unique);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't import profile: {ex.Message}", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        // ===================== Updates =====================

        private void Changelog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ChangelogWindow { Owner = this };
            dlg.ShowDialog();
        }

        /// <summary>
        /// If the user just updated to a new version, show a "What's new" window
        /// with the changelog sections that are newer than the last seen version.
        /// </summary>
        private void ShowWhatsNewIfUpdated()
        {
            var s = _settings.Settings;
            if (string.IsNullOrEmpty(s.LastSeenVersion))
            {
                // First ever launch — just mark it as seen, no notification.
                s.LastSeenVersion = AppInfo.Version;
                _settings.Save();
                return;
            }

            if (Version.TryParse(AppInfo.Version, out var current) &&
                Version.TryParse(s.LastSeenVersion, out var last) && current > last)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var dlg = new ChangelogWindow(s.LastSeenVersion) { Owner = this };
                    dlg.ShowDialog();
                    s.LastSeenVersion = AppInfo.Version;
                    _settings.Save();
                });
            }
        }

        private async Task CheckForUpdateOnStartup()
        {
            var info = await UpdateService.CheckForUpdateAsync();
            if (!info.HasUpdate) return; // silent when up to date
            await Dispatcher.InvokeAsync(async () => await OfferAndApplyUpdate(info, manual: false));
        }

        private async void CheckForUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            UpdateBtn.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates...";
            try
            {
                var info = await UpdateService.CheckForUpdateAsync();
                if (info.HasUpdate)
                {
                    await OfferAndApplyUpdate(info, manual: true);
                }
                else if (info.LatestVersion == info.CurrentVersion && info.LatestVersion != "")
                {
                    UpdateStatusText.Text = $"Up to date (v{info.CurrentVersion})";
                    MessageBox.Show($"You're running the latest version (v{info.CurrentVersion}).",
                        "Up to date", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatusText.Text = "";
                    MessageBox.Show("Couldn't reach GitHub to check for updates. Check your connection and try again.",
                        "Check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                UpdateBtn.IsEnabled = true;
            }
        }

        private async Task OfferAndApplyUpdate(UpdateInfo info, bool manual)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var result = MessageBox.Show(
                    $"A new version (v{info.LatestVersion}) is available — you're on v{info.CurrentVersion}.\n\n" +
                    "Download and install it now? The app will restart automatically.",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    if (manual) UpdateStatusText.Text = $"Update v{info.LatestVersion} available.";
                    return;
                }

                UpdateStatusText.Text = "Downloading update...";
                UpdateBtn.IsEnabled = false;

                var progressWindow = new UpdateProgressWindow(this);
                progressWindow.Show();

                try
                {
                    await Task.Run(() => UpdateService.DownloadAndApplyAsync(info,
                        new UpdateProgressWindow.Progress(progressWindow)));
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    progressWindow.Close();
                    UpdateStatusText.Text = "";
                    MessageBox.Show(ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _updating = false;
                UpdateBtn.IsEnabled = true;
            }
        }

        // ===================== Open deployed folder =====================

        private void OpenDeployed_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.Combine(TxtHandlersDir.Text.Trim(), "Elden Ring");
            var modEngine = Path.Combine(folder, "ModEngine");
            var target = Directory.Exists(modEngine) ? modEngine : (Directory.Exists(folder) ? folder : "");
            if (target.Length > 0)
                System.Diagnostics.Process.Start("explorer.exe", target);
            else
                MessageBox.Show("No deployed handler found yet. Deploy first, or check the handlers folder path.",
                    "Not deployed", MessageBoxButton.OK, MessageBoxImage.Information);
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
            PersistDeployLog(result, "Backup");
            DeployME2Btn.IsEnabled = true;
            DeployME3Btn.IsEnabled = true;
            BackupBtn.IsEnabled = true;
        }
    }
}