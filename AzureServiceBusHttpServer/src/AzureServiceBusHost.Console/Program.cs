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
            var host = new AzureServiceBusHttpServer(config);
            host.OpenAsync().Wait();
            System.Console.WriteLine("Server is opened at '{0}'", config.BaseAddress);
            System.Console.ReadKey();
            host.CloseAsync();
        }
    }
}