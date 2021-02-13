using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GrpcChatClient
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
