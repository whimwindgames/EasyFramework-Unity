using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace EasyFramework.Services.Network
{
    public sealed class HttpService : IHttpService
    {
        readonly IHttpTransport _transport;

        public HttpService(IHttpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async UniTask<TResponse> GetAsync<TResponse>(string url, HttpRequestOptions options = null)
        {
            var body = await SendWithRetryAsync(HttpMethod.Get, url, null, options ?? new HttpRequestOptions());
            return JsonConvert.DeserializeObject<TResponse>(body);
        }

        public async UniTask<TResponse> PostAsync<TRequest, TResponse>(
            string url, TRequest requestBody, HttpRequestOptions options = null)
        {
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var body = await SendWithRetryAsync(HttpMethod.Post, url, jsonBody, options ?? new HttpRequestOptions());
            return JsonConvert.DeserializeObject<TResponse>(body);
        }

        async UniTask<string> SendWithRetryAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options)
        {
            var maxAttempts = Math.Max(1, options.RetryCount + 1);
            Exception lastException = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    var (statusCode, responseBody) = await _transport.SendAsync(method, url, jsonBody, options);

                    if (statusCode >= 400)
                        throw new HttpException(statusCode, $"HTTP {statusCode} for {url}: {responseBody}");

                    return responseBody;
                }
                catch (HttpException)
                {
                    // 4xx/5xx 视为最终失败,不重试(重试只覆盖超时/瞬时网络错误)。
                    throw;
                }
                catch (Exception e)
                {
                    lastException = e;
                }
            }

            throw lastException ?? new InvalidOperationException($"HTTP request to {url} failed with no exception recorded.");
        }
    }
}
