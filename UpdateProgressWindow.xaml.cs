using System;
using System.Windows;

namespace ERHandlerManager
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
        }

        public void Report(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percent;
                StatusText.Text = status;
                PercentText.Text = percent > 0 ? $"{percent}%" : "";
            });
        }

        public class Progress : IProgress<(int percent, string status)>
        {
            private readonly UpdateProgressWindow _window;
            public Progress(UpdateProgressWindow window) => _window = window;

            public void Report((int percent, string status) value)
                => _window.Report(value.percent, value.status);
        }
    }
}
