using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Errors;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;

namespace TimeInformationClient
{
	class Program
	{
		static async Task Main(string[] args)
		{
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			IConfigurationRoot configuration = ReadConfiguration();
			var serverUrl = GetServerUrl(configuration);

			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			CancellationToken token = cancellationTokenSource.Token;

			CancelApplication(cancellationTokenSource);

			var channel = GrpcChannel.ForAddress(new Uri(serverUrl), new GrpcChannelOptions() { Credentials = ChannelCredentials.Insecure});
			Errors.ErrorService.ErrorServiceClient client = new Errors.ErrorService.ErrorServiceClient(channel);

			try
			{
				await client.IDoNotHandleErrorCorrectlyAsync(new ErrorRequest());
			}
			catch (RpcException rpcException)
			{
				Console.WriteLine($"{rpcException.Status}");
				Console.WriteLine($"Not really useful right?");
				Console.WriteLine();
			}

			try
			{
				await client.IDoNotLogRpcExceptionCorrectlyAsync(new ErrorRequest());
			}
			catch (RpcException rpcException)
			{
				Console.WriteLine($"{rpcException.Status}");
				Console.WriteLine("Better.. but not what us really helps right? Also check the missing server logs.");
				Console.WriteLine();
			}

			try
			{
				await client.GiveMeADetailedUnhandledExceptionAsync(new ErrorRequest());
			}
			catch (RpcException rpcException)
			{
				Console.WriteLine($"{rpcException.Status}");
				Console.WriteLine($"Same but now it is logged on server side correctly. And with the error id we can check the logs.");
				Console.WriteLine();
			}

			try
			{
				await client.GiveMeADetailedErrorAsync(new GiveMeADetailedErrorRequest());
			}
			catch (RpcException rpcException)
			{
				Console.WriteLine($"{rpcException.Status}");
				Console.WriteLine($"Looks the same right?");
				var detailedErrorEntry = rpcException.Trailers.FirstOrDefault(e => e.Key == $"{nameof(GiveMeADetailedErrorError).ToLower()}-bin");
				if (detailedErrorEntry != null)
				{
					Console.WriteLine($"But lets take look at the trailing metadata.");
					GiveMeADetailedErrorError detailError = GiveMeADetailedErrorError.Parser.ParseFrom(detailedErrorEntry.ValueBytes);
					Console.WriteLine($"Error: {detailError.ErrorCase}.");
					switch (detailError.ErrorCase)
					{
						case GiveMeADetailedErrorError.ErrorOneofCase.None:
							return;
						case GiveMeADetailedErrorError.ErrorOneofCase.PermissionDenied:
							Console.WriteLine($"User: {detailError.PermissionDenied.User} Reason: {detailError.PermissionDenied.Reason}");
							return;
						default:
							throw new InvalidOperationException($"{detailError.ErrorCase} is not supported.");
					}
				}
				else
				{
					Console.WriteLine("No detailed error in trailing metadata found.");
				}
			}

		}

		private static void CancelApplication(CancellationTokenSource cancellationTokenSource)
		{
			Console.CancelKeyPress += (sender, eventArgs) =>
			{
				Console.WriteLine("Closing the connection to server!");
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
