using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace AzureServiceBusHost.Console
{
    public class TestController : ApiController
    {
        public string Get(string id)
        {
            return "Message via the service bus host : " + id;
        }

        public HttpResponseMessage Post()
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}