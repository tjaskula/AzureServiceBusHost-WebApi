using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;

namespace Tjaskula.AzureServiceBusHost
{
	public class CustomHtmlFormatter : BufferedMediaTypeFormatter
	{
		public CustomHtmlFormatter()
		{
			SupportedMediaTypes.Add(new MediaTypeWithQualityHeaderValue("text/html"));	
		}

		public override bool CanReadType(Type type)
		{
			return false;
		}

		public override bool CanWriteType(Type type)
		{
			if (type == typeof(string))
				return true;
			return false;
		}

		public override void WriteToStream(Type type, object value, Stream writeStream, HttpContent content)
		{
			string html = "<html><span>";
			html += value;
			html += "</span></html>";

			using (var writer = new StreamWriter(writeStream))
			{
				writer.Write(html);
				writer.Flush();
			}
		}
	}
}