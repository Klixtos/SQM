using System;

namespace SmartQueueMiddleware.Services
{
    /// <summary>
    /// Interface for monitoring memory usage
    /// </summary>
    public interface IMemoryMonitor
    {
        /// <summary>
        /// Gets the current memory usage percentage (0-100)
        /// </summary>
        /// <returns>Memory usage as a percentage between 0 and 100</returns>
        int GetMemoryUsage();

        /// <summary>
        /// Gets detailed memory information
        /// </summary>
        /// <returns>Detailed memory statistics</returns>
        MemoryMetrics GetDetailedMemoryMetrics();
    }

    /// <summary>
    /// Structure to hold detailed memory metrics
    /// </summary>
    public struct MemoryMetrics
    {
        /// <summary>
        /// Total physical memory available in MB
        /// </summary>
        public long TotalMemoryMB { get; set; }

        /// <summary>
        /// Used physical memory in MB
        /// </summary>
        public long UsedMemoryMB { get; set; }

        /// <summary>
        /// Available physical memory in MB
        /// </summary>
        public long AvailableMemoryMB { get; set; }

        /// <summary>
        /// Memory usage as a percentage (0-100)
        /// </summary>
        public int MemoryUsagePercentage { get; set; }

        /// <summary>
        /// .NET managed memory usage (GC heap) in MB
        /// </summary>
        public long ManagedMemoryMB { get; set; }
    }
} 