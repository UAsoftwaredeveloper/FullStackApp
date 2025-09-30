using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ClientApp.Models;

namespace ClientApp.Services
{
    public interface IProductService
    {
        Task<Product[]> GetProductsAsync(bool forceRefresh = false, CancellationToken ct = default);
        void ClearCache();
    }

    public class ProductService : IProductService
    {
        private readonly HttpClient _http;
        private Product[]? _cache;
        private Task<Product[]>? _loadingTask;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly string _endpoint = "http://localhost:5286/api/productlist";

        public ProductService(HttpClient http)
        {
            _http = http;
        }

        public async Task<Product[]> GetProductsAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!forceRefresh && _cache != null)
                return _cache;

            await _lock.WaitAsync(ct);
            try
            {
                if (!forceRefresh && _cache != null)
                    return _cache;

                if (_loadingTask != null && !_loadingTask.IsCompleted)
                {
                    // Another caller is already fetching; await that task to dedupe requests
                    return await _loadingTask;
                }

                _loadingTask = FetchProductsAsync(ct);
                _cache = await _loadingTask;
                _loadingTask = null;
                return _cache;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<Product[]> FetchProductsAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var result = await _http.GetFromJsonAsync<Product[]>(_endpoint, cts.Token);
            return result ?? Array.Empty<Product>();
        }

        public void ClearCache()
        {
            _cache = null;
        }
    }
}
