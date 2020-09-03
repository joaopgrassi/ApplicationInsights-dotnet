﻿using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
#if NETCOREAPP2_1
using IntegrationTests.WebApp._2._1;
#else
using IntegrationTests.WebApp._3._1;
#endif

namespace IntegrationTests.Tests
{
    public class RequestCollectionTest : IClassFixture<CustomWebApplicationFactory<Startup>>
    {
        private readonly CustomWebApplicationFactory<Startup> _factory;
        protected readonly ITestOutputHelper output;


        public RequestCollectionTest(CustomWebApplicationFactory<Startup> factory, ITestOutputHelper output)
        {
            this.output = output;
            _factory = factory;
            _factory.sentItems.Clear();
        }

        [Fact]
        public async Task RequestSuccess()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Privacy";
            var url = client.BaseAddress + path;

            // Act
            var request = CreateRequestMessage();
            request.RequestUri = new Uri(url);
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            WaitForTelemetryToArrive();

            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(1, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            Assert.NotNull(req);
            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "200",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: true);
        }

        [Fact]
        public async Task RequestSuccessActionWithParameter()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Index/5";
            var expectedName = "GET Home/Index [id]";
            var url = client.BaseAddress + path;

            // Act
            var request = CreateRequestMessage();
            request.RequestUri = new Uri(url);
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            WaitForTelemetryToArrive();

            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            Assert.NotNull(req);

            var traces = GetTelemetryOfType<TraceTelemetry>(items);
            Assert.Single(traces);
            var trace = traces[0];
            Assert.NotNull(trace);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "200",
                 expectedName: expectedName,
                 expectedUrl: url,
                 expectedSuccess: true);
        }

        [Fact]
        public async Task RequestFailed()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Error";
            var url = client.BaseAddress + path;

            // Act
            var request = CreateRequestMessage();
            request.RequestUri = new Uri(url);
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            WaitForTelemetryToArrive();

            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            var exceptions = GetTelemetryOfType<ExceptionTelemetry>(items);
            Assert.Single(reqs);
            Assert.Single(exceptions);

            var req = reqs[0];
            var exc = exceptions[0];
            Assert.NotNull(req);
            Assert.NotNull(exc);

            Assert.Equal(exc.Context.Operation.Id, req.Context.Operation.Id);
            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "500",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: false);
        }

        [Fact]
        public async Task RequestNonExistentPage()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Nonexistent";
            var url = client.BaseAddress + path;

            // Act
            var request = CreateRequestMessage();
            request.RequestUri = new Uri(url);
            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            WaitForTelemetryToArrive();

            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(1, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            Assert.NotNull(req);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "404",
                 expectedName: "GET /" + path,
                 expectedUrl: url,
                 expectedSuccess: false);
        }

        [Fact]
        public async Task RequestSuccessWithTraceParent()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Index";
            var url = client.BaseAddress + path;

            // Act
            Dictionary<string, string> requestHeaders = new Dictionary<string, string>()
                {
                    { "traceparent", "00-4e3083444c10254ba40513c7316332eb-e2a5f830c0ee2c46-00"}
                };
            var request = CreateRequestMessage(requestHeaders);
            request.RequestUri = new Uri(url);
            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            this.output.WriteLine(await response.Content.ReadAsStringAsync());

            WaitForTelemetryToArrive();

            var items = _factory.sentItems;
            PrintItems(items);
            // 1 Trace from Ilogger, 1 Request
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            var traces = GetTelemetryOfType<TraceTelemetry>(items);
            Assert.Single(traces);
            var trace = traces[0];
            Assert.NotNull(req);
            Assert.NotNull(trace);

            Assert.Equal("4e3083444c10254ba40513c7316332eb", req.Context.Operation.Id);
            Assert.Equal("e2a5f830c0ee2c46", req.Context.Operation.ParentId);
            Assert.Equal("4e3083444c10254ba40513c7316332eb", trace.Context.Operation.Id);
            Assert.Equal(req.Id, trace.Context.Operation.ParentId);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "200",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: true);
        }

        [Fact]
        public async Task RequestFailedWithTraceParent()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Error";
            var url = client.BaseAddress + path;

            // Act
            Dictionary<string, string> requestHeaders = new Dictionary<string, string>()
                {
                    { "traceparent", "00-4e3083444c10254ba40513c7316332eb-e2a5f830c0ee2c46-00"}
                };
            var request = CreateRequestMessage(requestHeaders);
            request.RequestUri = new Uri(url);

            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            WaitForTelemetryToArrive();
            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            var exceptions = GetTelemetryOfType<ExceptionTelemetry>(items);
            Assert.Single(exceptions);
            var exception = exceptions[0];
            Assert.NotNull(req);
            Assert.NotNull(exception);

            Assert.Equal("4e3083444c10254ba40513c7316332eb", req.Context.Operation.Id);
            Assert.Equal("4e3083444c10254ba40513c7316332eb", exception.Context.Operation.Id);
            Assert.Equal("e2a5f830c0ee2c46", req.Context.Operation.ParentId);
            Assert.Equal(req.Id, exception.Context.Operation.ParentId);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "500",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: false);
        }

        [Fact]
        public async Task RequestSuccessWithW3CCompatibleRequestId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Index";
            var url = client.BaseAddress + path;

            // Act
            Dictionary<string, string> requestHeaders = new Dictionary<string, string>()
                {
                    { "Request-Id", "|40d1a5a08a68c0998e4a3b7c91915ca6.b9e41c35_1."}
                };
            var request = CreateRequestMessage(requestHeaders);
            request.RequestUri = new Uri(url);

            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            WaitForTelemetryToArrive();
            var items = _factory.sentItems;
            PrintItems(items);
            // 1 Trace from Ilogger, 1 Request
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            var traces = GetTelemetryOfType<TraceTelemetry>(items);
            Assert.Single(traces);
            var trace = traces[0];
            Assert.NotNull(req);
            Assert.NotNull(trace);

            Assert.Equal("40d1a5a08a68c0998e4a3b7c91915ca6", req.Context.Operation.Id);
            Assert.Equal("40d1a5a08a68c0998e4a3b7c91915ca6", trace.Context.Operation.Id);

            Assert.Equal("|40d1a5a08a68c0998e4a3b7c91915ca6.b9e41c35_1.", req.Context.Operation.ParentId);
            Assert.Equal(req.Id, trace.Context.Operation.ParentId);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "200",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: true);
        }

        [Fact]
        public async Task RequestFailedWithW3CCompatibleRequestId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Error";
            var url = client.BaseAddress + path;

            // Act
            Dictionary<string, string> requestHeaders = new Dictionary<string, string>()
                {
                    { "Request-Id", "|40d1a5a08a68c0998e4a3b7c91915ca6.b9e41c35_1."}
                };
            var request = CreateRequestMessage(requestHeaders);
            request.RequestUri = new Uri(url);

            var response = await client.SendAsync(request);

            // Assert
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            WaitForTelemetryToArrive();
            var items = _factory.sentItems;
            PrintItems(items);
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            var exceptions = GetTelemetryOfType<ExceptionTelemetry>(items);
            Assert.Single(exceptions);
            var exception = exceptions[0];
            Assert.NotNull(req);
            Assert.NotNull(exception);

            Assert.Equal("40d1a5a08a68c0998e4a3b7c91915ca6", req.Context.Operation.Id);
            Assert.Equal("40d1a5a08a68c0998e4a3b7c91915ca6", exception.Context.Operation.Id);

            Assert.Equal(req.Id, exception.Context.Operation.ParentId);
            Assert.Equal("|40d1a5a08a68c0998e4a3b7c91915ca6.b9e41c35_1.", req.Context.Operation.ParentId);
            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "500",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: false);
        }

        [Fact]
        public async Task RequestSuccessWithNonW3CCompatibleRequestId()
        {
            // Arrange
            var client = _factory.CreateClient();
            var path = "Home/Index";
            var url = client.BaseAddress + path;

            // Act
            Dictionary<string, string> requestHeaders = new Dictionary<string, string>()
                {
                    { "Request-Id", "|noncompatible.b9e41c35_1."}
                };
            var request = CreateRequestMessage(requestHeaders);
            request.RequestUri = new Uri(url);

            var response = await client.SendAsync(request);

            // Assert
            response.EnsureSuccessStatusCode();

            WaitForTelemetryToArrive();
            var items = _factory.sentItems;
            PrintItems(items);
            // 1 Trace from Ilogger, 1 Request
            Assert.Equal(2, items.Count);

            var reqs = GetTelemetryOfType<RequestTelemetry>(items);
            Assert.Single(reqs);
            var req = reqs[0];
            var traces = GetTelemetryOfType<TraceTelemetry>(items);
            Assert.Single(traces);
            var trace = traces[0];
            Assert.NotNull(req);
            Assert.NotNull(trace);

            Assert.NotEqual("noncompatible", req.Context.Operation.Id);
            Assert.NotEqual("noncompatible", trace.Context.Operation.Id);

            Assert.Equal("|noncompatible.b9e41c35_1.", req.Context.Operation.ParentId);
            Assert.Equal(req.Id, trace.Context.Operation.ParentId);
            Assert.Equal("noncompatible", req.Properties["ai_legacyRootId"]);

            ValidateRequest(
                 requestTelemetry: req,
                 expectedResponseCode: "200",
                 expectedName: "GET " + path,
                 expectedUrl: url,
                 expectedSuccess: true);
        }

        private void WaitForTelemetryToArrive()
        {
            // The response to the test server request is completed
            // before the actual telemetry is sent from HostingDiagnosticListener.
            // This could be a TestServer issue/feature. (In a real application, the response is not
            // sent to the user until TrackRequest() is called.)
            // The simplest workaround is to do a wait here.
            // This could be improved when entire functional tests are migrated to use this pattern.
            Task.Delay(1000).Wait();
        }

        private void ValidateRequest(RequestTelemetry requestTelemetry,
            string expectedResponseCode,
            string expectedName,
            string expectedUrl,
            bool expectedSuccess)
        {
            Assert.Equal(expectedResponseCode, requestTelemetry.ResponseCode);
            Assert.Equal(expectedName, requestTelemetry.Name);
            Assert.Equal(expectedSuccess, requestTelemetry.Success);
            Assert.Equal(expectedUrl, requestTelemetry.Url.ToString());
            Assert.True(requestTelemetry.Duration.TotalMilliseconds > 0);
            // requestTelemetry.Timestamp
        }

        private HttpRequestMessage CreateRequestMessage(Dictionary<string, string> requestHeaders = null)
        {
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage();
            httpRequestMessage.Method = HttpMethod.Get;
            if (requestHeaders != null)
            {
                foreach (var h in requestHeaders)
                {
                    httpRequestMessage.Headers.Add(h.Key, h.Value);
                }
            }

            return httpRequestMessage;
        }

        private List<T> GetTelemetryOfType<T>(ConcurrentBag<ITelemetry> items)
        {
            List<T> foundItems = new List<T>();
            foreach (var item in items)
            {
                if (item is T)
                {
                    foundItems.Add((T)item);
                }
            }

            return foundItems;
        }

        private void PrintItems(ConcurrentBag<ITelemetry> items)
        {
            int i = 1;
            foreach (var item in items)
            {
                this.output.WriteLine("Item " + (i++) + ".");

                if (item is RequestTelemetry req)
                {
                    this.output.WriteLine("RequestTelemetry");
                    this.output.WriteLine(req.Name);
                    this.output.WriteLine(req.Duration.ToString());
                }
                else if (item is DependencyTelemetry dep)
                {
                    this.output.WriteLine("DependencyTelemetry");
                    this.output.WriteLine(dep.Name);
                }
                else if (item is TraceTelemetry trace)
                {
                    this.output.WriteLine("TraceTelemetry");
                    this.output.WriteLine(trace.Message);
                }
                else if (item is ExceptionTelemetry exc)
                {
                    this.output.WriteLine("ExceptionTelemetry");
                    this.output.WriteLine(exc.Message);
                }
                else if (item is MetricTelemetry met)
                {
                    this.output.WriteLine("MetricTelemetry");
                    this.output.WriteLine(met.Name + "" + met.Sum);
                }

                PrintProperties(item as ISupportProperties);
                this.output.WriteLine("----------------------------");
            }
        }

        private void PrintProperties(ISupportProperties itemProps)
        {
            foreach (var prop in itemProps.Properties)
            {
                this.output.WriteLine(prop.Key + ":" + prop.Value);
            }
        }

    }
}