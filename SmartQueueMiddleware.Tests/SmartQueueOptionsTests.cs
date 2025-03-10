using Xunit;
using SmartQueueMiddleware;

namespace SmartQueueMiddleware.Tests
{
    public class SmartQueueOptionsTests
    {
        [Fact]
        public void DefaultOptions_HaveCorrectValues()
        {
            // Arrange & Act
            var options = new SmartQueueOptions();

            // Assert
            Assert.Equal(80, options.CpuThreshold);
            Assert.Equal(90, options.MemoryThreshold);
            Assert.Equal(100, options.MaxQueueSize);
            Assert.Equal(30, options.MaxWaitTimeSeconds);
            Assert.Equal(100, options.MaxConcurrentRequests);
            Assert.Equal(503, options.RejectionStatusCode);
            Assert.Equal("Server is under high load. Please try again later.", options.RejectionMessage);
            Assert.True(options.UseMemoryMonitoring);
            Assert.True(options.EnableLogs);
        }

        [Fact]
        public void CustomOptions_AreSetCorrectly()
        {
            // Arrange & Act
            var options = new SmartQueueOptions
            {
                CpuThreshold = 85,
                MemoryThreshold = 95,
                MaxQueueSize = 200,
                MaxWaitTimeSeconds = 60,
                MaxConcurrentRequests = 50,
                RejectionStatusCode = 429,
                RejectionMessage = "Custom message",
                UseMemoryMonitoring = false,
                EnableLogs = false
            };

            // Assert
            Assert.Equal(85, options.CpuThreshold);
            Assert.Equal(95, options.MemoryThreshold);
            Assert.Equal(200, options.MaxQueueSize);
            Assert.Equal(60, options.MaxWaitTimeSeconds);
            Assert.Equal(50, options.MaxConcurrentRequests);
            Assert.Equal(429, options.RejectionStatusCode);
            Assert.Equal("Custom message", options.RejectionMessage);
            Assert.False(options.UseMemoryMonitoring);
            Assert.False(options.EnableLogs);
        }
    }
}