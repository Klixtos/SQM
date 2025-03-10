using System;
using Microsoft.Extensions.Logging;
using Moq;
using SmartQueueMiddleware.Services;
using Xunit;

namespace SmartQueueMiddleware.Tests
{
    public class MemoryMonitorTests
    {
        [Fact]
        public void GetMemoryUsage_ReturnsValueBetween0And100()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<CrossPlatformMemoryMonitor>>();
            var monitor = new CrossPlatformMemoryMonitor(loggerMock.Object);

            // Act
            var result = monitor.GetMemoryUsage();

            // Assert
            Assert.InRange(result, 0, 100);

            // Cleanup
            monitor.Dispose();
        }

        [Fact]
        public void GetDetailedMemoryMetrics_ReturnsNonNullMetrics()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<CrossPlatformMemoryMonitor>>();
            var monitor = new CrossPlatformMemoryMonitor(loggerMock.Object);

            // Act
            var result = monitor.GetDetailedMemoryMetrics();

            // Assert
            Assert.NotNull(result);
            Assert.InRange(result.MemoryUsagePercentage, 0, 100);
            Assert.True(result.TotalMemoryMB > 0);

            // Cleanup
            monitor.Dispose();
        }
    }
}