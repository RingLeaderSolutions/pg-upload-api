using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using RLS.PortfolioGeneration.UploadAPI.Model;

namespace RLS.PortfolioGeneration.UploadAPI.Controllers
{

    [Route("/api/upload")]
    public class UploadController : Controller   
    {
        private readonly ILogger<UploadController> _log;

        private readonly CloudStorageAccount _storageAccount;
        private readonly UploadApiConfiguration _configuration;
        private readonly BlobRequestOptions _blobRequestOptions;

        public UploadController(IOptions<UploadApiConfiguration> configuration, ILogger<UploadController> logger)
        {
            _log = logger;
            _configuration = configuration.Value;

            var credentials = new StorageCredentials(_configuration.StorageAccountName, _configuration.StorageAccountKey);
            _storageAccount = new CloudStorageAccount(credentials, true);

            _blobRequestOptions = new BlobRequestOptions
            {
                RetryPolicy = new ExponentialRetry(),
                ServerTimeout = TimeSpan.FromSeconds(30)
            };
        }

        [HttpPost("topline/{portfolio}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public Task<ObjectResult> UploadTopline(ICollection<IFormFile> files, string portfolio)
        {
            return UploadFile(files, UploadType.Topline, portfolio);
        }

        [HttpPost("historic/{portfolio}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public Task<ObjectResult> UploadHistoric(ICollection<IFormFile> files, string portfolio)
        {
            return UploadFile(files, UploadType.Historic, portfolio);
        }
        
        private async Task<ObjectResult> UploadFile(ICollection<IFormFile> files, UploadType uploadType, string portfolio)
        {
            var fileCount = files.Count;
            _log.LogInformation($"Received request to upload [{fileCount}] [{uploadType}] files by user [Unauthenticated] for portfolio [{portfolio}]");
            var successfulUploads = new List<string>();
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                var fileName = file.FileName;

                try
                {
                    _log.LogInformation($"Attempting to upload file: [{fileName}]");
                    var uri = await UploadFile(stream, fileName, uploadType, portfolio, "Unauthenticated");
                    if (!string.IsNullOrEmpty(uri))
                    {
                        _log.LogInformation($"Successfully uploaded: [{fileName}]");
                        successfulUploads.Add(fileName);
                    }
                }
                catch (StorageException storageException)
                {
                    _log.LogError($"Failed to upload file: [{fileName}], Exception: [{storageException.Message}]");
                    _log.LogError($"{storageException}");
                    break;
                }
            }

            var uploadCount = successfulUploads.Count;

            if (uploadCount != fileCount)
            {
                _log.LogInformation($"Detected [{uploadCount}/{fileCount}] uploads successful.");

                if (uploadCount > 0)
                {
                    _log.LogInformation($"Deleting successful uploads from blob storage to return to earlier state.");

                    foreach (var fileName in successfulUploads)
                    {
                        await DeleteFile(fileName);
                    }
                }
                
                return StatusCode(500, new { Error = "Upload cancelled due to internal server error."});
            }

            // TODO: At this point we know we have successfully uploaded all files, so we need to call the processing endpoint.
            // _log.LogInformation("Making request to processing API");
            return Ok(new { success = true });
        }

        private CloudBlockBlob GetBlockBlobForFileName(string fileName)
        {
            var blobClient = _storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_configuration.StorageBlobName);

            return container.GetBlockBlobReference(fileName);
        }

        private async Task<string> UploadFile(Stream stream, string fileName, UploadType type, string portfolio, string user)
        {
            var blockBlob = GetBlockBlobForFileName(fileName);
            
            _log.LogInformation($"Sending upload request for blob file: [{fileName}]");
            await blockBlob.UploadFromStreamAsync(stream, AccessCondition.GenerateIfNotExistsCondition(), _blobRequestOptions, new OperationContext());

            _log.LogInformation($"Setting blob metadata: [{fileName}]");
            blockBlob.Metadata.Add("uploadType", type.ToString());
            blockBlob.Metadata.Add("portfolio", portfolio);
            blockBlob.Metadata.Add("user", user);
            blockBlob.Metadata.Add("uploadTime", DateTime.UtcNow.ToString("O"));
            await blockBlob.SetMetadataAsync();

            stream.Dispose();
            return blockBlob.Uri.ToString();
        }

        private async Task DeleteFile(string fileName)
        {
            var blockBlob = GetBlockBlobForFileName(fileName);

            _log.LogInformation($"Sending delete request for blob file: [{fileName}]");
            await blockBlob.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots, AccessCondition.GenerateEmptyCondition(), _blobRequestOptions, new OperationContext());
        }
    }
}
