using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace SnapToolCloud.Storage
{
    public class AzureBlobService
    {
        private readonly BlobServiceClient _blobService;
        private readonly string _containerName;

        public AzureBlobService(string connectionString, string containerName)
        {
            _blobService = new BlobServiceClient(connectionString);
            _containerName = containerName;
        }

        private BlobContainerClient GetContainer()
        {
            var container = _blobService.GetBlobContainerClient(_containerName);
            container.CreateIfNotExists(PublicAccessType.None);
            return container;
        }

        public async Task<string> UploadAsync(string blobName, Stream data, string contentType = "application/octet-stream")
        {
            var container = GetContainer();
            var blob = container.GetBlobClient(blobName);

            await blob.UploadAsync(data, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });

            return blob.Uri.ToString();
        }

        public async Task<bool> ExistsAsync(string blobName)
        {
            var container = GetContainer();
            var blob = container.GetBlobClient(blobName);
            return await blob.ExistsAsync();
        }

        public async Task DeleteAsync(string blobName)
        {
            var container = GetContainer();
            var blob = container.GetBlobClient(blobName);
            await blob.DeleteIfExistsAsync();
        }

        public async Task<Stream?> DownloadAsync(string blobName)
        {
            var container = GetContainer();
            var blob = container.GetBlobClient(blobName);

            if (!await blob.ExistsAsync())
                return null;

            var ms = new MemoryStream();
            await blob.DownloadToAsync(ms);
            ms.Position = 0;

            return ms;
        }
    }
}
