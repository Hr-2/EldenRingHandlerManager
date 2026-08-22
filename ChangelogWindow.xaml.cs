using System.Linq;
using System.Windows;
using ERHandlerManager.Services;

namespace ERHandlerManager
{
    public partial class ChangelogWindow : Window
    {
        public ChangelogWindow()
        {
            InitializeComponent();
            TitleText.Text = "Changelog";
            ChangelogText.Text = ChangelogService.GetFullText();
            VersionHint.Text = string.IsNullOrEmpty(ChangelogService.GetTopVersion())
                ? ""
                : $"Latest version in changelog: v{ChangelogService.GetTopVersion()}";
        }

        /// <summary>Opens the window showing only sections newer than the given version.</summary>
        public ChangelogWindow(string? sinceVersion) : this()
        {
            var sections = ChangelogService.GetNewerSections(sinceVersion);
            if (sections.Count > 0)
            {
                TitleText.Text = "What's new";
                ChangelogText.Text = string.Join("\n\n", sections.Select(s => $"## [{s.version}]\n\n{s.text}"));
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
