using Grpc.Net.Client;
using GrpcServer;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ChatClient
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		Grpc.Core.AsyncDuplexStreamingCall<ChatRequest, ChatReply> call = null;
		Task outputTask = null;

		public MainWindow()
		{
			// With help from http://www.networkcomms.net/creating-a-wpf-chat-client-server-application/
			// They used ProtoContract and ProtoMember as attribute.. 5 years ago.. very funny :)

			// Fix for current bug in wpf .net core. crashes with german localisation "BILDAUF" not found :)
			var culture = new System.Globalization.CultureInfo("en-US");
			Thread.CurrentThread.CurrentCulture = culture;
			Thread.CurrentThread.CurrentUICulture = culture;

			InitializeComponent();
		}

		// Event
		public void ConnectToServer()
		{
			AppContext.SetSwitch(
				"System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",
				true);

			// generated protobuf classes must be linked in the project and protobuf generation has to been disabled 
			// otherwise wpf dont find them on startup
			// https://github.com/dotnet/wpf/issues/810
			// Its a prerelease :D

			var httpClient = new HttpClient();
			var serverUrl = "http://localhost:50051";
			httpClient.BaseAddress = new Uri(serverUrl);
			Chat.ChatClient client = GrpcClient.Create<Chat.ChatClient>(httpClient);
			call = client.Chat();
			outputTask = Task.Run(() => OutputReponse(call.ResponseStream));
		}

		private async Task OutputReponse(Grpc.Core.IAsyncStreamReader<ChatReply> responseStream)
		{
			while (await responseStream.MoveNext())
			{
				var reply = responseStream.Current;
				await AppendLineToChatBox(reply.Message);
			}
		}

		/// Append the provided message to the chatBox text box.
		/// </summary>
		/// <param name="message"></param>
		public async Task AppendLineToChatBox(string message)
		{
			//To ensure we can successfully append to the text box from any thread
			//we need to wrap the append within an invoke action.
			await chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
			{
				chatBox.AppendText(messageToAdd + "\n");
				chatBox.ScrollToEnd();
			}), new object[] { message });
		}

		/// <summary>
		/// Refresh the userlist
		/// </summary>
		private async Task UpdateUserList()
		{
			await this.userList.Dispatcher.BeginInvoke(new Action<string[]>((users) =>
			{
				//First clear the text box
				userList.Text = "";

				//Now write out each username
				foreach (var username in users)
					userList.AppendText(username + "\n");
			}), new object[] { userList });
		}

		/// Send any entered message when we click the send button.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		/// Event.. no Task as return value..
		private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
		{
			await SendMessage();
		}

		/// <summary>
		/// Send any entered message when we press enter or return
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		/// Event.. no Task as return value..
		private async void MessageText_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter || e.Key == Key.Return)
			{
				await SendMessage();
			}
		}

		/// <summary>
		/// Correctly shutdown NetworkComms .Net when closing the WPF application
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (call != null)
			{
				await call.RequestStream.CompleteAsync();
				call.Dispose();
				call = null;
			}

		}

		/// <summary>
		/// Send our message.
		/// </summary>
		private async Task SendMessage()
		{
			if (call == null)
			{
				ConnectToServer();
			}

			//If we have tried to send a zero length string we just return
			if (messageText.Text.Trim() == "") return;

			await call.RequestStream.WriteAsync(new ChatRequest() { ClientName = "KinNeko", Message = this.messageText.Text });

			this.messageText.Clear();
		}
	}
}
