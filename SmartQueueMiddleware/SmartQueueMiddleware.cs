using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartQueueMiddleware.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SmartQueueMiddleware
{
    /// <summary>
    /// Middleware that monitors resources and queues requests when system is under load
    /// </summary>
    public class SmartQueueMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SmartQueueMiddleware> _logger;
        private readonly ICpuUsageProvider _cpuUsageProvider;
        private readonly IMemoryMonitor _memoryMonitor;
        private readonly SemaphoreSlim _semaphore;
        // The channel now carries the enqueue time, the work item, and the TaskCompletionSource.
        private readonly Channel<(DateTime EnqueuedAt, Func<Task> WorkItem, TaskCompletionSource<bool> Tcs)> _requestQueue;
        private readonly SmartQueueOptions _options;

        /// <summary>
        /// Initializes the middleware
        /// </summary>
        /// <param name="next">The next middleware in the pipeline</param>
        /// <param name="logger">Logger for the middleware</param>
        /// <param name="cpuUsageProvider">CPU usage monitoring service</param>
        /// <param name="memoryMonitor">Memory monitoring service</param>
        /// <param name="serviceProvider">Service provider for resolving dependencies</param>
        /// <param name="options">Configuration options for the middleware</param>
        public SmartQueueMiddleware(
            RequestDelegate next,
            ILogger<SmartQueueMiddleware> logger,
            ICpuUsageProvider cpuUsageProvider,
            IMemoryMonitor memoryMonitor,
            IServiceProvider serviceProvider,
            SmartQueueOptions options = null)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cpuUsageProvider = cpuUsageProvider ?? throw new ArgumentNullException(nameof(cpuUsageProvider));
            _memoryMonitor = memoryMonitor;
            _options = options ?? new SmartQueueOptions();

            // Limit concurrent requests
            _semaphore = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);

            // Create bounded request queue
            _requestQueue = Channel.CreateBounded<(DateTime, Func<Task>, TaskCompletionSource<bool>)>(
                new BoundedChannelOptions(_options.MaxQueueSize)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            // Start queue processor
            _ = ProcessQueueAsync();

            if (_options.EnableLogs)
            {
                _logger.LogInformation("SmartQueueMiddleware initialized with CPU threshold: {CpuThreshold}%, Memory threshold: {MemoryThreshold}%, " +
                    "Max concurrent requests: {MaxConcurrentRequests}, Queue limit: {QueueLimit}, Max wait time: {MaxWaitTime}s",
                    _options.CpuThreshold, _options.MemoryThreshold, _options.MaxConcurrentRequests, _options.MaxQueueSize, _options.MaxWaitTimeSeconds);
            }
        }

        /// <summary>
        /// Processes the HTTP request
        /// </summary>
        /// <param name="context">The current HTTP context</param>
        /// <returns>A task representing the middleware execution</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip monitoring endpoints
            var path = context.Request.Path.ToString().ToLowerInvariant();
            if (path.Contains("health") || path.Contains("cpu") || path.Contains("memory") || path.Contains("/_") || path.StartsWith("/swagger"))
            {
                if (_options.EnableLogs)
                {
                    _logger.LogDebug("Skipping queue for path: {Path}", path);
                }
                await _next(context);
                return;
            }

            bool shouldQueue = false;
            string queueReason = string.Empty;

            try
            {
                // Check if CPU usage exceeds threshold
                int currentCpu = _cpuUsageProvider.GetCpuUsage();
                if (_options.EnableLogs)
                {
                    _logger.LogDebug("Current CPU usage: {Cpu}%", currentCpu);
                }
                
                if (currentCpu >= _options.CpuThreshold)
                {
                    shouldQueue = true;
                    queueReason = $"High CPU usage: {currentCpu}%";
                    if (_options.EnableLogs)
                    {
                        _logger.LogWarning("CPU threshold exceeded: {Cpu}% >= {Threshold}%", currentCpu, _options.CpuThreshold);
                    }
                }
                
                // Check if memory usage exceeds threshold
                if (!shouldQueue && _options.UseMemoryMonitoring && _memoryMonitor != null)
                {
                    int memoryUsage = _memoryMonitor.GetMemoryUsage();
                    if (_options.EnableLogs)
                    {
                        _logger.LogDebug("Current memory usage: {Memory}%", memoryUsage);
                    }
                    
                    if (memoryUsage >= _options.MemoryThreshold)
                    {
                        shouldQueue = true;
                        queueReason = $"High memory usage: {memoryUsage}%";
                        if (_options.EnableLogs)
                        {
                            _logger.LogWarning("Memory threshold exceeded: {Memory}% >= {Threshold}%", memoryUsage, _options.MemoryThreshold);
                            
                            var detailedMemory = _memoryMonitor.GetDetailedMemoryMetrics();
                            _logger.LogDebug("Memory details - Total: {TotalMB}MB, Used: {UsedMB}MB, Available: {AvailableMB}MB, Managed: {ManagedMB}MB",
                                detailedMemory.TotalMemoryMB,
                                detailedMemory.UsedMemoryMB,
                                detailedMemory.AvailableMemoryMB,
                                detailedMemory.ManagedMemoryMB);
                        }
                    }
                }

                if (shouldQueue)
                {
                    // Check if queue is full
                    int currentQueueSize = _requestQueue.Reader.Count;
                    if (_options.EnableLogs)
                    {
                        _logger.LogWarning("Resource threshold exceeded. {Reason}. Current queue size: {QueueSize}/{QueueLimit}", 
                            queueReason, currentQueueSize, _options.MaxQueueSize);
                    }
                    
                    // Reject if queue is full
                    if (currentQueueSize >= _options.MaxQueueSize)
                    {
                        if (_options.EnableLogs)
                        {
                            _logger.LogWarning("Queue limit reached ({QueueSize}/{QueueLimit}). Rejecting request immediately.", 
                                currentQueueSize, _options.MaxQueueSize);
                        }
                            
                        context.Response.StatusCode = _options.RejectionStatusCode;
                        await context.Response.WriteAsync(_options.RejectionMessage);
                        return;
                    }

                    // Create task completion source to track request
                    var tcs = new TaskCompletionSource<bool>();
                    
                    // Wrap request processing in a callable function
                    Func<Task> workItem = async () =>
                    {
                        try
                        {
                            await _semaphore.WaitAsync();
                            try
                            {
                                await _next(context);
                            }
                            finally
                            {
                                _semaphore.Release();
                            }
                            tcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            if (_options.EnableLogs)
                            {
                                _logger.LogError(ex, "Error processing queued request");
                            }
                            tcs.TrySetException(ex);
                            throw;
                        }
                    };

                    // Add request to queue
                    var enqueueTime = DateTime.UtcNow;
                    await _requestQueue.Writer.WriteAsync((enqueueTime, workItem, tcs));
                    
                    if (_options.EnableLogs)
                    {
                        _logger.LogInformation("Request queued at position {Position}", currentQueueSize + 1);
                    }

                    // Add queue header
                    context.Response.Headers["X-SmartQueue-Status"] = "Queued";

                    // Wait for processing or timeout
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_options.MaxWaitTimeSeconds));
                    if (await Task.WhenAny(tcs.Task, timeoutTask) == timeoutTask)
                    {
                        if (_options.EnableLogs)
                        {
                            _logger.LogWarning("Request timed out after waiting in queue for {TimeoutSeconds} seconds", _options.MaxWaitTimeSeconds);
                        }
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("Request timed out while waiting in queue");
                        return;
                    }
                }
                else
                {
                    // No queue needed, process directly
                    if (_options.EnableLogs)
                    {
                        _logger.LogDebug("Resource usage below thresholds, processing request immediately");
                    }
                    
                    await _semaphore.WaitAsync();
                    try
                    {
                        await _next(context);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                if (_options.EnableLogs)
                {
                    _logger.LogError(ex, "Error in SmartQueueMiddleware");
                }
                throw;
            }
        }

        /// <summary>
        /// Background task that processes the request queue
        /// </summary>
        /// <returns>A task representing the queue processing</returns>
        private async Task ProcessQueueAsync()
        {
            try
            {
                if (_options.EnableLogs)
                {
                    _logger.LogInformation("Request queue processor started");
                }
                
                while (await _requestQueue.Reader.WaitToReadAsync())
                {
                    if (_requestQueue.Reader.TryRead(out var item))
                    {
                        var (enqueuedAt, workItem, tcs) = item;
                        var queueTime = DateTime.UtcNow - enqueuedAt;
                        
                        if (_options.EnableLogs)
                        {
                            _logger.LogInformation("Processing request from queue. Queue time: {QueueTimeMs}ms", queueTime.TotalMilliseconds);
                        }
                        
                        try
                        {
                            // Execute the queued work item
                            _ = Task.Run(workItem);
                        }
                        catch (Exception ex)
                        {
                            if (_options.EnableLogs)
                            {
                                _logger.LogError(ex, "Error executing queued work item");
                            }
                            tcs.TrySetException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_options.EnableLogs)
                {
                    _logger.LogError(ex, "Error in queue processor");
                }
            }
        }
    }
}
