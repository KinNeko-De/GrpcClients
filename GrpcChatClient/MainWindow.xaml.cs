using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcServer;

namespace GrpcChatClient
{
	/// <summary>
	///     Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly CancellationTokenSource cancellationTokenSource;
		private AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse> call;
		private BlockingCollection<ChatMessagesRequest> outgoingMessages = new BlockingCollection<ChatMessagesRequest>();

		private Task sendingTask;
		private Task receivingTask;

		private string userId;
		private string userName;

		public MainWindow()
		{
			// With help from http://www.networkcomms.net/creating-a-wpf-chat-client-server-application/
			// They used ProtoContract and ProtoMember as attribute.. 5 years ago.. very funny :)

			cancellationTokenSource = new CancellationTokenSource();

			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			InitializeComponent();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			
		}

		private async Task StartChatting(string userName, RoutedEventArgs e)
		{
			this.userName = userName;
			this.userId = Guid.NewGuid().ToString();
			try
			{
				call = await ConnectToServer();
				await Login($"{userName}");

				sendingTask = Task.Run(() => SendMessageOverTheWire());

				receivingTask = ReceivingResponses();
			}
			catch (Exception exception)
			{
				await AppendLineToChatBox($"Connect to server failed: {exception}");
				e.Handled = true;
			}
		}

		public async Task<AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse>> ConnectToServer()
		{
			try
			{
				await AppendLineToChatBox($"Trying to connect to server...");
				var client = CreateClient();
				call = client.SendMessages(cancellationToken: cancellationTokenSource.Token);
				return call;
			}
			catch (Exception exception)
			{
				await AppendLineToChatBox(exception.ToString());
				throw;
			}
		}

		public static ChatService.ChatServiceClient CreateClient()
		{
			var channel = GrpcChannel.ForAddress("https://localhost:5001");
			var client = new ChatService.ChatServiceClient(channel);
			return client;
		}

		private async Task ReceivingResponses()
		{
			try
			{
				IAsyncEnumerable<ChatMessagesResponse> responses = call.ResponseStream.ReadAllAsync(cancellationTokenSource.Token);
				await foreach (var response in responses)
				{
					await OutputMessage(response);
				}
			}
			catch (Exception exception)
			{
				await AppendLineToChatBox($"Exception occurred while receiving messages from server: {exception}");
			}
		}

		public async Task Login(string name)
		{
			var request = new ChatMessagesRequest()
			{
				UserLogin = new UserLogin()
				{
					Id = Guid.NewGuid().ToString(),
					Name = name
				}
			};

			await call.RequestStream.WriteAsync(request);

			await AppendLineToChatBox($"You joined the chat.");
		}

		public async Task SendMessageOverTheWire()
		{
			var outgoing = outgoingMessages
.GetConsumingEnumerable();		
			foreach (var message in outgoing)
			{
				await call.RequestStream.WriteAsync(message);
			}
		}

		private async Task OutputMessage(ChatMessagesResponse response)
		{
			switch (response.MessagesCase)
			{
				case ChatMessagesResponse.MessagesOneofCase.None:
					break;
				case ChatMessagesResponse.MessagesOneofCase.ChatMessage:
					await AppendLineToChatBox($"[{response.SendFromUserName}]: {response.ChatMessage.Message}");
					break;
				case ChatMessagesResponse.MessagesOneofCase.UserLogin:
					await AppendLineToChatBox($"[{response.UserLogin.Name}] connected.");
					break;
				case ChatMessagesResponse.MessagesOneofCase.UserLogout:
					await AppendLineToChatBox($"[{response.UserLogout.Name}] disconnected.");
					break;
			}
		}

		/// <summary>
		///     Append the provided message to the chatBox text box.
		/// </summary>
		/// <param name="message"></param>
		public async Task AppendLineToChatBox(string message)
		{
			// To ensure we can successfully append to the text box from any thread
			// we need to wrap the append within an invoke action.
			await chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
			{
				chatBox.AppendText(messageToAdd + Environment.NewLine);
				chatBox.ScrollToEnd();
			}), message);
		}

		/// <summary>
		///     Refresh the userlist
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
			}), new object[] {userList});
		}

		/// <summary>
		///     Send any entered message when we click the send button.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		/// Event.. no Task as return value..
		private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
		{
			if(userId == null)
			{
				await NewUserLogin(e);
			}

			await SendMessage();
		}

		/// <summary>
		///     Send any entered message when we press enter or return
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
		///     Correctly shutdown NetworkComms .Net when closing the WPF application
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			await Disconnect();
		}

		private async Task Disconnect()
		{
			outgoingMessages.CompleteAdding();
			await sendingTask;

			if (call != null)
			{
				await call.RequestStream.CompleteAsync();
			}

			if(receivingTask != null)
			{
				await receivingTask;
			}
			outgoingMessages.CompleteAdding();

			if(call != null)
			{
				// Dispose because i dispose everything. It should not cancel here if we did everything right
				call.Dispose();
				call = null;
			}

			// Just to be sure :)
			cancellationTokenSource.Cancel();
			cancellationTokenSource.Dispose();
			userId = null;
			userName = null;
		}

		/// <summary>
		///     Send our message.
		/// </summary>
		private async Task SendMessage()
		{
			

			// If we have tried to send a zero length string we just return
			if (messageText.Text == string.Empty)
			{
				return;
			}

			var request = new ChatMessagesRequest()
			{
				ChatMessage = new ChatMessage()
				{
					Id = userId,
					Message = messageText.Text
				}
			};

			outgoingMessages.Add(request, cancellationTokenSource.Token);

			var responseToMe = new ChatMessagesResponse()
			{
				SendFromUserId = userId,
				SendFromUserName = userName,
				ChatMessage = request.ChatMessage
			};
			await OutputMessage(responseToMe);

			/*
			var random = new Random();
			while (true)
			{
				var randomMessage = random.Next(1, 1000);
				await SendMessageOverTheWire(randomMessage.ToString());
				await Task.Delay(2);
			}
			*/

			messageText.Clear();
		}

		private async void connectAndLoginButton_Click(object sender, RoutedEventArgs e)
		{
			await NewUserLogin(e);
		}

		private async Task NewUserLogin(RoutedEventArgs e)
		{
			if(userId != null)
			{
				MessageBox.Show("Already logged in", "Error", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			LoginDialog loginWindow = new LoginDialog();
			bool? dialogResult = loginWindow.ShowDialog();
		
			if (dialogResult.HasValue)
			{
				if (dialogResult.Value)
				{
					var loginName = loginWindow.LoginName;
					await StartChatting(loginName, e);
				}
				else
				{
					await AppendLineToChatBox("Login aborted.");
				}
			}
			else
			{
				await AppendLineToChatBox("Something went wrong (example i forgot the window close event)");
			}
		}
	}
}