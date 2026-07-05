using Cysharp.Threading.Tasks;

namespace EasyFramework.Services.Network
{
    /// <summary>真实网络 I/O 的边界。真实实现 UnityWebRequestTransport 走真实网络;
    /// HttpService 的重试/超时/反序列化逻辑通过注入 FakeHttpTransport 完全脱离真实网络单测。</summary>
    public interface IHttpTransport
    {
        UniTask<(long statusCode, string body)> SendAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options);
    }
}
