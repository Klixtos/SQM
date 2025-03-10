using System;
using Microsoft.Extensions.Logging;
using Moq;
using SmartQueueMiddleware.Services;
using Xunit;

namespace SmartQueueMiddleware.Tests
{
    public class CpuUsageProviderTests
    {
        [Fact]
        public void GetCpuUsage_ReturnsValueBetween0And100()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<CrossPlatformCpuUsageProvider>>();
            var provider = new CrossPlatformCpuUsageProvider(loggerMock.Object);

            // Act
            var result = provider.GetCpuUsage();

            // Assert
            Assert.InRange(result, 0, 100);

            // Cleanup
            provider.Dispose();
        }

        [Fact]
        public void Constructor_DoesNotThrowException()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<CrossPlatformCpuUsageProvider>>();

            // Act & Assert
            var exception = Record.Exception(() => new CrossPlatformCpuUsageProvider(loggerMock.Object));
            Assert.Null(exception);

            // Cleanup
            using var provider = new CrossPlatformCpuUsageProvider(loggerMock.Object);
        }
    }
}