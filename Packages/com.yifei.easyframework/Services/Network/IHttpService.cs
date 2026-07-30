using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Network
{
    public enum HttpMethod { Get, Post } // Put/Delete 目前没有具体需求驱动,真正用到时再加

    public sealed class HttpRequestOptions
    {
        public IReadOnlyDictionary<string, string> Headers;
        public int TimeoutSeconds = 10;
        public int RetryCount = 2;
        /// <summary>POST 等非幂等请求默认不重试;开启时还必须提供 IdempotencyKey。</summary>
        public bool RetryNonIdempotent;
        public string IdempotencyKey;
        public int BaseRetryDelayMs = 250;
        public int MaxRetryDelayMs = 4_000;
    }

    public sealed class HttpException : System.Exception
    {
        public long StatusCode { get; }
        public string ResponseBody { get; }
        public HttpException(long statusCode, string responseBody)
            : base($"HTTP request failed with status {statusCode}.")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }

    public interface IHttpService
    {
        UniTask<TResponse> GetAsync<TResponse>(
            string url, HttpRequestOptions options = null, CancellationToken ct = default);
        UniTask<TResponse> PostAsync<TRequest, TResponse>(
            string url, TRequest body, HttpRequestOptions options = null,
            CancellationToken ct = default);
    }
}
