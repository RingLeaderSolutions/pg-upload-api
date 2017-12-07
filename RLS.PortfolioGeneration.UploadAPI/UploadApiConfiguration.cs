namespace RLS.PortfolioGeneration.UploadAPI
{
    public sealed class UploadApiConfiguration
    {
        public string StorageAccountName { get; set; }

        public string StorageAccountKey { get; set; }

        public string StorageBlobName { get; set; }

        public string ReportUploadApiUri { get; set; }
    }
}
