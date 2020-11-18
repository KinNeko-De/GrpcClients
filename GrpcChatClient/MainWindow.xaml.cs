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
		private Guid userId = Guid.NewGuid();
		private AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse> call;
		private BlockingCollection<ChatMessagesResponse> incomingMessages = new BlockingCollection<ChatMessagesResponse>();
		private BlockingCollection<ChatMessagesRequest> outgoingMessages = new BlockingCollection<ChatMessagesRequest>();

		private Task sendingTask;
		private Task outputTask;
		private Task receivingTask;

		public MainWindow()
		{
			// With help from http://www.networkcomms.net/creating-a-wpf-chat-client-server-application/
			// They used ProtoContract and ProtoMember as attribute.. 5 years ago.. very funny :)

			cancellationTokenSource = new CancellationTokenSource();

			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			InitializeComponent();
		}

		private async void Window_Loaded(object sender, RoutedEventArgs e)
		{
			try
			{
				call = await ConnectToServer();

				var outgoing = outgoingMessages
					.GetConsumingEnumerable();
				sendingTask = Task.Run(() => SendMessageOverTheWire(outgoing));

				var incoming = incomingMessages
					.GetConsumingEnumerable();
				outputTask = Task.Run(() => OutputMessages(incoming));

				await Login($"Max{new Random().Next(1, 100)}");

				receivingTask = ReceivingResponses(cancellationTokenSource.Token);
			}
			catch (Exception exception)
			{
				await AppendLineToChatBox(exception.ToString());
			}
		}

		public async Task<AsyncDuplexStreamingCall<ChatMessagesRequest, ChatMessagesResponse>> ConnectToServer()
		{
			await AppendLineToChatBox($"Trying to connect to server...");
			var client = CreateClient();
			call = client.SendMessages(cancellationToken: cancellationTokenSource.Token);
			return call;
		}

		public static Chat.ChatClient CreateClient()
		{
			var channel = GrpcChannel.ForAddress("https://localhost:5001");
			var client = new Chat.ChatClient(channel);
			return client;
		}

		private async Task ReceivingResponses(CancellationToken cancellationToken)
		{
			IAsyncEnumerable<ChatMessagesResponse> responses = call.ResponseStream.ReadAllAsync(cancellationTokenSource.Token);
			await foreach (var response in responses)
			{
				incomingMessages.Add(response, cancellationToken);
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

		public async Task SendMessageOverTheWire(IEnumerable<ChatMessagesRequest> messages)
		{
			foreach (var message in messages)
			{
				await call.RequestStream.WriteAsync(message);
			}
		}

		private async Task OutputMessages(IEnumerable<ChatMessagesResponse> incoming)
		{
			foreach (var response in incoming)
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
		}

		/// <summary>
		///     Append the provided message to the chatBox text box.
		/// </summary>
		/// <param name="message"></param>
		public async Task AppendLineToChatBox(string message)
		{
			//To ensure we can successfully append to the text box from any thread
			//we need to wrap the append within an invoke action.
			await chatBox.Dispatcher.BeginInvoke(new Action<string>((messageToAdd) =>
			{
				chatBox.AppendText(messageToAdd + Environment.NewLine);
				chatBox.ScrollToEnd();
			}), new object[] {message});
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
			SendMessage();
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
				SendMessage();
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

			await receivingTask;
			outgoingMessages.CompleteAdding();
			await outputTask;

			if(call != null)
			{
				// Dispose because i dispose everything. It should not cancel here if we did everything right
				call.Dispose();
				call = null;
			}

			// Just to be sure :)
			cancellationTokenSource.Cancel();
			cancellationTokenSource.Dispose();
		}

		/// <summary>
		///     Send our message.
		/// </summary>
		private void SendMessage()
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
					Id = userId.ToString(),
					Message = messageText.Text
				}
			};

			outgoingMessages.Add(request);

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
	}
}