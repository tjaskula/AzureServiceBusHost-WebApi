using System;
using System.Net.Http;
using System.ServiceModel.Channels;
using System.Threading;
using System.Threading.Tasks;

namespace Tjaskula.AzureServiceBusHost
{
	public class LoggingActivityHandler : DelegatingHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return base.SendAsync(request, cancellationToken).ContinueWith(t =>
				                                                               {
					                                                               var response = t.Result;

					                                                               var message = response.Content.ReadAsStringAsync().Result.Replace("<html><span>", string.Empty).Replace("</span></html>", string.Empty);

																				   Console.WriteLine(message);

					                                                               return response;
				                                                               });
		}
	}
}
