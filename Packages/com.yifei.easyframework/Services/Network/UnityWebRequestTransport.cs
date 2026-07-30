using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace EasyFramework.Services.Network
{
    /// <summary>IHttpTransport 的真实实现;真正发起网络请求的唯一入口。
    /// UnityWebRequest 不能在 EditMode 测试里真正联网,因此不写单测——
    /// HttpService 的重试/超时/反序列化逻辑已通过 FakeHttpTransport 在 HttpServiceTests 完全覆盖。
    ///
    /// 注意:UniTask 的 UnityWebRequestAsyncOperation.ToUniTask() 会在 HTTP 协议错误
    /// (4xx/5xx,即 UnityWebRequest.Result.ProtocolError)时抛出 UnityWebRequestException,
    /// 而不是像本类型注释所暗示的那样正常返回、把状态码交给上层判断。HttpService 的约定是
    /// SendAsync 对 4xx/5xx 也要正常返回 (statusCode, body),由 HttpService 自己决定是否
    /// 转换成 HttpException(且不重试);只有连接错误/数据处理错误才应该抛异常触发重试。
    /// 因此这里显式捕获 UnityWebRequestException,对 ProtocolError 把响应码/响应体解包后
    /// 正常返回,其余情况(连接失败等)才继续抛出。</summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public async UniTask<(long statusCode, string body)> SendAsync(
            HttpMethod method, string url, string jsonBody, HttpRequestOptions options,
            CancellationToken ct)
        {
            using var request = method == HttpMethod.Get
                ? UnityWebRequest.Get(url)
                : CreatePostRequest(url, jsonBody);

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = options.TimeoutSeconds;

            if (options.Headers != null)
                foreach (var header in options.Headers)
                    request.SetRequestHeader(header.Key, header.Value);
            if (!string.IsNullOrWhiteSpace(options.IdempotencyKey))
                request.SetRequestHeader("Idempotency-Key", options.IdempotencyKey);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: ct);
            }
            catch (UnityWebRequestException e) when (e.Result == UnityWebRequest.Result.ProtocolError)
            {
                // HTTP 层面的 4xx/5xx:交给 HttpService 判断,不在 transport 层当作异常。
                return (e.ResponseCode, e.Text);
            }
            catch (UnityWebRequestException e)
            {
                // 连接错误 / 数据处理错误等瞬时性故障:转换成 TimeoutException 让 HttpService 重试。
                throw new TimeoutException($"Network transport failed: {e.Error}");
            }

            return (request.responseCode, request.downloadHandler.text);
        }

        static UnityWebRequest CreatePostRequest(string url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }
    }
}
