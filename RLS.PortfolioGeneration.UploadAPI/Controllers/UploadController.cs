using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        private ReportUploadApiService _reportService;

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

            _reportService = new ReportUploadApiService(configuration.Value);
        }

        [HttpPost("supply/gas/{portfolioId}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadGasSupplyMeterData(string accountId, ICollection<IFormFile> files, string portfolioId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulSupplyMeterDataUpload(portfolioId, accountId, uploadedFileName, UtilityType.Gas);
            return await UploadFile(files, UploadType.MeterSupplyData, portfolioId, ReportAction);
        }

        [HttpPost("supply/electricity/{portfolioId}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadElectricitySupplyMeterData(string accountId, ICollection<IFormFile> files, string portfolioId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulSupplyMeterDataUpload(portfolioId, accountId, uploadedFileName, UtilityType.Electricity);
            return await UploadFile(files, UploadType.MeterSupplyData, portfolioId, ReportAction);
        }

        [HttpPost("historic/{portfolioId}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadHistoric(ICollection<IFormFile> files, string portfolioId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulHistoricalUpload(portfolioId, uploadedFileName);
            return await UploadFile(files, UploadType.Historic, portfolioId, ReportAction);
        }

        [HttpPost("loa/{portfolioId}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadLoa(string accountId, ICollection<IFormFile> files, string portfolioId)
        { 
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulLoaUpload(portfolioId, accountId, uploadedFileName);
            return await UploadFile(files, UploadType.LetterOfAuthority, portfolioId, ReportAction);
        }

        [HttpPost("sites/{portfolioId}")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadSiteList(string accountId, ICollection<IFormFile> files, string portfolioId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulSiteListUpload(portfolioId, accountId, uploadedFileName);
            return await UploadFile(files, UploadType.SiteList, portfolioId, ReportAction);
        }

        [HttpPost("backing/{tenderId}/gas")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadGasBackingSheet(ICollection<IFormFile> files, string tenderId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulGasBackingSheetUpload(tenderId, uploadedFileName);
            return await UploadFile(files, UploadType.BackingSheet, tenderId, ReportAction);
        }

        [HttpPost("backing/{tenderId}/electricity")]
        [Consumes("application/json", "application/json-patch+json", "multipart/form-data")]
        public async Task<ObjectResult> UploadElectricityBackingSheet(ICollection<IFormFile> files, string tenderId)
        {
            async void ReportAction(string uploadedFileName) => await _reportService.ReportSuccessfulElectricityBackingSheetUpload(tenderId, uploadedFileName);
            return await UploadFile(files, UploadType.BackingSheet, tenderId, ReportAction);
        }

        private async Task<ObjectResult> UploadFile(ICollection<IFormFile> files, UploadType uploadType, string portfolio, Action<string> reportAction)
        {
            var fileCount = files.Count;
            _log.LogInformation($"Received request to upload [{fileCount}] [{uploadType}] files by user [Unauthenticated] for portfolioId [{portfolio}]");
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
                        successfulUploads.Add(uri);
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
            
            _log.LogInformation("Making request to processing API");

            foreach (var uploadedFileUri in successfulUploads)
            {
                var fileName = Path.GetFileName(uploadedFileUri);
                reportAction(fileName);
            }
            
            return Ok(new { success = true });
        }

        private CloudBlockBlob GetBlockBlobForFileName(string fileName)
        {
            var blobClient = _storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(_configuration.StorageBlobName);

            return container.GetBlockBlobReference(fileName);
        }

        private async Task<string> UploadFile(Stream stream, string originalFilename, UploadType type, string portfolioId, string user)
        {
            var extension = Path.GetExtension(originalFilename);
            var newFilename = Guid.NewGuid() + extension;

            var blockBlob = GetBlockBlobForFileName(newFilename);
            
            _log.LogInformation($"Sending upload request for blob file: [{originalFilename}] - [{newFilename}]");
            await blockBlob.UploadFromStreamAsync(stream, AccessCondition.GenerateIfNotExistsCondition(), _blobRequestOptions, new OperationContext());

            _log.LogInformation($"Setting blob metadata: [{originalFilename} - {newFilename}]");
            blockBlob.Metadata.Add("uploadType", type.ToString());
            blockBlob.Metadata.Add("portfolioId", portfolioId);
            blockBlob.Metadata.Add("user", user);
            blockBlob.Metadata.Add("uploadTime", DateTime.UtcNow.ToString("O"));
            blockBlob.Metadata.Add("originalFilename", originalFilename);
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
