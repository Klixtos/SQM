using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartQueueMiddleware.Services;
using Xunit;

namespace SmartQueueMiddleware.Tests
{
    public class SmartQueueMiddlewareExtensionsTests
    {
        [Fact]
        public void AddSmartQueue_RegistersServices()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Add required services for DI
            services.AddSingleton(new Mock<ILogger<CrossPlatformCpuUsageProvider>>().Object);
            services.AddSingleton(new Mock<ILogger<CrossPlatformMemoryMonitor>>().Object);
            
            // Act
            var result = services.AddSmartQueue();
            
            // Assert
            Assert.Same(services, result); // Returns the same service collection
            
            var provider = services.BuildServiceProvider();
            var cpuProvider = provider.GetService<ICpuUsageProvider>();
            var memoryMonitor = provider.GetService<IMemoryMonitor>();
            
            Assert.NotNull(cpuProvider);
            Assert.NotNull(memoryMonitor);
        }
        
        [Fact]
        public void UseSmartQueue_DoesNotThrowException()
        {
            // Simply verify that the extension method can be called without errors
            // Since we can't easily test extension methods with mocks
            
            // Arrange
            var mock = new Mock<IApplicationBuilder>();
            
            // Act & Assert - should not throw an exception
            var exception = Record.Exception(() => 
                SmartQueueMiddlewareExtensions.UseSmartQueue(mock.Object));
            
            Assert.Null(exception);
        }
    }
}