using System.IO;
using System.ServiceModel.Channels;
using System.Xml;
using Microsoft.ServiceBus.Web;

namespace Tjaskula.AzureServiceBusHost
{
	class XmlBodyWriter : BodyWriter
	{
		private readonly string _content;

		public XmlBodyWriter(string content, bool isBuffered) : base(isBuffered)
		{
			_content = content;
		}

		protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
		{
			writer.WriteRaw(_content);
		}
	}

}
