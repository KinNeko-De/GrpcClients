using FileTransfer;
using Grpc.Core;
using Grpc.Net.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileTransferClient
{
	class Program
	{
		static async Task Main(string[] args)
		{
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

			var channel = GrpcChannel.ForAddress("https://localhost:5001");
			var grpcClient = new FileTransfer.Files.FilesClient(channel);

			var fileName = "medium.txt";
			var sourceFile = Path.Combine(Path.GetTempPath(), "FileTransfer", "Files", fileName);

			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
			using (var call = grpcClient.StartUpload())
			{

				await SendFileMetadata(fileName, call);

				await SendFilePayload(sourceFile, call, cancellationTokenSource.Token);

				await call.RequestStream.CompleteAsync();

				await call.ResponseAsync;
				
			}

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
			var chunksize = 0x1F400;
			byte[] chunk = new byte[chunksize];

			using (FileStream fileToUpload = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, chunksize))
			{
				int bytesRead = 0;
				while((bytesRead = await fileToUpload.ReadAsync(chunk, 0, chunksize, cancellationToken)) > 0)
				{
					var filePayloadRequest = new StartUploadRequest()
					{
						FilePayload = new FilePayload
						{
							Chunk = Google.Protobuf.ByteString.CopyFrom(new Span<byte>(chunk, 0, bytesRead))
						}
					};

					await call.RequestStream.WriteAsync(filePayloadRequest);
				}
			}
		}
	}
}
