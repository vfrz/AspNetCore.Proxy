﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AspNetCore.Proxy.Tests
{
    public class HttpIntegrationTests
    {
        private readonly TestServer _server;
        private readonly HttpClient _client;

        public HttpIntegrationTests()
        {
            _server = new TestServer(new WebHostBuilder().UseStartup<Startup>());
            _client = _server.CreateClient();
        }

        [Fact]
        public async Task CanProxyControllerPostRequest()
        {
            var content = new StringContent("{\"title\": \"foo\", \"body\": \"bar\", \"userId\": 1}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("api/posts", content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("101", JObject.Parse(responseString).Value<string>("id"));
        }

        [Fact]
        public async Task CanProxyControllerContentHeadersPostRequest()
        {
            var content = "hello world";
            var contentType = "application/xcustom";

            var stringContent = new StringContent(content);
            stringContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var response = await _client.PostAsync("echo/post", stringContent);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Equal(content, JObject.Parse(responseString).Value<string>("data"));
            Assert.Equal(contentType, JObject.Parse(responseString)["headers"]["content-type"]);
            Assert.Equal(content.Length, JObject.Parse(responseString)["headers"]["content-length"]);
        }

        [Fact]
        public async Task CanProxyControllerPostWithFormRequest()
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "xyz", "123" }, { "abc", "321" } });
            var response = await _client.PostAsync("api/posts", content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseString);

            Assert.Contains("101", json.Value<string>("id"));
            Assert.Equal("123", json["xyz"]);
            Assert.Equal("321", json["abc"]);
        }

        [Fact]
        public async Task CanProxyControllerPostWithFormAndFilesRequest()
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("123"), "xyz");
            content.Add(new StringContent("456"), "xyz");
            content.Add(new StringContent("321"), "abc");
            const string fileName = "Test こんにちは file.txt";
            const string fileString = "This is a test file こんにちは with non-ascii content.";
            var fileContent = new StreamContent(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(fileString)));
            content.Add(fileContent, "testFile", fileName);

            var response = await _client.PostAsync("api/multipart", content);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseString);

            var form = Assert.IsAssignableFrom<JObject>(json["form"]);
            Assert.Equal(2, form.Count);
            var xyz = Assert.IsAssignableFrom<JArray>(form["xyz"]);
            Assert.Equal(2, xyz.Count);
            Assert.Equal("123", xyz[0]);
            Assert.Equal("456", xyz[1]);
            Assert.Equal("321", form["abc"]);

            var files = Assert.IsAssignableFrom<JObject>(json["files"]);
            Assert.Single(files);
            var file = files.ToObject<Dictionary<string, string>>().Single();
            Assert.Equal($"data:application/octet-stream;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(fileString))}", file.Value);
            Assert.Equal(fileName, file.Key);
        }

        [Fact]
        public async Task CanProxyControllerCatchAllPostWithFormRequest()
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "xyz", "123" }, { "abc", "321" } });
            var response = await _client.PostAsync("api/catchall/posts", content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseString);

            Assert.Contains("101", json.Value<string>("id"));
            Assert.Equal("123", json["xyz"]);
            Assert.Equal("321", json["abc"]);
        }

        [Fact]
        public async Task CanProxyControllerCatchAll()
        {
            var response = await _client.GetAsync("api/catchall/posts/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("sunt aut facere repellat provident occaecati excepturi optio reprehenderit", JObject.Parse(responseString).Value<string>("title"));
        }

        [Fact]
        public async Task CanProxyMiddlewareWithContextAndArgsToTask()
        {
            var response = await _client.GetAsync("api/comments/contextandargstotask/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("id labore ex et quam laborum", JObject.Parse(responseString).Value<string>("name"));
        }

        [Fact]
        public async Task CanProxyMiddlewareWithContextAndArgsToString()
        {
            var response = await _client.GetAsync("api/comments/contextandargstostring/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("id labore ex et quam laborum", JObject.Parse(responseString).Value<string>("name"));
        }

        [Fact]
        public async Task CanProxyWithController()
        {
            var response = await _client.GetAsync("api/controller/posts/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("sunt aut facere repellat provident occaecati excepturi optio reprehenderit", JObject.Parse(responseString).Value<string>("title"));
        }

        [Fact]
        public async Task CanModifyRequest()
        {
            var response = await _client.GetAsync("api/controller/customrequest/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("qui est esse", JObject.Parse(responseString).Value<string>("title"));
        }

        [Fact]
        public async Task CanModifyResponse()
        {
            var response = await _client.GetAsync("api/controller/customresponse/1");
            response.EnsureSuccessStatusCode();

            Assert.Equal("It's all greek...er, Latin...to me!", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CanModifyBadResponse()
        {
            var response = await _client.GetAsync("api/controller/badresponse/1");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            Assert.Equal("I tried to proxy, but I chose a bad address, and it is not found.", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CanGetGeneric502OnFailure()
        {
            var response = await _client.GetAsync("api/controller/fail/1");
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [Fact]
        public async Task CanGetCustomFailure()
        {
            var response = await _client.GetAsync("api/controller/customfail/1");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Things borked.", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CanGetIntercept()
        {
            var response = await _client.GetAsync("api/controller/intercept/1");
            response.EnsureSuccessStatusCode();
            Assert.Equal("This was intercepted and not proxied!", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task CanProxyReasonPhase()
        {
            var response = await _client.GetAsync("api/controller/customreasonphase/proxy");

            response.EnsureSuccessStatusCode();
            Assert.Equal("I am dummy!", response.ReasonPhrase);
        }

        [Fact]
        public async Task CanProxyConcurrentCalls()
        {
            var calls = Enumerable.Range(1, 100).Select(i => _client.GetAsync($"api/controller/posts/{i}"));

            Assert.True((await Task.WhenAll(calls)).All(r => r.IsSuccessStatusCode));
        }

        [Fact]
        public async Task CanProvideCustomClient()
        {
            var response = await _client.GetAsync("api/controller/timeoutclient/1");
            // Expects failure because the HTTP client is set to low timeout.
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [Fact]
        public async Task CanProvideBaseAddressClient()
        {
            var response = await _client.GetAsync("api/controller/baseaddressclient/1");
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("sunt aut facere repellat provident occaecati excepturi optio reprehenderit", JObject.Parse(responseString).Value<string>("title"));
        }
    }
}
