using System.IO;
using System.Runtime.Serialization.Json;
using System.ServiceModel.Channels;
using System.Xml;

namespace Tjaskula.AzureServiceBusHost
{
	class JsonStreamBodyWriter : BodyWriter
	{
		readonly Stream _jsonStream;
		public JsonStreamBodyWriter(Stream jsonStream)
			: base(false)
		{
			_jsonStream = jsonStream;
		}

		protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
		{
			writer.WriteNode(JsonReaderWriterFactory.CreateJsonReader(_jsonStream, XmlDictionaryReaderQuotas.Max), false);
			writer.Flush();
		}
	}

}
