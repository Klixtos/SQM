using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SmartQueueMiddleware.Services;

namespace SmartQueueMiddleware
{
    /// <summary>
    /// Configuration options for SmartQueueMiddleware
    /// </summary>
    public class SmartQueueOptions
    {
        /// <summary>
        /// CPU usage threshold percentage (0-100) that triggers request queuing
        /// </summary>
        public int CpuThreshold { get; set; } = 80;

        /// <summary>
        /// Memory usage threshold percentage (0-100) that triggers request queuing
        /// </summary>
        public int MemoryThreshold { get; set; } = 90;

        /// <summary>
        /// Maximum number of requests in the queue before rejecting new requests
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// Maximum wait time in seconds for a queued request
        /// </summary>
        public int MaxWaitTimeSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of concurrent requests being processed
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 100;

        /// <summary>
        /// HTTP status code returned when rejecting requests
        /// </summary>
        public int RejectionStatusCode { get; set; } = 503;

        /// <summary>
        /// Message returned when rejecting requests
        /// </summary>
        public string RejectionMessage { get; set; } = "Server is under high load. Please try again later.";

        /// <summary>
        /// Whether to monitor memory usage in addition to CPU
        /// </summary>
        public bool UseMemoryMonitoring { get; set; } = true;
        
        /// <summary>
        /// Controls middleware logging (true = enabled, false = disabled)
        /// </summary>
        public bool EnableLogs { get; set; } = true;
    }

    /// <summary>
    /// Extensions for registering and using SmartQueueMiddleware
    /// </summary>
    public static class SmartQueueMiddlewareExtensions
    {
        /// <summary>
        /// Registers SmartQueue services
        /// </summary>
        public static IServiceCollection AddSmartQueue(this IServiceCollection services, SmartQueueOptions options = null)
        {
            options ??= new SmartQueueOptions();
            
            // Register CPU usage provider
            services.AddSingleton<ICpuUsageProvider>(sp => 
                new CrossPlatformCpuUsageProvider(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrossPlatformCpuUsageProvider>>(),
                    options));
            
            // Register memory monitor
            services.AddSingleton<IMemoryMonitor>(sp => 
                new CrossPlatformMemoryMonitor(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CrossPlatformMemoryMonitor>>(),
                    options));

            return services;
        }

        /// <summary>
        /// Adds SmartQueue middleware to the request pipeline
        /// </summary>
        public static IApplicationBuilder UseSmartQueue(this IApplicationBuilder builder, SmartQueueOptions options = null)
        {
            return builder.UseMiddleware<SmartQueueMiddleware>(options ?? new SmartQueueOptions());
        }
    }
} 