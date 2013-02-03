using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.ServiceModel.Channels;

namespace Tjaskula.AzureServiceBusHost
{
    /// <summary>
    /// Provides extension methods for getting an <see cref="HttpRequestMessage"/> instance or
    /// an <see cref="HttpResponseMessage"/> instance from a <see cref="Message"/> instance and
    /// provides extension methods for creating a <see cref="Message"/> instance from either an 
    /// <see cref="HttpRequestMessage"/> instance or an 
    /// <see cref="HttpResponseMessage"/> instance.
    /// </summary>
    internal static class HttpMessageExtensions
    {
        internal const string ToHttpRequestMessageMethodName = "ToHttpRequestMessage";
        internal const string ToHttpResponseMessageMethodName = "ToHttpResponseMessage";
        internal const string ToMessageMethodName = "ToMessage";

        /// <summary>
        /// Returns a reference to the <see cref="HttpRequestMessage"/> 
        /// instance held by the given <see cref="Message"/> or null if the <see cref="Message"/> does not
        /// hold a reference to an <see cref="HttpRequestMessage"/> 
        /// instance.
        /// </summary>
        /// <param name="message">The given <see cref="Message"/> that holds a reference to an 
        /// <see cref="HttpRequestMessage"/> instance.
        /// </param>
        /// <returns>
        /// A reference to the <see cref="HttpRequestMessage"/> 
        /// instance held by the given <see cref="Message"/> or null if the <see cref="Message"/> does not
        /// hold a reference to an <see cref="HttpRequestMessage"/> 
        /// instance.
        /// </returns>
        public static HttpRequestMessage ToHttpRequestMessage(this Message message)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            HttpRequestMessageProperty requestProperty;
            if (!message.Properties.TryGetValue(HttpRequestMessageProperty.Name, out requestProperty))
            {
                throw new InvalidOperationException(string.Format("The incoming message does not have the required '{0}' property of type '{1}'.", HttpRequestMessageProperty.Name, typeof(HttpRequestMessageProperty).Name));
            }

            Uri uri = message.Headers.To;
            if (uri == null)
            {
                throw new InvalidOperationException("The incoming message does not have the required 'To' header.");
            }

            var nreq = new HttpRequestMessage(new HttpMethod(message.Headers.Action), message.Headers.To.AbsoluteUri);

            // Copy message properties to HttpRequestMessage. While it does have the
            // risk of allowing properties to get out of sync they in virtually all cases are
            // read-only so the risk is low. The downside to not doing it is that it isn't
            // possible to access anything from HttpRequestMessage (or OperationContent.Current)
            // which is worse.
            foreach (KeyValuePair<string, object> kv in message.Properties)
            {
                nreq.Properties.Add(kv.Key, kv.Value);
            }

            if (nreq.Content == null)
            {
                nreq.Content = new ByteArrayContent(new byte[0]);
            }
            else
            {
                nreq.Content.Headers.Clear();
            }

            message.Headers.To = uri;

            nreq.RequestUri = uri;
            nreq.Method = GetHttpMethod(requestProperty.Method);

            WebHeaderCollection headers = requestProperty.Headers;
            foreach (var headerName in headers.AllKeys)
            {
                string headerValue = headers[headerName];
                if (!nreq.Headers.TryAddWithoutValidation(headerName, headerValue))
                {
                    nreq.Content.Headers.TryAddWithoutValidation(headerName, headerValue);
                }
            }

            return nreq;
        }

        /// <summary>
        /// Creates a new <see cref="Message"/> that holds a reference to the given
        /// <see cref="HttpResponseMessage"/> instance.
        /// </summary>
        /// <param name="response">The <see cref="HttpResponseMessage"/> 
        /// instance to which the new <see cref="Message"/> should hold a reference.
        /// </param>
        /// <returns>A <see cref="Message"/> that holds a reference to the given
        /// <see cref="HttpResponseMessage"/> instance.
        /// </returns>
        public static Message ToMessage(this HttpResponseMessage response)
        {
            if (response == null)
            {
                throw new ArgumentNullException("response");
            }

            var message = Message.CreateMessage(MessageVersion.None, response.RequestMessage.Method.Method, response.Content.ReadAsAsync<object>().Result);
            HttpResponseMessageProperty responseProperty = new HttpResponseMessageProperty();
            CopyHeadersToNameValueCollection(response.Headers, responseProperty.Headers);
            HttpContent content = response.Content;
            if (content != null)
            {
                CopyHeadersToNameValueCollection(response.Content.Headers, responseProperty.Headers);
            }
            else
            {
                responseProperty.SuppressEntityBody = true;
            }

            message.Properties.Clear();
            message.Headers.Clear();

            message.Properties.Add(HttpResponseMessageProperty.Name, responseProperty);
            
            return message;
        }

        /// <summary>
        /// Gets the static <see cref="HttpMethod"/> instance for any given HTTP method name.
        /// </summary>
        /// <param name="method">The HTTP request method.</param>
        /// <returns>An existing static <see cref="HttpMethod"/> or a new instance if the method was not found.</returns>
        internal static HttpMethod GetHttpMethod(string method)
        {
            if (String.IsNullOrEmpty(method))
            {
                return null;
            }

            if (String.Equals("GET", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Get;
            }

            if (String.Equals("POST", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Post;
            }

            if (String.Equals("PUT", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Put;
            }

            if (String.Equals("DELETE", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Delete;
            }

            if (String.Equals("HEAD", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Head;
            }

            if (String.Equals("OPTIONS", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Options;
            }

            if (String.Equals("TRACE", method, StringComparison.OrdinalIgnoreCase))
            {
                return HttpMethod.Trace;
            }

            return new HttpMethod(method);
        }

        private static void CopyHeadersToNameValueCollection(HttpHeaders headers, NameValueCollection nameValueCollection)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                foreach (string value in header.Value)
                {
                    nameValueCollection.Add(header.Key, value);
                }
            }
        }
    }
}