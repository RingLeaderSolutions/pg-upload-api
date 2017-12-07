using System;
using System.Threading.Tasks;
using Flurl.Http;

namespace RLS.PortfolioGeneration.UploadAPI
{
    public class ReportUploadApiService
    {
        private readonly UploadApiConfiguration _configuration;

        public ReportUploadApiService(UploadApiConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task ReportSuccessfulLoaUpload(string portfolioId, string accountId, string fileName)
        {
            var payload = new ReportLoaUploadRequest
            {
                AccountId = accountId,
                BlobFileName = fileName,
                Received = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                Expiry = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-ddTHH:mm:ss")
            };

            await MakeApiRequest($"loa/{portfolioId}", payload);
        }

        public async Task ReportSuccessfulSiteListUpload(string portfolioId, string accountId, string fileName)
        {
            var payload = new ReportSiteListUploadRequest
            {
                PortfolioId = portfolioId,
                CsvNames = new[] { fileName },
                Notes = $"Uploaded ${DateTime.UtcNow}"
            };

            await MakeApiRequest($"sitelist/{accountId}", payload);
        }

        public async Task ReportSuccessfulSupplyMeterDataUpload(string portfolioId, string accountId, string fileName)
        {
            var payload = new ReportSupplyDataUploadRequest
            {
                PortfolioId = portfolioId,
                CsvNames = new[] { fileName },
                Notes = $"Uploaded ${DateTime.UtcNow}"
            };

            await MakeApiRequest($"supplymeterdata/{accountId}", payload);
        }

        public async Task ReportSuccessfulHistoricalUpload(string portfolioId, string fileName)
        {
            var payload = new ReportHistoricUploadRequest
            {
                CsvNames = new[] {fileName},
                Notes = $"Uploaded ${DateTime.UtcNow}"
            };

            await MakeApiRequest($"portfolio/{portfolioId}", payload);
        }

        private async Task MakeApiRequest(string endPoint, object postData)
        {
            var fullUri = $"{_configuration.ReportUploadApiUri}/portman-web/upload/{endPoint}";

            var response = await fullUri.PostJsonAsync(postData);
            response.EnsureSuccessStatusCode();
        }
    }

    public sealed class ReportLoaUploadRequest
    {
        public string Id { get; } = "";

        public string BlobFileName { get; set; }

        public string AccountId { get; set; }

        public string DocumentType { get; } = "loa";

        public string Received { get; set; }

        public string Expiry { get; set; }
    }

    public sealed class ReportHistoricUploadRequest
    {
        public string UploadType { get; } = "HISTORICAL";

        public string[] CsvNames { get; set; }

        public string Notes { get; set; }
    }

    public sealed class ReportSupplyDataUploadRequest
    {
        public string PortfolioId { get; set; }

        public string UploadType { get; } = "SUPPLYDATA";

        public string[] CsvNames { get; set; }

        public string Notes { get; set; }
    }

    public sealed class ReportSiteListUploadRequest
    {
        public string PortfolioId { get; set; }

        public string UploadType { get; } = "SITELIST";

        public string[] CsvNames { get; set; }

        public string Notes { get; set; }
    }
}