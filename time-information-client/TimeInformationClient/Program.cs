using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Streaming.Server;

namespace TimeInformationClient
{
	class Program
	{
		static async Task Main(string[] args)
		{
			AppContext.SetSwitch(
				"System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			IConfigurationRoot configuration = ReadConfiguration();
			var serverUrl = GetServerUrl(configuration);

			Console.Write("What is your name?: ");
			var userName = Console.ReadLine();

			var channel = GrpcChannel.ForAddress(new Uri(serverUrl), new GrpcChannelOptions() { Credentials = ChannelCredentials.Insecure});
			TimeInformationService.TimeInformationServiceClient client = new TimeInformationService.TimeInformationServiceClient(channel);
			await TimePingWithCancellationToken(userName, client);

			// await TimePingWithGoodBye(userName, client);

		}

		private static async Task TimePingWithGoodBye(string userName, TimeInformationService.TimeInformationServiceClient client)
		{
			Console.Write("How long should i wait? (seconds): ");
			var waitTime = double.Parse(Console.ReadLine());

			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			CancellationToken token = cancellationTokenSource.Token;

			CancelApplication(cancellationTokenSource);

			using (var call = client.TimePingWithGoodBye(cancellationToken: token))
			{
				var outputTask = Task.Run(() => PrintReponse(call.ResponseStream));

				while (!token.IsCancellationRequested)
				{
					await call.RequestStream.WriteAsync(new TimePingRequest() { ClientName = userName });
					await Task.Delay(TimeSpan.FromSeconds(waitTime), token);
				}

				await call.RequestStream.WriteAsync(new TimePingRequest() { ClientName = userName, GoodBye = true });

				await call.RequestStream.CompleteAsync();

				await outputTask;
			};
		}

		private static async Task TimePingWithCancellationToken(string userName, TimeInformationService.TimeInformationServiceClient client)
		{
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			CancellationToken token = cancellationTokenSource.Token;

			using (var response = client.TimePing(new TimePingRequest() { ClientName = userName }))
			{
				CancelApplication(cancellationTokenSource);

				try
				{
					while (await response.ResponseStream.MoveNext(token))
					{
						TimePingResponse timePingReply = response.ResponseStream.Current;
						Console.WriteLine($"{timePingReply.TimeNow.ToDateTimeOffset().ToString()} : {timePingReply.Message}");
					}
				}
				catch (RpcException e)
				{
					if (e.StatusCode != StatusCode.Cancelled)
					{
						throw;
					}
				}
			}
		}

		private static async Task PrintReponse(IAsyncStreamReader<TimePingResponse> responseStream)
		{
			while(await responseStream.MoveNext(CancellationToken.None))
			{
				var timePingReply = responseStream.Current;
				Console.WriteLine($"{timePingReply.TimeNow.ToDateTimeOffset().ToString()} : {timePingReply.Message}");
			}
		}

		private static void CancelApplication(CancellationTokenSource cancellationTokenSource)
		{
			Console.CancelKeyPress += (sender, eventArgs) =>
			{
				Console.WriteLine("Closing the connection and send good bye!");
				cancellationTokenSource.Cancel();
				eventArgs.Cancel = true;
			};
		}

		private static string GetServerUrl(IConfigurationRoot configuration)
		{
			var serverUrl = configuration.GetSection("ServerUrl").Value;
			Console.WriteLine($"ServerUrl: {serverUrl}");
			return serverUrl;
		}

		private static IConfigurationRoot ReadConfiguration()
		{
			var builder = new ConfigurationBuilder()
							.SetBasePath(Directory.GetCurrentDirectory())
							.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

			IConfigurationRoot configuration = builder.Build();
			return configuration;
		}
	}
}
