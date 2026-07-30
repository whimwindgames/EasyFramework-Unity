using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace EasyFramework.Services.Network
{
    public sealed class HttpService : IHttpService
    {
        readonly IHttpTransport _transport;
        readonly Func<TimeSpan, CancellationToken, UniTask> _delay;

        public HttpService(IHttpTransport transport)
            : this(transport, (duration, ct) =>
                UniTask.Delay(duration, DelayType.Realtime, cancellationToken: ct)) { }

        internal HttpService(IHttpTransport transport,
            Func<TimeSpan, CancellationToken, UniTask> delay)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public async UniTask<TResponse> GetAsync<TResponse>(
            string url, HttpRequestOptions options = null, CancellationToken ct = default)
        {
            var body = await SendWithRetryAsync(
                HttpMethod.Get, url, null, options ?? new HttpRequestOptions(), ct);
            return Deserialize<TResponse>(body);
        }

        public async UniTask<TResponse> PostAsync<TRequest, TResponse>(
            string url, TRequest requestBody, HttpRequestOptions options = null,
            CancellationToken ct = default)
        {
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var body = await SendWithRetryAsync(
                HttpMethod.Post, url, jsonBody, options ?? new HttpRequestOptions(), ct);
            return Deserialize<TResponse>(body);
        }

        async UniTask<string> SendWithRetryAsync(
            HttpMethod method, string url, string jsonBody,
            HttpRequestOptions options, CancellationToken ct)
        {
            Validate(url, options);
            var mayRetry = method == HttpMethod.Get ||
                           (options.RetryNonIdempotent &&
                            !string.IsNullOrWhiteSpace(options.IdempotencyKey));
            var maxAttempts = mayRetry ? options.RetryCount + 1 : 1;
            Exception lastException = null;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var (statusCode, responseBody) = await _transport.SendAsync(
                        method, url, jsonBody, options, ct);
                    if (statusCode >= 400)
                    {
                        var error = new HttpException(statusCode, responseBody);
                        if (!mayRetry || !IsTransient(statusCode) || attempt + 1 >= maxAttempts)
                            throw error;
                        lastException = error;
                    }
                    else
                    {
                        return responseBody;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpException)
                {
                    throw;
                }
                catch (Exception e) when (IsTransient(e))
                {
                    lastException = e;
                    if (!mayRetry || attempt + 1 >= maxAttempts)
                        throw;
                }

                await _delay(Backoff(options, attempt), ct);
            }

            throw lastException ?? new InvalidOperationException("HTTP request failed.");
        }

        static T Deserialize<T>(string body)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(body);
            }
            catch (JsonException e)
            {
                throw new JsonSerializationException("HTTP response JSON is invalid.", e);
            }
        }

        static void Validate(string url, HttpRequestOptions options)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                throw new ArgumentException("An absolute HTTP URL is required.", nameof(url));
            if (options.TimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.TimeoutSeconds));
            if (options.RetryCount < 0)
                throw new ArgumentOutOfRangeException(nameof(options.RetryCount));
            if (options.BaseRetryDelayMs < 0 || options.MaxRetryDelayMs < 0)
                throw new ArgumentOutOfRangeException(nameof(options.BaseRetryDelayMs));
        }

        static bool IsTransient(long statusCode)
            => statusCode == 408 || statusCode == 429 || statusCode >= 500;

        static bool IsTransient(Exception exception)
            => exception is TimeoutException;

        static TimeSpan Backoff(HttpRequestOptions options, int failedAttempt)
        {
            var multiplier = Math.Pow(2, Math.Min(failedAttempt, 20));
            var milliseconds = Math.Min(
                options.MaxRetryDelayMs,
                options.BaseRetryDelayMs * multiplier);
            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }
}
