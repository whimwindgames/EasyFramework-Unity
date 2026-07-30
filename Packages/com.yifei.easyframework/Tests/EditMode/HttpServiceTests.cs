using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyFramework.Services.Network;
using Newtonsoft.Json;
using NUnit.Framework;
using VContainer;

namespace EasyFramework.Tests
{
    public class HttpServiceTests
    {
        sealed class FakeHttpTransport : IHttpTransport
        {
            public Queue<(long statusCode, string body)> Responses = new();
            public List<(HttpMethod method, string url, string jsonBody, HttpRequestOptions options)> Calls = new();
            public bool ThrowTimeoutOnce;
            public bool AlwaysThrowTimeout;
            bool _timeoutThrown;

            public UniTask<(long statusCode, string body)> SendAsync(
                HttpMethod method, string url, string jsonBody, HttpRequestOptions options,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Calls.Add((method, url, jsonBody, options));

                if (AlwaysThrowTimeout)
                    throw new TimeoutException("simulated timeout");

                if (ThrowTimeoutOnce && !_timeoutThrown)
                {
                    _timeoutThrown = true;
                    throw new TimeoutException("simulated timeout");
                }

                if (Responses.Count == 0)
                    throw new InvalidOperationException("FakeHttpTransport has no queued response.");

                return UniTask.FromResult(Responses.Dequeue());
            }
        }

        static HttpService MakeService(FakeHttpTransport transport)
            => new HttpService(transport, (_, __) => UniTask.CompletedTask);

        sealed class Payload
        {
            public string Name;
            public int Value;
        }

        [Test]
        public void GetAsync_DeserializesSuccessResponse()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "a", Value = 1 })));
            var svc = MakeService(transport);

            var result = svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult();

            Assert.AreEqual("a", result.Name);
            Assert.AreEqual(1, result.Value);
            Assert.AreEqual(HttpMethod.Get, transport.Calls[0].method);
        }

        [Test]
        public void PostAsync_SendsSerializedBody_DeserializesResponse()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "b", Value = 2 })));
            var svc = MakeService(transport);

            var result = svc.PostAsync<Payload, Payload>(
                "https://example.com/api", new Payload { Name = "req", Value = 9 }).GetAwaiter().GetResult();

            Assert.AreEqual("b", result.Name);
            Assert.AreEqual(HttpMethod.Post, transport.Calls[0].method);
            StringAssert.Contains("\"req\"", transport.Calls[0].jsonBody);
        }

        [Test]
        public void GetAsync_RetriesOnTimeout_ThenSucceeds()
        {
            var transport = new FakeHttpTransport { ThrowTimeoutOnce = true };
            transport.Responses.Enqueue((200, JsonConvert.SerializeObject(new Payload { Name = "c", Value = 3 })));
            var svc = MakeService(transport);

            var result = svc.GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult();

            Assert.AreEqual("c", result.Name);
            Assert.AreEqual(2, transport.Calls.Count); // 第一次超时 + 一次重试成功
        }

        [Test]
        public void GetAsync_4xxResponse_ThrowsHttpException()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((404, "not found"));
            var svc = MakeService(transport);

            var ex = Assert.Throws<HttpException>(() =>
                svc.GetAsync<Payload>("https://example.com/api",
                    new HttpRequestOptions { RetryCount = 0 }).GetAwaiter().GetResult());
            Assert.AreEqual(404, ex.StatusCode);
            StringAssert.DoesNotContain("not found", ex.Message);
        }

        [Test]
        public void GetAsync_5xxResponse_ThrowsHttpException()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((500, "server error"));
            var svc = MakeService(transport);

            var ex = Assert.Throws<HttpException>(() =>
                svc.GetAsync<Payload>("https://example.com/api",
                    new HttpRequestOptions { RetryCount = 0 }).GetAwaiter().GetResult());
            Assert.AreEqual(500, ex.StatusCode);
        }

        [Test]
        public void GetAsync_RetryCountExhausted_ThrowsFinalFailure()
        {
            var transport = new FakeHttpTransport { AlwaysThrowTimeout = true };
            // RetryCount=2 意味着最多尝试 3 次(1 次初始 + 2 次重试),全部超时。
            var svc = MakeService(transport);
            var options = new HttpRequestOptions { RetryCount = 2 };

            var ex = Assert.Throws<TimeoutException>(() =>
                svc.GetAsync<Payload>("https://example.com/api", options).GetAwaiter().GetResult());

            Assert.IsNotNull(ex);
            Assert.AreEqual(3, transport.Calls.Count); // 1 次初始 + 2 次重试,全部耗尽
        }

        [Test]
        public void GetAsync_RetriesTransient5xxThenSucceeds()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue((503, "temporary"));
            transport.Responses.Enqueue((200,
                JsonConvert.SerializeObject(new Payload { Name = "ok", Value = 4 })));
            var result = MakeService(transport)
                .GetAsync<Payload>("https://example.com/api").GetAwaiter().GetResult();
            Assert.AreEqual("ok", result.Name);
            Assert.AreEqual(2, transport.Calls.Count);
        }

        [Test]
        public void PostAsync_Timeout_DoesNotRetryByDefault()
        {
            var transport = new FakeHttpTransport { AlwaysThrowTimeout = true };
            Assert.Throws<TimeoutException>(() =>
                MakeService(transport).PostAsync<Payload, Payload>(
                    "https://example.com/api", new Payload()).GetAwaiter().GetResult());
            Assert.AreEqual(1, transport.Calls.Count);
        }

        [Test]
        public void PostAsync_WithIdempotencyKey_CanRetry()
        {
            var transport = new FakeHttpTransport { ThrowTimeoutOnce = true };
            transport.Responses.Enqueue((200,
                JsonConvert.SerializeObject(new Payload { Name = "ok" })));
            var options = new HttpRequestOptions
            {
                RetryNonIdempotent = true,
                IdempotencyKey = "transaction-1",
            };
            var result = MakeService(transport).PostAsync<Payload, Payload>(
                "https://example.com/api", new Payload(), options).GetAwaiter().GetResult();
            Assert.AreEqual("ok", result.Name);
            Assert.AreEqual(2, transport.Calls.Count);
        }

        static EasyFramework.FrameworkOptions MakeFrameworkOptions()
            => new EasyFramework.FrameworkOptions
            {
                SaveDirectory = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "ef_http_" + System.Guid.NewGuid().ToString("N")),
            };

        [Test]
        public void FrameworkInstaller_RegistersHttpService_ResolvableAsIHttpService()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptions());
            using var container = builder.Build();

            var resolved = container.Resolve<IHttpService>();

            Assert.IsInstanceOf<HttpService>(resolved);
        }

        [Test]
        public void G_Http_IsExposedAsGHttp()
        {
            var builder = new VContainer.ContainerBuilder();
            EasyFramework.FrameworkInstaller.Install(builder, MakeFrameworkOptions());
            using var container = builder.Build();

            EasyFramework.G.Initialize(container);
            try
            {
                Assert.AreSame(container.Resolve<IHttpService>(), EasyFramework.G.Http);
            }
            finally
            {
                EasyFramework.G.Reset();
            }
        }
    }
}
