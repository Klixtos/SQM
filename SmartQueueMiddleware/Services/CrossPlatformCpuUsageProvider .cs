using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace SmartQueueMiddleware.Services
{
    /// <summary>
    /// Cross-platform implementation of CPU usage monitoring
    /// </summary>
    public class CrossPlatformCpuUsageProvider : ICpuUsageProvider, IDisposable
    {
        private volatile int _currentCpuUsage;
        private readonly Timer _timer;
        private readonly ILogger<CrossPlatformCpuUsageProvider> _logger;
        private readonly SmartQueueOptions _options;

        // For Linux CPU measurement
        private ulong _prevIdleTime;
        private ulong _prevTotalTime;
        
        // For Windows CPU measurement using Performance Counter
        private PerformanceCounter _cpuCounter;

        // For tracking significant changes
        private const int SignificantChangeThreshold = 5; // 5% change is considered significant

        /// <summary>
        /// Initializes the CPU usage provider
        /// </summary>
        public CrossPlatformCpuUsageProvider(
            ILogger<CrossPlatformCpuUsageProvider> logger,
            SmartQueueOptions options = null)
        {
            _logger = logger;
            _options = options ?? new SmartQueueOptions();
            
            InitializeForPlatform();
            
            // Poll CPU usage every second
            _timer = new Timer(UpdateCpuUsageCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            
            if (_options.EnableLogs)
            {
                _logger.LogDebug("CPU Usage Provider initialized for {Platform}", 
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown");
            }
        }
        
        private void InitializeForPlatform()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows uses Performance Counter
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                    
                    if (_options?.EnableLogs == true)
                        _logger.LogDebug("Windows Performance Counter initialized");
                    
                    // First call always returns 0, so call it during init
                    _cpuCounter.NextValue();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Linux reads from /proc/stat
                    ReadLinuxCpuStats(out _prevIdleTime, out _prevTotalTime);
                    
                    if (_options?.EnableLogs == true)
                        _logger.LogDebug("Linux CPU monitoring initialized");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (_options?.EnableLogs == true)
                        _logger.LogDebug("macOS CPU monitoring initialized");
                }
                else
                {
                    if (_options?.EnableLogs == true)
                        _logger.LogWarning("Unsupported platform for CPU monitoring");
                }
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                    _logger.LogError(ex, "Error initializing CPU monitoring");
            }
        }

        private void UpdateCpuUsageCallback(object state)
        {
            try
            {
                UpdateCpuUsage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CPU usage update timer callback");
            }
        }

        private void UpdateCpuUsage()
        {
            try
            {
                int previousValue = _currentCpuUsage;
                int newCpuValue;
                
                // Get platform-specific CPU value
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    newCpuValue = GetLinuxCpuUsage();
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogDebug("Linux CPU measurement: {CpuUsage}% (previous: {PrevCpuUsage}%)", 
                            newCpuValue, previousValue);
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    newCpuValue = GetWindowsCpuUsage();
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogDebug("Windows CPU measurement: {CpuUsage}% (previous: {PrevCpuUsage}%)", 
                            newCpuValue, previousValue);
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    newCpuValue = GetMacOsCpuUsage();
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogDebug("macOS CPU measurement: {CpuUsage}% (previous: {PrevCpuUsage}%)", 
                            newCpuValue, previousValue);
                    }
                }
                else
                {
                    newCpuValue = GetProcessCpuUsage();
                    if (_options?.EnableLogs == true)
                    {
                        _logger.LogDebug("Fallback CPU measurement: {CpuUsage}% (previous: {PrevCpuUsage}%)", 
                            newCpuValue, previousValue);
                    }
                }
                
                // Log if threshold exceeded
                if (_options != null && newCpuValue >= _options.CpuThreshold)
                {
                    if (_options.EnableLogs)
                    {
                        _logger.LogWarning("CPU threshold exceeded: {Current}% >= {Threshold}%", 
                            newCpuValue, _options.CpuThreshold);
                    }
                }
                // Log significant changes
                else if (_options?.EnableLogs == true && Math.Abs(newCpuValue - previousValue) >= SignificantChangeThreshold)
                {
                    _logger.LogDebug("CPU usage changed significantly: {Previous}% -> {Current}%", 
                        previousValue, newCpuValue);
                }
                
                _currentCpuUsage = newCpuValue;
            }
            catch (Exception ex)
            {
                if (_options?.EnableLogs == true)
                {
                    _logger.LogError(ex, "Error updating CPU usage: {ErrorMessage}", ex.Message);
                }
            }
        }

        private int GetLinuxCpuUsage()
        {
            try
            {
                ulong idleTime, totalTime;
                ReadLinuxCpuStats(out idleTime, out totalTime);
                
                // Calculate CPU usage based on delta between measurements
                if (_prevTotalTime > 0)
                {
                    var totalDelta = totalTime - _prevTotalTime;
                    var idleDelta = idleTime - _prevIdleTime;
                    
                    if (totalDelta == 0)
                {
                    return 0;
                    }
                    
                    var usage = 100 - (int)Math.Round(100.0 * idleDelta / totalDelta);
                    
                    // Update for next calculation
                    _prevIdleTime = idleTime;
                    _prevTotalTime = totalTime;
                    
                    return Math.Max(0, Math.Min(100, usage));
                }
                
                // First run or reset
                _prevIdleTime = idleTime;
                _prevTotalTime = totalTime;
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Linux CPU stats");
                return GetProcessCpuUsage(); // Fallback
            }
        }
        
        private void ReadLinuxCpuStats(out ulong idleTime, out ulong totalTime)
        {
            idleTime = 0;
            totalTime = 0;
            
            var statContent = File.ReadAllText("/proc/stat");
            var cpuLine = statContent.Split('\n').FirstOrDefault(l => l.StartsWith("cpu "));
            
            if (string.IsNullOrEmpty(cpuLine))
                return;
                
            var values = cpuLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (values.Length < 5)
                return;
                
            var user = ulong.Parse(values[1]);
            var nice = ulong.Parse(values[2]);
            var system = ulong.Parse(values[3]);
            var idle = ulong.Parse(values[4]);
            var iowait = values.Length > 5 ? ulong.Parse(values[5]) : 0;
            var irq = values.Length > 6 ? ulong.Parse(values[6]) : 0;
            var softirq = values.Length > 7 ? ulong.Parse(values[7]) : 0;
            var steal = values.Length > 8 ? ulong.Parse(values[8]) : 0;
            
            idleTime = idle + iowait;
            totalTime = user + nice + system + idle + iowait + irq + softirq + steal;
        }

        private int GetWindowsCpuUsage()
        {
            try
            {
                if (_cpuCounter != null)
                {
                    var value = (int)Math.Round(_cpuCounter.NextValue());
                    return Math.Max(0, Math.Min(100, value));
                }
                return GetProcessCpuUsage(); // Fallback
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Windows CPU usage");
                return GetProcessCpuUsage(); // Fallback
            }
        }

        private int GetMacOsCpuUsage()
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"top -l 1 | grep -E '^CPU'\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                var match = Regex.Match(output, @"(\d+\.\d+)%\s+user,\s+(\d+\.\d+)%\s+sys");
                
                if (match.Success && match.Groups.Count >= 3)
                {
                    var user = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    var sys = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    return (int)Math.Round(user + sys);
                }
                
                return GetProcessCpuUsage(); // Fallback
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error measuring macOS CPU usage");
                return GetProcessCpuUsage(); // Fallback
            }
        }
        
        // Fallback method that uses the current process CPU time
        private int GetProcessCpuUsage()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                Thread.Sleep(100); // Short sample
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                
                var cpuUsagePercent = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;
                
                return (int)Math.Round(cpuUsagePercent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in fallback CPU measurement");
                return 50; // Default fallback value
            }
        }

        /// <summary>
        /// Gets the current CPU usage as a percentage
        /// </summary>
        public int GetCpuUsage() => _currentCpuUsage;

        /// <summary>
        /// Disposes the timer and performance counter resources
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
            _cpuCounter?.Dispose();
        }

        private class CpuUsage
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

            [StructLayout(LayoutKind.Sequential)]
            private struct FILETIME
            {
                public uint dwLowDateTime;
                public uint dwHighDateTime;
            }

            private FILETIME _prevIdleTime;
            private FILETIME _prevKernelTime;
            private FILETIME _prevUserTime;

            public int GetUsage()
            {
                try
                {
                    FILETIME idleTime, kernelTime, userTime;
                    GetSystemTimes(out idleTime, out kernelTime, out userTime);

                    ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | idleTime.dwLowDateTime;
                    ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | kernelTime.dwLowDateTime;
                    ulong user = ((ulong)userTime.dwHighDateTime << 32) | userTime.dwLowDateTime;

                    if (_prevIdleTime.dwLowDateTime != 0 || _prevIdleTime.dwHighDateTime != 0)
                    {
                        ulong prevIdle = ((ulong)_prevIdleTime.dwHighDateTime << 32) | _prevIdleTime.dwLowDateTime;
                        ulong prevKernel = ((ulong)_prevKernelTime.dwHighDateTime << 32) | _prevKernelTime.dwLowDateTime;
                        ulong prevUser = ((ulong)_prevUserTime.dwHighDateTime << 32) | _prevUserTime.dwLowDateTime;

                        ulong idleDiff = idle - prevIdle;
                        ulong totalDiff = (kernel - prevKernel) + (user - prevUser);

                        if (totalDiff > 0)
                        {
                            int usagePercentage = (int)(100 - ((idleDiff * 100) / totalDiff));
                            _prevIdleTime = idleTime;
                            _prevKernelTime = kernelTime;
                            _prevUserTime = userTime;
                            return usagePercentage;
                        }
                    }

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
                    return 0;
                }
                catch
                {
                    return 0;
                }
            }
        }
    }
}
