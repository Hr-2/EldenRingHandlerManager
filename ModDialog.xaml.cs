using System.IO;
using System.Windows;
using ERHandlerManager.Models;

namespace ERHandlerManager
{
    public partial class ModDialog : Window
    {
        public ModEntry Result { get; private set; } = new();

        public ModDialog()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        public ModDialog(ModEntry existing) : this()
        {
            TxtName.Text = existing.Name;
            TxtSource.Text = existing.SourcePath;
            TxtHandlerJs.Text = existing.HandlerJsPath;
            ChkCustomJs.IsChecked = existing.UseCustomHandlerJs;
            UpdateCustomJsState();
            Title = "Edit Mod";
            Result.Name = existing.Name;
            Result.SourcePath = existing.SourcePath;
            Result.Kind = existing.Kind;
            Result.Engine = existing.Engine;
        }

        private void ChkCustomJs_Toggled(object sender, RoutedEventArgs e) => UpdateCustomJsState();

        private void UpdateCustomJsState()
        {
            var enabled = ChkCustomJs.IsChecked == true;
            TxtHandlerJs.IsEnabled = enabled;
            BtnBrowseHandlerJs.IsEnabled = enabled;
            if (enabled && string.IsNullOrWhiteSpace(TxtHandlerJs.Text))
                TxtHandlerJs.Focus();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (IsDllSource(TxtSource.Text))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
                    Title = "Select a .dll mod file"
                };
                if (File.Exists(TxtSource.Text))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(TxtSource.Text);
                    dlg.FileName = Path.GetFileName(TxtSource.Text);
                }
                if (dlg.ShowDialog() == true)
                    TxtSource.Text = dlg.FileName;
            }
            else
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                if (Directory.Exists(TxtSource.Text))
                    dlg.SelectedPath = TxtSource.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtSource.Text = dlg.SelectedPath;
            }
        }

        private static bool IsDllSource(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (File.Exists(path) || (Directory.Exists(path) && ERHandlerManager.Services.ModDetector.DetectKind(path) == ModKind.Dll));
        }

        private void BrowseHandlerJs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JavaScript files (*.js)|*.js|All files (*.*)|*.*",
                Title = "Select a custom handler .js file"
            };
            if (File.Exists(TxtHandlerJs.Text))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(TxtHandlerJs.Text);
                dlg.FileName = Path.GetFileName(TxtHandlerJs.Text);
            }
            if (dlg.ShowDialog() == true)
                TxtHandlerJs.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtName.Text.Trim();
            var source = TxtSource.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a mod name.", "Missing name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!ERHandlerManager.Services.ModDetector.IsNameSafe(name))
            {
                MessageBox.Show("The name contains characters that aren't allowed (such as \", \\ or /). " +
                                "Use letters, numbers, spaces, and - _ . ( ) only.",
                    "Invalid name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                MessageBox.Show("Source path does not exist.", "Bad path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result.Name = name;
            Result.SourcePath = source;
            Result.Kind = ERHandlerManager.Services.ModDetector.DetectKind(source);
            Result.HandlerJsPath = TxtHandlerJs.Text.Trim();
            Result.UseCustomHandlerJs = ChkCustomJs.IsChecked == true;

            DialogResult = true;
            Close();
        }
    }
}