using System;
using System.Diagnostics.Contracts;
using System.ServiceModel.Channels;
using System.Web.Http;
using Microsoft.ServiceBus;

namespace Tjaskula.AzureServiceBusHost
{
    public class AzureServiceBusConfiguration : HttpConfiguration
    {
        private int _maxConcurrentRequests = 10;
        private string _issuerSecret;
        private string _issuerName;
        private const int MinConcurrentRequests = 1;
        private static Uri _baseAddress = ServiceBusEnvironment.CreateServiceUri(ServiceConfiguration.ServiceScheme, ServiceConfiguration.ServiceNamespace, ServiceConfiguration.ServicePath);

        public AzureServiceBusConfiguration(string baseAddress) : base(new HttpRouteCollection(baseAddress))
        {
            if (baseAddress == null) throw new ArgumentNullException("baseAddress");

            _baseAddress = new Uri(baseAddress);
        }

        public AzureServiceBusConfiguration() : base(new HttpRouteCollection(_baseAddress.ToString()))
        {
        }

        public string IssuerName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_issuerName))
                    _issuerName = ServiceConfiguration.ServiceOwner;
                return _issuerName;
            }
            set { _issuerName = value; }
        }

        public string IssuerSecret
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_issuerSecret))
                    _issuerSecret = ServiceConfiguration.ServiceKey;
                return _issuerSecret;
            }
            set { _issuerSecret = value; }
        }

        public Uri BaseAddress 
        { 
            get
                {
                    return _baseAddress;
                } 
        }

        /// <summary>
        /// Gets or sets the upper limit of how many concurrent <see cref="T:System.Net.Http.HttpRequestMessage"/> instances 
        /// can be processed at any given time. The default is 100 times the number of CPU cores.
        /// </summary>
        /// <value>
        /// The maximum concurrent <see cref="T:System.Net.Http.HttpRequestMessage"/> instances processed at any given time.
        /// </value>
        public int MaxConcurrentRequests
        {
            get { return _maxConcurrentRequests; }

            set
            {
                if (value < MinConcurrentRequests)
                {
                    throw new ArgumentOutOfRangeException("value", string.Format("Value must be greater than or equal to {0}.", MinConcurrentRequests));
                }
                _maxConcurrentRequests = value;
            }
        }

        /// <summary>
        /// Internal method called to configure <see cref="Binding"/> settings.
        /// </summary>
        /// <param name="webHttpRelayBinding">Azure Web Http relay binding.</param>
        /// <returns>The <see cref="BindingParameterCollection"/> to use when building the <see cref="IChannelListener"/> or null if no binding parameters are present.</returns>
        internal BindingParameterCollection ConfigureBinding(Binding webHttpRelayBinding)
        {
            return OnConfigureBinding(webHttpRelayBinding);
        }

        /// <summary>
        /// Called to apply the configuration on the endpoint level.
        /// </summary>
        /// <param name="webHttpRelayBinding">Azure Http endpoint.</param>
        /// <returns>The <see cref="BindingParameterCollection"/> to use when building the <see cref="IChannelListener"/> or null if no binding parameters are present.</returns>
        protected virtual BindingParameterCollection OnConfigureBinding(Binding webHttpRelayBinding)
        {
            if (webHttpRelayBinding == null)
            {
                throw new ArgumentNullException("webHttpRelayBinding");
            }

            var bindingParameters = new BindingParameterCollection();
            bindingParameters.Add(new TransportClientEndpointBehavior(TokenProvider.CreateSharedSecretTokenProvider(IssuerName, IssuerSecret)));

            return bindingParameters;
        }

        internal static int MultiplyByProcessorCount(int value)
        {
            Contract.Assert(value > 0);
            try
            {
                return Math.Max(Environment.ProcessorCount * value, value);
            }
            catch
            {
                return value;
            }
        }
    }
}