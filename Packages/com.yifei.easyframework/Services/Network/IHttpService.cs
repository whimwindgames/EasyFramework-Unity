using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Network
{
    public enum HttpMethod { Get, Post } // Put/Delete 目前没有具体需求驱动,真正用到时再加

    public sealed class HttpRequestOptions
    {
        public IReadOnlyDictionary<string, string> Headers;
        public int TimeoutSeconds = 10;
        public int RetryCount = 2;
    }

    public sealed class HttpException : System.Exception
    {
        public long StatusCode { get; }
        public HttpException(long statusCode, string message) : base(message) => StatusCode = statusCode;
    }

    public interface IHttpService
    {
        UniTask<TResponse> GetAsync<TResponse>(string url, HttpRequestOptions options = null);
        UniTask<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, HttpRequestOptions options = null);
    }
}
