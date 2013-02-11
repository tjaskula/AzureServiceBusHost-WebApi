using System.ServiceModel.Channels;

namespace Tjaskula.AzureServiceBusHost
{
	internal class RawContentTypeMapper : WebContentTypeMapper
	{
		public override WebContentFormat GetMessageFormatForContentType(string contentType)
		{
			return WebContentFormat.Raw;
		}
	}
}