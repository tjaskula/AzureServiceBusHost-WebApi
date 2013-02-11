﻿using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Web.Http;
using Tjaskula.AzureServiceBusHost;

namespace AzureServiceBusHost.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new AzureServiceBusConfiguration();
            config.Routes.MapHttpRoute("default", "{controller}/{id}", new { id = RouteParameter.Optional });
//			config.Formatters.Remove(config.Formatters.JsonFormatter);
//			config.Formatters.Remove(config.Formatters.XmlFormatter);
	        //config.Formatters.Insert(0, new CustomFormatter());
            var host = new AzureServiceBusHttpServer(config);
            host.OpenAsync().Wait();
            System.Console.WriteLine("Server is opened at '{0}'", config.BaseAddress);
            System.Console.ReadKey();
            host.CloseAsync();
        }
    }

	internal class CustomFormatter : MediaTypeFormatter
	{
		public CustomFormatter()
		{
			SupportedMediaTypes.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			SupportedMediaTypes.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
		}
		public override bool CanReadType(System.Type type)
		{
			return true;
		}

		public override bool CanWriteType(System.Type type)
		{
			return true;
		}

		public override MediaTypeFormatter GetPerRequestFormatterInstance(System.Type type, System.Net.Http.HttpRequestMessage request, MediaTypeHeaderValue mediaType)
		{
			return base.GetPerRequestFormatterInstance(type, request, mediaType);
		}

		public override void SetDefaultContentHeaders(System.Type type, HttpContentHeaders headers, MediaTypeHeaderValue mediaType)
		{
			base.SetDefaultContentHeaders(type, headers, mediaType);
		}

		public override System.Threading.Tasks.Task<object> ReadFromStreamAsync(System.Type type, System.IO.Stream readStream, System.Net.Http.HttpContent content, IFormatterLogger formatterLogger)
		{
			return base.ReadFromStreamAsync(type, readStream, content, formatterLogger);
		}

		public override System.Threading.Tasks.Task WriteToStreamAsync(System.Type type, object value, System.IO.Stream writeStream, System.Net.Http.HttpContent content, System.Net.TransportContext transportContext)
		{
			return base.WriteToStreamAsync(type, value, writeStream, content, transportContext);
		}
	}
}