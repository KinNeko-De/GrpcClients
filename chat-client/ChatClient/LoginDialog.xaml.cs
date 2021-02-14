using System.Windows;

namespace ChatClient
{
	/// <summary>
	/// Interaction logic for LoginDialog.xaml
	/// </summary>
	public partial class LoginDialog : Window
	{
		public string LoginName {  get { return Name.Text; } }

		public LoginDialog()
		{
			InitializeComponent();
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			if(string.IsNullOrWhiteSpace(Name.Text))
			{
				MessageBox.Show("Enter a name that is not empty successfully", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			else
			{
				DialogResult = true;
				Close();
			}
		}

		private void Button_Click_1(object sender, RoutedEventArgs e)
		{
			DialogResult = false;
			Close();
		}
	}
}
