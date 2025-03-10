# Smart Queue Middleware

A resource-aware request queuing middleware for ASP.NET Core applications that helps manage high load scenarios by monitoring both CPU and memory usage.

## Overview

Smart Queue Middleware helps ASP.NET Core applications gracefully handle high traffic by monitoring system resources (CPU and memory) and dynamically managing request processing. When system resources are constrained, it queues less time-sensitive requests rather than rejecting them or allowing them to contribute to system overload.

## Key Features

- **Resource-Aware Request Throttling**: Queues incoming requests when CPU or memory usage exceeds configurable thresholds
- **Cross-Platform Monitoring**: Tracks CPU and memory usage across Windows, Linux, and macOS systems
- **Configurable Request Queue**: Bounded queue with customizable size and overflow policies
- **Controlled Concurrency**: Limits parallel request processing to maintain system stability
- **Request Timeout Management**: Ensures queued requests receive timely responses or appropriate timeout messages
- **Graceful Degradation**: Provides informative status codes and messages under load
- **Simple Logging Control**: Turn middleware logging on/off with a single setting
- **Simple Integration**: Easy setup with extension methods and minimal configuration
- **Reactive Queue Processing**: Non-blocking, event-driven queue processing without polling intervals

## When to Use This Middleware

Smart Queue Middleware is particularly useful for:

- **Mixed Workloads**: Applications handling both resource-intensive and lightweight requests
- **Variable Traffic Patterns**: Systems experiencing burst traffic or unpredictable load spikes
- **Resource-Constrained Environments**: Applications running in environments with limited CPU or memory resources
- **Applications with Nonessential Requests**: Systems where some requests can be delayed briefly during peak loads
- **Enterprise Systems**: Where maintaining predictable degradation patterns and SLAs is critical

This middleware is not intended to replace dedicated message queues or background job processors for genuinely asynchronous workloads.

## Implementation Details

The Smart Queue Middleware uses:

- **Channel-based queuing**: For efficient, thread-safe request queuing with bounded capacity
- **SemaphoreSlim**: For concurrency control of request processing
- **Cross-platform CPU Monitoring**: Detection of CPU usage across Windows, Linux, and macOS
- **Cross-platform Memory Monitoring**: Detection of memory usage across Windows, Linux, and macOS
- **TaskCompletionSource**: For managing the lifecycle of queued requests
- **Exception handling patterns**: For graceful timeouts and cancellations
- **Reactive processing**: Queue processor that waits asynchronously for new items without constant polling

## Usage

```csharp
// In Program.cs

// 1. Register services
builder.Services.AddSmartQueue();

// 2. Add middleware with default settings
app.UseSmartQueue();

// OR with custom options
app.UseSmartQueue(new SmartQueueOptions {
    CpuThreshold = 85,              // CPU usage threshold percentage
    MemoryThreshold = 90,           // Memory usage threshold percentage
    MaxConcurrentRequests = 100,    // Maximum parallel requests
    MaxQueueSize = 50,              // Maximum queued requests before rejecting
    MaxWaitTimeSeconds = 10,        // Maximum time a request can wait in queue
    UseMemoryMonitoring = true,     // Enable/disable memory monitoring
    EnableLogs = true               // Enable/disable middleware logging (set to false in production for performance)
});

// 3. Add diagnostic endpoints (optional)
app.MapGet("/cpu", (ICpuUsageProvider cpuProvider) => {
    return $"Current CPU usage: {cpuProvider.GetCpuUsage()}%";
});

app.MapGet("/memory", (IMemoryMonitor memoryMonitor) => {
    var memory = memoryMonitor.GetDetailedMemoryMetrics();
    return new {
        UsagePercentage = memory.MemoryUsagePercentage,
        TotalMemoryMB = memory.TotalMemoryMB,
        UsedMemoryMB = memory.UsedMemoryMB,
        AvailableMemoryMB = memory.AvailableMemoryMB
    };
});
```

## Configuration Options

The `SmartQueueOptions` class provides the following configuration options:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| CpuThreshold | int | 80 | CPU usage percentage threshold (0-100) to trigger request queuing |
| MemoryThreshold | int | 90 | Memory usage percentage threshold (0-100) to trigger request queuing |
| MaxQueueSize | int | 100 | Maximum number of requests that can be queued before rejecting |
| MaxWaitTimeSeconds | int | 30 | Maximum time in seconds a request can wait in the queue |
| MaxConcurrentRequests | int | 100 | Maximum number of requests that can be processed concurrently |
| RejectionStatusCode | int | 503 | HTTP status code to return when a request is rejected |
| RejectionMessage | string | "Server is under high load. Please try again later." | Message to return when a request is rejected |
| UseMemoryMonitoring | bool | true | Whether to use memory monitoring alongside CPU monitoring |
| EnableLogs | bool | true | Whether to enable logging from the middleware (set to false to disable all logs) |

## Required Dependencies

The middleware requires the following NuGet packages:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Http" Version="2.3.0" />
  <PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.1" />
  <PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.0" />
</ItemGroup>
```

> Note: The `System.Diagnostics.PerformanceCounter` package is required for Windows CPU monitoring. On Linux and macOS, the middleware will use platform-specific mechanisms.

## Resource Monitoring Details

### CPU Monitoring

The middleware uses a cross-platform CPU monitoring system that works on:
- Windows: Using performance counters via `PerformanceCounter` class (tested and verified)
- Linux: Reading from `/proc/stat` to calculate total and idle CPU time (implementation provided but not extensively tested)
- macOS: Using `sysctl` commands to retrieve CPU statistics (implementation provided but not extensively tested)
- Fallback: If the platform-specific methods fail, it falls back to using `Process.GetCurrentProcess()` metrics

### Memory Monitoring

The middleware monitors system memory using platform-specific implementations:
- Windows: Using the GlobalMemoryStatusEx API via P/Invoke (tested and verified)
- Linux: Reading from `/proc/meminfo` to get memory statistics (implementation provided but not extensively tested)
- macOS: Using `sysctl` commands to retrieve memory information (implementation provided but not extensively tested)
- Fallback: If platform-specific methods fail, it falls back to using managed memory metrics from the garbage collector

> **Note:** The Windows implementations have been thoroughly tested. The Linux and macOS implementations are provided for cross-platform compatibility but have not been extensively tested in production environments.

## Queue Processing

The queue processing uses a non-polling, reactive approach:

1. A background task starts when the middleware is initialized
2. The task continuously waits for items to be added to the queue using `_requestQueue.Reader.WaitToReadAsync()`
3. When a request is queued, the background task is immediately notified and processes the item
4. The queue processor executes the queued work item in a separate task
5. This approach ensures immediate processing without wasting CPU resources when the queue is empty

## Logging Control

The middleware provides a simple way to control logging output:

- When `EnableLogs` is `true` (default), the middleware logs according to your application's logging configuration
- When `EnableLogs` is `false`, the middleware produces no logs at all, not even warnings or errors

This allows you to:
- Enable detailed logging during development and testing
- Completely disable logging in production for maximum performance
- Toggle logging without reconfiguring your entire application's logging infrastructure

## Considerations and Limitations

- **Resource Monitoring Overhead**: There is a small overhead to CPU and memory monitoring which should be considered for extremely performance-sensitive applications
- **Memory Usage**: Queued requests consume memory while waiting for processing
- **Not a Circuit Breaker**: This is not a replacement for proper circuit breaker patterns for downstream service failures
- **Local Resource Monitoring**: The current implementation monitors local resources only and is not cluster-aware
- **Windows Dependency**: The Windows CPU monitoring relies on the PerformanceCounter class, which requires the appropriate NuGet package
- **Cross-Platform Testing**: The Linux and macOS implementations are provided but have not been thoroughly tested in real-world environments. Users should conduct their own testing before deploying to production on these platforms.

## Alternative Approaches

Different scenarios might benefit from different solutions:

- **For I/O-bound workloads**: Traditional async/await patterns in ASP.NET Core already provide excellent performance
- **For fully asynchronous operations**: Consider background job processors like Hangfire or HangfireIO
- **For external service protection**: Consider circuit breaker patterns with libraries like Polly
- **For simple rate limiting**: Consider rate limiting middleware

## Disclaimer

This middleware is provided "as is" without warranty of any kind, express or implied. Use at your own risk.

- **Production Readiness**: While designed for production use, you should thoroughly test this middleware in your specific environment before deploying to production.
- **Performance Impact**: Resource monitoring introduces a small overhead that should be evaluated in your specific use case.
- **No Liability**: The authors and contributors are not responsible for any damage or issues that may arise from using this middleware.
- **Not an Official Microsoft Product**: This is a community-contributed middleware and is not officially supported by Microsoft.

## Inspiration

This middleware draws inspiration from several concurrent programming patterns and queue management strategies across different platforms:

- Event loop systems with backpressure mechanisms
- Operating system scheduler designs
- Enterprise application queue management systems
- HTTP server request buffering strategies

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License:

```
MIT License

Copyright (c) 2023 Smart Queue Middleware Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Acknowledgements

Thanks to the ASP.NET Core team for the excellent middleware pipeline that makes extensions like this possible. 

## Solution Structure

The SmartQueueMiddleware solution consists of the following projects:

### Core Components

- **SmartQueueMiddleware**: The main middleware library containing:
  - `SmartQueueMiddleware.cs` - The primary middleware implementation
  - `SmartQueueMiddlewareExtensions.cs` - Contains the options class and extension methods for both service registration and middleware pipeline integration
  - `Services/` - Resource monitoring implementations:
    - CPU usage monitoring with cross-platform support
    - Memory monitoring with cross-platform support

### Examples and Testing

- **SQMDemo**: A sample ASP.NET Core web application that demonstrates middleware usage:
  - Endpoints for triggering CPU-intensive operations
  - Diagnostic endpoints to view current resource usage
  - Configuration examples for the middleware

- **StressMachine**: A simple console application for experimentation (name inspired by "The IT Crowd"):
  - Just a playground for generating load on the demo application
  - Creates concurrent HTTP requests to test queue behavior
  - Not intended for serious performance testing or benchmarking
  - Useful for quickly seeing the middleware in action

- **SmartQueueMiddleware.Tests**: Unit tests covering:
  - Configuration options validation
  - CPU monitoring functionality
  - Memory monitoring functionality
  - Extension methods for service registration

### Project Dependencies

The middleware has minimal dependencies, requiring only:
- ASP.NET Core HTTP Abstractions
- Microsoft Extensions Logging
- System.Diagnostics.PerformanceCounter (Windows-only)

## Getting Started with the Source Code

1. **Clone the repository**:
   ```
   git clone https://github.com/Klixtos/SQM.git
   ```

2. **Open the solution** in your preferred IDE or code editor

3. **Build the solution**:
   ```
   dotnet build
   ```

4. **Run the tests**:
   ```
   dotnet test
   ```

5. **Try the demo application**:
   ```
   cd SQMDemo
   dotnet run
   ```

6. **Run the stress test** (in a separate terminal):
   ```
   cd StressMachine
   dotnet run
   ```

This will help users understand the different components in your solution and how they work together, without implying that a NuGet package is available yet. 