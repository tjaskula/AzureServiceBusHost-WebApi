using System.Web.Http;
using Tjaskula.AzureServiceBusHost;

namespace AzureServiceBusHost.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new AzureServiceBusConfiguration();
            config.Routes.MapHttpRoute("default", "{controller}/{message}", new { id = RouteParameter.Optional });
			config.MessageHandlers.Add(new LoggingActivityHandler());
			config.Formatters.Insert(0, new CustomHtmlFormatter());
	        //config.Formatters.Insert(0, new CustomFormatter());
            var host = new AzureServiceBusHttpServer(config);
            host.OpenAsync().Wait();
            System.Console.WriteLine("Server is opened at '{0}'", config.BaseAddress);
            System.Console.ReadKey();
            host.CloseAsync();
        }
    }
}