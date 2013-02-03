using System;
using Microsoft.ServiceBus;
using Xunit;
using Xunit.Extensions;

namespace Tjaskula.AzureServiceBusHost.Tests
{
    public class AzureServiceBusConfigurationTests
    {
        [Fact]
        public void AzureServiceBusConfiguration_NullBaseAddressString_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AzureServiceBusConfiguration(null));
        }

        [Fact]
        public void AzureServiceBusConfiguration_EmptyBaseAddressString_Throws()
        {
            Assert.Throws<UriFormatException>(() => new AzureServiceBusConfiguration(string.Empty));
        }

        [Fact]
        public void AzureServiceBusConfiguration_InvalidBaseAddressString_Throws()
        {
            Assert.Throws<UriFormatException>(() => new AzureServiceBusConfiguration("invalid"));
        }

        [Fact]
        public void AzureServiceBusConfiguration_BaseAddress_IsSet()
        {
            // Arrange
            string baseAddress = "http://localhost";

            // Act
            var config = new AzureServiceBusConfiguration(baseAddress);

            // Assert
            Assert.Equal(new Uri(baseAddress), config.BaseAddress);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void MaxConcurrentRequests_LessThenMinConcurrent_Throws(int maxConcurrentRequest)
        {
            // Arrange
            // Act
            var config = new AzureServiceBusConfiguration();

            // Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => config.MaxConcurrentRequests = maxConcurrentRequest);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(200)]
        public void MaxConcurrentRequests_GreaterThenMinConcurrent_IsSet(int maxConcurrentRequest)
        {
            // Arrange
            // Act
            var config = new AzureServiceBusConfiguration();
            config.MaxConcurrentRequests = maxConcurrentRequest;

            // Assert
            Assert.Equal(maxConcurrentRequest, config.MaxConcurrentRequests);
        }

        [Fact]
        public void ConfigureBinding_BindingIsNull_Throws()
        {
            // Arrange
            // Act
            var config = new AzureServiceBusConfiguration();

            // Assert
            Assert.Throws<ArgumentNullException>(() => config.ConfigureBinding(null));
        }

        [Fact]
        public void ConfigureBinding_BindingPassed_ReturnsBindingsParameters()
        {
            // Arrange
            var config = new AzureServiceBusConfiguration();

            var binding = new WebHttpRelayBinding();

            // Act
            var bindingsParameters = config.ConfigureBinding(binding);
            var bindingParameter = bindingsParameters.Find<TransportClientEndpointBehavior>();

            // Assert
            Assert.NotNull(bindingParameter);
        }
    }
}