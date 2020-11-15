using System;
using Grpc.Net.Client;
using GrpcServer;

namespace ProtoClient
{
	public class GrpcChatClientProvider
	{
		public static Chat.ChatClient Create()
		{
			var channel = GrpcChannel.ForAddress("https://localhost:5001");
			var client = new Chat.ChatClient(channel);
			return client;
		}
	}
}