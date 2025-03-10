using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartQueueMiddleware;
using SmartQueueMiddleware.Services;

namespace SQMDemo
{
    // Response class for memory metrics - makes Swagger documentation nicer
    public class MemoryMetricsResponse
    {
        [JsonPropertyName("usagePercentage")]
        public int UsagePercentage { get; set; }
        
        [JsonPropertyName("totalMemoryMB")]
        public long TotalMemoryMB { get; set; }
        
        [JsonPropertyName("usedMemoryMB")]
        public long UsedMemoryMB { get; set; }
        
        [JsonPropertyName("availableMemoryMB")]
        public long AvailableMemoryMB { get; set; }
        
        [JsonPropertyName("managedMemoryMB")]
        public long ManagedMemoryMB { get; set; }
    }
    
    // Response class for CPU metrics
    public class CpuMetricsResponse
    {
        [JsonPropertyName("usagePercentage")]
        public int UsagePercentage { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure SmartQueue options - You can modify these to control logging
            var smartQueueOptions = new SmartQueueOptions
            {
                // Set to true to enable middleware logs, false to disable all logging
                EnableLogs = false,   
                
                // Performance threshold options:
                CpuThreshold = 85,
                MemoryThreshold = 90,
                MaxConcurrentRequests = 1000,
                MaxQueueSize = 2000,
                MaxWaitTimeSeconds = 25,
                UseMemoryMonitoring = true
            };

            // Add basic console logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            
            // Display the current middleware configuration
            Console.WriteLine("\nSmartQueueMiddleware Configuration:");
            Console.WriteLine(" - CPU threshold: {0}%", smartQueueOptions.CpuThreshold);
            Console.WriteLine(" - Memory threshold: {0}%", smartQueueOptions.MemoryThreshold);
            Console.WriteLine(" - Max concurrent requests: {0}", smartQueueOptions.MaxConcurrentRequests);
            Console.WriteLine(" - Max queue size: {0}", smartQueueOptions.MaxQueueSize);
            Console.WriteLine(" - Max wait time: {0}s", smartQueueOptions.MaxWaitTimeSeconds);
            Console.WriteLine(" - Middleware logging: {0}", 
                smartQueueOptions.EnableLogs ? "ENABLED" : "DISABLED");
            
            // Register SmartQueueMiddleware services with options
            builder.Services.AddSmartQueue(smartQueueOptions);

            // Add diagnostic endpoints
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Add Swagger for easy testing
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Correct middleware order: put UseHttpsRedirection before your middleware
            app.UseHttpsRedirection();

            // Add resource monitoring debug endpoints to verify monitoring
            app.MapGet("/cpu", (ICpuUsageProvider cpuProvider) => {
                var cpu = cpuProvider.GetCpuUsage();
                Console.WriteLine($"Current CPU usage: {cpu}%");
                return new CpuMetricsResponse 
                {
                    UsagePercentage = cpu,
                    Message = $"Current CPU usage: {cpu}%"
                };
            });

            app.MapGet("/memory", (IMemoryMonitor memoryMonitor) => {
                var memory = memoryMonitor.GetDetailedMemoryMetrics();
                Console.WriteLine($"Memory usage: {memory.MemoryUsagePercentage}% (Total: {memory.TotalMemoryMB}MB, Used: {memory.UsedMemoryMB}MB)");
                return new MemoryMetricsResponse
                {
                    UsagePercentage = memory.MemoryUsagePercentage,
                    TotalMemoryMB = memory.TotalMemoryMB,
                    UsedMemoryMB = memory.UsedMemoryMB,
                    AvailableMemoryMB = memory.AvailableMemoryMB,
                    ManagedMemoryMB = memory.ManagedMemoryMB
                };
            });

            // Register SmartQueue middleware with the same options
            app.UseSmartQueue(smartQueueOptions);

            app.MapGet("/test/{id}", async (HttpContext context, string id) =>
            {
                Console.WriteLine($"Processing request {id}...");
                
                // CPU-intensive operation: calculate hashes repeatedly
                var stopwatch = Stopwatch.StartNew();

                Random random = new Random();
                PerformCpuIntensiveOperation(random.Next(1, 16)); // 1-15 seconds of CPU-intensive work
               
                
                stopwatch.Stop();
                return $"{id} - Request processed successfully after {stopwatch.ElapsedMilliseconds}ms of CPU work!";
            });

            app.Run();
        }

        private static void PerformCpuIntensiveOperation(int durationSeconds)
        {
            // Create stopwatch to measure execution time
            var stopwatch = Stopwatch.StartNew();
            
            while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
            {
                // Perform computationally intensive operations
                
                // 1. Calculate SHA256 hashes repeatedly (CPU intensive)
                using (var sha256 = SHA256.Create())
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        var data = Encoding.UTF8.GetBytes($"Data to hash {i} {Guid.NewGuid()}");
                        var hash = sha256.ComputeHash(data);
                    }
                }
                
                // 2. Perform matrix multiplication (CPU and memory intensive)
                PerformMatrixMultiplication(250);
            }
        }

        private static void PerformMatrixMultiplication(int size)
        {
            // Create matrices
            double[,] matrixA = new double[size, size];
            double[,] matrixB = new double[size, size];
            double[,] result = new double[size, size];
            
            // Initialize with random values
            Random rand = new Random();
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    matrixA[i, j] = rand.NextDouble();
                    matrixB[i, j] = rand.NextDouble();
                }
            }
            
            // Perform multiplication (inefficient algorithm to increase CPU usage)
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    result[i, j] = 0;
                    for (int k = 0; k < size; k++)
                    {
                        result[i, j] += matrixA[i, k] * matrixB[k, j];
                    }
                }
            }
        }
    }
}
