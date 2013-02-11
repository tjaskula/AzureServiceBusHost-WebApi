using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace AzureServiceBusHost.Console
{
    public class TestController : ApiController
    {
        public string Get(string message)
        {
            return "Vous avez dit : " + message;
        }

        public HttpResponseMessage Post()
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}