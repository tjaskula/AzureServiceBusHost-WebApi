using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Xunit;
using Xunit.Extensions;

namespace Tjaskula.AzureServiceBusHost.Tests
{
    public class AzureServiceBusHttpServerTests : IDisposable
    {
        private AzureServiceBusHttpServer _server = null;

        public AzureServiceBusHttpServerTests()
        {
            _server = CreateServer();
        }

        public void Dispose()
        {
            if (_server != null)
            {
                _server.CloseAsync().Wait();
            }
        }

        [Theory]
        [InlineData("/SelfHostServerTest/EchoString")]
        public void SendAsync_Direct_Returns_OK_For_Successful_ObjectContent_Write(string uri)
        {
            // Arrange & Act
            HttpResponseMessage response =
                new HttpClient(_server).GetAsync(((AzureServiceBusConfiguration) _server.Configuration).BaseAddress + uri).Result;
            string responseString = response.Content.ReadAsStringAsync().Result;

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("\"echoString\"", responseString);
        }

        [Theory]
        [InlineData("/SelfHostServerTest/EchoStream")]
        public void SendAsync_ServiceModel_Returns_OK_For_Successful_Stream_Write(string uri)
        {
            // Arrange & Act
            HttpResponseMessage response =
                new HttpClient(_server).GetAsync(((AzureServiceBusConfiguration) _server.Configuration).BaseAddress + uri).Result;
            string responseString = response.Content.ReadAsStringAsync().Result;

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("echoStream", responseString);
        }

        [Theory]
        [InlineData("/SelfHostServerTest/ThrowBeforeTask")]
        [InlineData("/SelfHostServerTest/ThrowBeforeWrite")]
        [InlineData("/SelfHostServerTest/ThrowAfterWrite")]
        public void SendAsync_Direct_Throws_When_ObjectContent_CopyToAsync_Throws(string uri)
        {
            // Arrange & Act & Assert
            Assert.Throws<InvalidOperationException>(
                () =>
                    {
                        try
                        {
                            new HttpClient(_server).GetAsync(
                                ((AzureServiceBusConfiguration) _server.Configuration).BaseAddress + uri).Wait();
                        }
                        catch(Exception e)
                        {
                            throw e.InnerException;
                        }
                    });
        }

        [Theory]
        [InlineData("/SelfHostServerTest/ThrowBeforeTask")]
        [InlineData("/SelfHostServerTest/ThrowBeforeWrite")]
        [InlineData("/SelfHostServerTest/ThrowAfterWrite")]
        public void SendAsync_ServiceModel_Closes_Connection_When_ObjectContent_CopyToAsync_Throws(string uri)
        {
            // Arrange
            Task<HttpResponseMessage> task = new HttpClient(_server).GetAsync(((AzureServiceBusConfiguration)_server.Configuration).BaseAddress + uri);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                                                    {
                                                        try
                                                        {
                                                            task.Wait();
                                                        }
                                                        catch (Exception e)
                                                        {
                                                            throw e.InnerException;
                                                        }
                                                    });
        }

        [Theory]
        [InlineData("/SelfHostServerTest/ThrowBeforeWriteStream")]
        [InlineData("/SelfHostServerTest/ThrowAfterWriteStream")]
        public void SendAsync_Direct_Throws_When_StreamContent_Throws(string uri)
        {
            // Arrange & Act & Assert
            Assert.Throws<InvalidOperationException>(
                () =>
                    {
                        try
                        {
                            new HttpClient(_server).GetAsync(
                                ((AzureServiceBusConfiguration) _server.Configuration).BaseAddress + uri).Wait();
                        }
                        catch (Exception e)
                        {
                            throw e.InnerException;
                        }

                    });
        }

        [Theory]
        [InlineData("/SelfHostServerTest/ThrowBeforeWriteStream")]
        [InlineData("/SelfHostServerTest/ThrowAfterWriteStream")]
        public void SendAsync_ServiceModel_Throws_When_StreamContent_Throws(string uri)
        {
            // Arrange
            Task task = new HttpClient(_server).GetAsync(((AzureServiceBusConfiguration)_server.Configuration).BaseAddress + uri);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                                                    {
                                                        try
                                                        {
                                                            task.Wait();
                                                        }
                                                        catch (Exception e)
                                                        {
                                                            throw e.InnerException;
                                                        }
                                                    });
        }

        internal class ThrowsBeforeTaskObjectContent : ObjectContent
        {
            public ThrowsBeforeTaskObjectContent()
                : base(typeof(string), "testContent", new JsonMediaTypeFormatter())
            {
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                throw new InvalidOperationException("ThrowBeforeTask");
            }
        }

        internal class ThrowBeforeWriteObjectContent : ObjectContent
        {
            public ThrowBeforeWriteObjectContent()
                : base(typeof(string), "testContent", new JsonMediaTypeFormatter())
            {
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return Task.Factory.StartNew(() =>
                {
                    throw new InvalidOperationException("ThrowBeforeWrite");
                });

            }
        }

        internal class ThrowAfterWriteObjectContent : ObjectContent
        {
            public ThrowAfterWriteObjectContent()
                : base(typeof(string), "testContent", new JsonMediaTypeFormatter())
            {
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return Task.Factory.StartNew(() =>
                {
                    byte[] buffer =
                        Encoding.UTF8.GetBytes("ThrowAfterWrite");
                    stream.Write(buffer, 0, buffer.Length);
                    throw new InvalidOperationException("ThrowAfterWrite");
                });
            }
        }

        internal class ThrowBeforeWriteStream : StreamContent
        {
            public ThrowBeforeWriteStream()
                : base(new MemoryStream(Encoding.UTF8.GetBytes("ThrowBeforeWriteStream")))
            {
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                throw new InvalidOperationException("ThrowBeforeWriteStream");
            }
        }

        internal class ThrowAfterWriteStream : StreamContent
        {
            public ThrowAfterWriteStream()
                : base(new MemoryStream(Encoding.UTF8.GetBytes("ThrowAfterWriteStream")))
            {
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                base.SerializeToStreamAsync(stream, context).Wait();
                throw new InvalidOperationException("ThrowAfterWriteStream");
            }
        }

        private static AzureServiceBusHttpServer CreateServer()
        {
            var config = new AzureServiceBusConfiguration();
            config.Routes.MapHttpRoute("Default", "{controller}/{action}");

            var server = new AzureServiceBusHttpServer(config);
            server.OpenAsync().Wait();
            return server;
        }
    }

    public class SelfHostServerTestController : ApiController
    {
        [HttpGet]
        public string EchoString()
        {
            return "echoString";
        }

        [HttpGet]
        public HttpResponseMessage EchoStream()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("echoStream")))
            };
        }

        [HttpGet]
        public HttpResponseMessage ThrowBeforeTask()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new AzureServiceBusHttpServerTests.ThrowsBeforeTaskObjectContent()
            };
        }

        [HttpGet]
        public HttpResponseMessage ThrowBeforeWrite()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new AzureServiceBusHttpServerTests.ThrowBeforeWriteObjectContent()
            };
        }

        [HttpGet]
        public HttpResponseMessage ThrowAfterWrite()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new AzureServiceBusHttpServerTests.ThrowAfterWriteObjectContent()
            };
        }

        [HttpGet]
        public HttpResponseMessage ThrowBeforeWriteStream()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new AzureServiceBusHttpServerTests.ThrowBeforeWriteStream()
            };
        }

        [HttpGet]
        public HttpResponseMessage ThrowAfterWriteStream()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new AzureServiceBusHttpServerTests.ThrowAfterWriteStream()
            };
        }
    }
}