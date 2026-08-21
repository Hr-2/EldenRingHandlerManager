using System.Windows;
using System.Windows.Input;

namespace ERHandlerManager
{
    public partial class ProfileNameDialog : Window
    {
        public string Result { get; private set; } = "";

        public ProfileNameDialog()
        {
            InitializeComponent();
            TxtName.Focus();
        }

        private void TxtName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Save_Click(sender, e);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a profile name.", "Missing name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = name;
            DialogResult = true;
            Close();
        }
    }
}