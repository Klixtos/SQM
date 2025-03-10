using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace SmartQueueMiddleware.Services
{
    /// <summary>
    /// Cross-platform implementation of memory monitoring
    /// </summary>
    public class CrossPlatformMemoryMonitor : IMemoryMonitor, IDisposable
    {
        private readonly ILogger<CrossPlatformMemoryMonitor> _logger;
        private MemoryMetrics _lastMetrics;
        private DateTime _lastCheck = DateTime.MinValue;
        private static readonly TimeSpan _cacheTime = TimeSpan.FromSeconds(1);
        private readonly SmartQueueOptions _options;
        
        // For tracking significant changes
        private const int SignificantChangeThreshold = 5; // 5% change is considered significant
        
        // Timer to periodically update memory usage
        private readonly Timer _timer;

        /// <summary>
        /// Initializes the memory monitor
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="options">SmartQueue options</param>
        public CrossPlatformMemoryMonitor(
            ILogger<CrossPlatformMemoryMonitor> logger,
            SmartQueueOptions options = null)
        {
            _logger = logger;
            _options = options ?? new SmartQueueOptions();
            _lastMetrics = new MemoryMetrics
            {
                MemoryUsagePercentage = 0,
                TotalMemoryMB = 0,
                UsedMemoryMB = 0,
                AvailableMemoryMB = 0,
                ManagedMemoryMB = 0
            };
            
            // Initialize memory metrics
            UpdateMemoryUsage();
            
            if (_options.EnableLogs)
            {
                _logger.LogDebug("Memory monitor initialized. Total memory: {TotalMemoryMB} MB", _lastMetrics.TotalMemoryMB);
            }
            
            // Update memory usage every second
            _timer = new Timer(UpdateMemoryUsageCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        
        /// <summary>
        /// Timer callback to update memory usage periodically
        /// </summary>
        private void UpdateMemoryUsageCallback(object state)
        {
            try
            {
                UpdateMemoryUsage();
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error in memory usage update timer callback");
                }
            }
        }

        /// <summary>
        /// Gets current memory usage percentage
        /// </summary>
        public int GetMemoryUsage()
        {
            return _lastMetrics.MemoryUsagePercentage;
        }

        /// <summary>
        /// Gets detailed memory metrics
        /// </summary>
        public MemoryMetrics GetDetailedMemoryMetrics()
        {
            return _lastMetrics;
        }

        /// <summary>
        /// Updates the memory usage statistics based on the current platform
        /// </summary>
        private void UpdateMemoryUsage()
        {
            try
            {
                var previousUsage = _lastMetrics.MemoryUsagePercentage;
                MemoryMetrics newMetrics;
                string platform = "Unknown";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    platform = "Windows";
                    newMetrics = GetWindowsMemoryUsage();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    platform = "Linux";
                    newMetrics = GetLinuxMemoryUsage();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    platform = "macOS";
                    newMetrics = GetMacOsMemoryUsage();
                }
                else
                {
                    platform = "Unknown (fallback)";
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogWarning("Unable to determine OS platform. Using fallback memory monitoring.");
                    }
                    newMetrics = GetFallbackMemoryUsage();
                }

                // Also track managed memory (GC heap) in all cases
                newMetrics.ManagedMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
                
                // Log the memory measurement with platform details
                if (_options?.EnableLogs == true)
                {
                    _logger.LogDebug("{Platform} memory measurement: {Usage}% (previous: {PreviousUsage}%)", 
                        platform, newMetrics.MemoryUsagePercentage, previousUsage);
                }
                
                // Check if memory usage meets or exceeds the threshold
                if (_options != null && newMetrics.MemoryUsagePercentage >= _options.MemoryThreshold)
                {
                    if (_options.EnableLogs)
                    {
                        _logger.LogWarning("Memory threshold exceeded: {Current}% >= {Threshold}%", 
                            newMetrics.MemoryUsagePercentage, _options.MemoryThreshold);
                        
                        // Add detailed metrics when threshold is exceeded
                        _logger.LogWarning("Memory details - Total: {TotalMB}MB, Used: {UsedMB}MB, Available: {AvailableMB}MB, Managed: {ManagedMB}MB",
                            newMetrics.TotalMemoryMB, newMetrics.UsedMemoryMB, 
                            newMetrics.AvailableMemoryMB, newMetrics.ManagedMemoryMB);
                    }
                }
                // Check if there's a significant change in memory usage
                else if (_options?.EnableLogs == true && Math.Abs(newMetrics.MemoryUsagePercentage - previousUsage) >= SignificantChangeThreshold)
                {
                    _logger.LogDebug("Memory usage changed significantly: {Previous}% -> {Current}%", 
                        previousUsage, newMetrics.MemoryUsagePercentage);
                    
                    // Add detailed metrics when there's a significant change
                    _logger.LogDebug("Memory details - Total: {TotalMB}MB, Used: {UsedMB}MB, Available: {AvailableMB}MB, Managed: {ManagedMB}MB",
                        newMetrics.TotalMemoryMB, newMetrics.UsedMemoryMB, 
                        newMetrics.AvailableMemoryMB, newMetrics.ManagedMemoryMB);
                }

                _lastMetrics = newMetrics;
                _lastCheck = DateTime.Now;
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error updating memory usage. Using fallback calculation.");
                }
                _lastMetrics = GetFallbackMemoryUsage();
                _lastCheck = DateTime.Now;
            }
        }

        /// <summary>
        /// Gets memory usage on Windows systems
        /// </summary>
        private MemoryMetrics GetWindowsMemoryUsage()
        {
            var metrics = new MemoryMetrics();
            
            try
            {
                // Initialize the MEMORYSTATUSEX structure
                var memoryStatusEx = new MEMORYSTATUSEX
                {
                    dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
                };
                
                // Log that we're about to call GlobalMemoryStatusEx
                if (_options?.EnableLogs == true)
                {
                    _logger.LogDebug("Calling GlobalMemoryStatusEx to get Windows memory information");
                }
                
                // Call the Windows API
                if (!GlobalMemoryStatusEx(ref memoryStatusEx))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogWarning("Failed to get Windows memory info via GlobalMemoryStatusEx. Error code: {ErrorCode}", error);
                    }
                    return GetFallbackMemoryUsage();
                }
                
                // Process the results
                metrics.TotalMemoryMB = (long)(memoryStatusEx.ullTotalPhys / (1024 * 1024));
                metrics.AvailableMemoryMB = (long)(memoryStatusEx.ullAvailPhys / (1024 * 1024));
                metrics.UsedMemoryMB = metrics.TotalMemoryMB - metrics.AvailableMemoryMB;
                metrics.MemoryUsagePercentage = (int)memoryStatusEx.dwMemoryLoad;
                
                // Additional safeguards to ensure we don't return zeros
                if (metrics.TotalMemoryMB <= 0 || metrics.MemoryUsagePercentage <= 0)
                {
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogWarning("GlobalMemoryStatusEx returned suspicious values: Total memory: {Total}MB, Usage: {Usage}%", 
                            metrics.TotalMemoryMB, metrics.MemoryUsagePercentage);
                        
                        // If the values look wrong, try the fallback
                        if (metrics.TotalMemoryMB <= 0)
                        {
                            _logger.LogWarning("Falling back to process memory calculation due to invalid total memory value");
                        }
                    }
                    
                    if (metrics.TotalMemoryMB <= 0)
                    {
                        return GetFallbackMemoryUsage();
                    }
                }
                
                if (_options?.EnableLogs == true)
                {
                    _logger.LogDebug("Windows memory details: Total: {Total}MB, Available: {Available}MB, Used: {Used}MB, Usage: {Usage}%",
                        metrics.TotalMemoryMB, metrics.AvailableMemoryMB, metrics.UsedMemoryMB, metrics.MemoryUsagePercentage);
                }
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error getting Windows memory metrics");
                }
                return GetFallbackMemoryUsage();
            }
            
            return metrics;
        }

        /// <summary>
        /// Gets memory usage on Linux systems by reading /proc/meminfo
        /// </summary>
        private MemoryMetrics GetLinuxMemoryUsage()
        {
            var metrics = new MemoryMetrics();
            
            try
            {
                string memInfo = File.ReadAllText("/proc/meminfo");
                
                // Extract memory information using regex
                long totalMemKb = ExtractValue(memInfo, @"MemTotal:\s+(\d+)");
                long freeMemKb = ExtractValue(memInfo, @"MemFree:\s+(\d+)");
                long availableMemKb = ExtractValue(memInfo, @"MemAvailable:\s+(\d+)");
                long buffers = ExtractValue(memInfo, @"Buffers:\s+(\d+)");
                long cached = ExtractValue(memInfo, @"Cached:\s+(\d+)");
                
                // If MemAvailable isn't available (older kernels), calculate available memory
                if (availableMemKb == 0)
                {
                    availableMemKb = freeMemKb + buffers + cached;
                }
                
                // Convert to MB
                metrics.TotalMemoryMB = totalMemKb / 1024;
                metrics.AvailableMemoryMB = availableMemKb / 1024;
                metrics.UsedMemoryMB = metrics.TotalMemoryMB - metrics.AvailableMemoryMB;
                
                // Calculate percentage
                if (metrics.TotalMemoryMB > 0)
                {
                    metrics.MemoryUsagePercentage = (int)((metrics.UsedMemoryMB * 100) / metrics.TotalMemoryMB);
                }
                
                if (_options?.EnableLogs == true)
                {
                    _logger.LogDebug($"Linux memory: Total={metrics.TotalMemoryMB}MB, " +
                                    $"Used={metrics.UsedMemoryMB}MB, Available={metrics.AvailableMemoryMB}MB, " +
                                    $"Usage={metrics.MemoryUsagePercentage}%");
                }
                return metrics;
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error getting Linux memory usage");
                }
                return GetFallbackMemoryUsage();
            }
        }

        /// <summary>
        /// Gets memory usage on MacOS systems using sysctl
        /// </summary>
        private MemoryMetrics GetMacOsMemoryUsage()
        {
            var metrics = new MemoryMetrics();
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n hw.memsize vm.page_free_count vm.page_size",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        if (_options?.EnableLogs == true)
                        {
                            _logger.LogWarning("Failed to start sysctl process");
                        }
                        return GetFallbackMemoryUsage();
                    }
                    
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 3)
                    {
                        long totalMemory = long.Parse(lines[0].Trim());
                        long freePages = long.Parse(lines[1].Trim());
                        long pageSize = long.Parse(lines[2].Trim());
                        
                        long freeMemory = freePages * pageSize;
                        
                        // Convert to MB
                        metrics.TotalMemoryMB = totalMemory / (1024 * 1024);
                        metrics.AvailableMemoryMB = freeMemory / (1024 * 1024);
                        metrics.UsedMemoryMB = metrics.TotalMemoryMB - metrics.AvailableMemoryMB;
                        
                        // Calculate percentage
                        metrics.MemoryUsagePercentage = (int)((metrics.UsedMemoryMB * 100) / metrics.TotalMemoryMB);
                        
                        if (_options?.EnableLogs == true)
                        {
                            _logger.LogDebug($"MacOS memory: Total={metrics.TotalMemoryMB}MB, " +
                                            $"Used={metrics.UsedMemoryMB}MB, Available={metrics.AvailableMemoryMB}MB, " +
                                            $"Usage={metrics.MemoryUsagePercentage}%");
                        }
                        return metrics;
                    }
                }
                
                if (_options?.EnableLogs == true)
                {
                    _logger.LogWarning("Failed to parse MacOS memory info");
                }
                return GetFallbackMemoryUsage();
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error getting MacOS memory usage");
                }
                return GetFallbackMemoryUsage();
            }
        }

        /// <summary>
        /// Gets fallback memory usage when platform-specific methods fail
        /// </summary>
        private MemoryMetrics GetFallbackMemoryUsage()
        {
            var metrics = new MemoryMetrics();
            
            try
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogDebug("Using fallback memory calculation via Process information");
                }
                
                // Get managed memory from the garbage collector
                long managedMemoryBytes = GC.GetTotalMemory(false);
                metrics.ManagedMemoryMB = managedMemoryBytes / (1024 * 1024);
                
                // Get process memory information
                using (var process = Process.GetCurrentProcess())
                {
                    // WorkingSet64 represents the amount of memory the process is using
                    long workingSetBytes = process.WorkingSet64;
                    long privateMemoryBytes = process.PrivateMemorySize64;
                    
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogDebug("Process memory - Working Set: {WorkingSet}MB, Private Memory: {PrivateMemory}MB, Managed Memory: {ManagedMemory}MB",
                            workingSetBytes / (1024 * 1024),
                            privateMemoryBytes / (1024 * 1024),
                            metrics.ManagedMemoryMB);
                    }
                    
                    // On Windows, try to get system memory via a different method
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        try
                        {
                            // Try to use the Performance Counter for memory information
                            using (var performanceCounter = new PerformanceCounter("Memory", "Available MBytes", true))
                            {
                                // Available memory in MB
                                float availableMemoryMB = performanceCounter.NextValue();
                                
                                using (var totalRamCounter = new PerformanceCounter("Memory", "Committed Bytes", true))
                                {
                                    // Committed memory in bytes
                                    float committedBytes = totalRamCounter.NextValue();
                                    long committedMemoryMB = (long)(committedBytes / (1024 * 1024));
                                    
                                    // Try to estimate total physical memory
                                    // This is a very rough approximation
                                    metrics.AvailableMemoryMB = (long)availableMemoryMB;
                                    metrics.UsedMemoryMB = committedMemoryMB;
                                    metrics.TotalMemoryMB = metrics.AvailableMemoryMB + metrics.UsedMemoryMB;
                                    
                                    if (metrics.TotalMemoryMB > 0)
                                    {
                                        metrics.MemoryUsagePercentage = (int)((metrics.UsedMemoryMB * 100) / metrics.TotalMemoryMB);
                                        
                                        if (_options?.EnableLogs == true)
                                        {
                                            _logger.LogDebug("Fallback Windows memory details via Performance Counter: " +
                                                "Total: {Total}MB, Available: {Available}MB, Used: {Used}MB, Usage: {Usage}%",
                                                metrics.TotalMemoryMB, metrics.AvailableMemoryMB, metrics.UsedMemoryMB, metrics.MemoryUsagePercentage);
                                        }
                                        
                                        return metrics;
                                    }
                                }
                            }
                        }
                        catch (Exception pcEx)
                        {
                            if (_options?.EnableLogs == true)
                            {
                                _logger.LogWarning(pcEx, "Failed to get memory info via Performance Counters");
                            }
                        }
                    }
                }
                
                // If we couldn't get better measurements, use rough estimation
                // Estimate total system memory based on process memory
                // This is not very accurate but better than nothing
                if (metrics.TotalMemoryMB <= 0)
                {
                    // Assume the application's working set is a reasonable percentage of total memory
                    // This is a very rough estimate, typically processes don't use more than 10-20% of system memory
                    const int estimatedAppPercentage = 15; // Assume app is using 15% of system memory
                    long workingSetMB = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    
                    metrics.TotalMemoryMB = workingSetMB * 100 / estimatedAppPercentage;
                    metrics.UsedMemoryMB = metrics.TotalMemoryMB / 2; // Assume 50% usage
                    metrics.AvailableMemoryMB = metrics.TotalMemoryMB - metrics.UsedMemoryMB;
                    metrics.MemoryUsagePercentage = 50; // Assume 50% as a reasonable default
                    
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogWarning("Using estimated memory values. Total: {Total}MB (estimated), Usage: {Usage}%", 
                            metrics.TotalMemoryMB, metrics.MemoryUsagePercentage);
                    }
                }
                
                return metrics;
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error in fallback memory calculation");
                }
                
                // Last resort fallback - just provide some reasonable defaults
                metrics.MemoryUsagePercentage = 50; // Assume 50% as a safe default
                metrics.TotalMemoryMB = 8 * 1024; // Assume 8GB
                metrics.UsedMemoryMB = 4 * 1024; // Assume 4GB used
                metrics.AvailableMemoryMB = 4 * 1024; // Assume 4GB available
                metrics.ManagedMemoryMB = 500; // Assume 500MB managed
                
                return metrics;
            }
        }

        /// <summary>
        /// Helper method to extract values from Linux /proc/meminfo
        /// </summary>
        private long ExtractValue(string input, string pattern)
        {
            var match = Regex.Match(input, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                if (long.TryParse(match.Groups[1].Value, out var value))
                {
                    return value;
                }
            }
            return 0;
        }

        /// <summary>
        /// Windows-specific structures and methods for memory monitoring
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// Disposes the timer when the service is disposed
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
} 