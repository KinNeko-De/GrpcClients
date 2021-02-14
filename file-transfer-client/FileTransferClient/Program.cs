﻿using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Files;

namespace FileTransferClient
{
	class Program
	{
		static async Task Main(string[] args)
		{
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			var channel = GrpcChannel.ForAddress("https://localhost:5001");
			var grpcClient = new TransferService.TransferServiceClient(channel);

			var fileName = "medium.txt";
			var sourceFile = Path.Combine(Path.GetTempPath(), "FileTransfer", "Files", fileName);
			if (!File.Exists(sourceFile))
			{
				throw new FileNotFoundException("File to upload not found.", sourceFile);
			}

			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			using var call = grpcClient.StartUpload();

			await SendFileMetadata(fileName, call);

			await SendFilePayload(sourceFile, call, cancellationTokenSource.Token);

			await call.RequestStream.CompleteAsync();

			await call;
		}

		private static async Task SendFileMetadata(string fileName, AsyncClientStreamingCall<StartUploadRequest, StartUploadResponse> call)
		{
			var fileMetadataRequest = new StartUploadRequest()
			{
				FileMetadata = new FileMetadata
				{
					FileName = fileName
				}
			};
			await call.RequestStream.WriteAsync(fileMetadataRequest);
		}

		private static async Task SendFilePayload(string sourceFile, AsyncClientStreamingCall<StartUploadRequest, StartUploadResponse> call, CancellationToken cancellationToken)
		{
			var chunkSize = 0x1F400;
			byte[] chunk = new byte[chunkSize];
			Memory<byte> memory = new Memory<byte>(chunk);

			await using FileStream fileToUpload = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize);

			int bytesRead = 0;
			while((bytesRead = await fileToUpload.ReadAsync(memory, cancellationToken)) > 0)
			{
				var filePayloadRequest = new StartUploadRequest()
				{
					FilePayload = new FilePayload
					{
						Chunk = Google.Protobuf.ByteString.CopyFrom(memory.Slice(0, bytesRead).Span)
					}
				};

				await call.RequestStream.WriteAsync(filePayloadRequest);
			}
		}
	}
}
