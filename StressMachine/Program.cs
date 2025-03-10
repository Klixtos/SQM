using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;

namespace StressMachine
{
    internal class Program
    {
        // Track successful and failed requests
        private static int _successCount = 0;
        private static int _failureCount = 0;
        private static readonly HttpClient _httpClient;
        private static bool _continueStress = true; // Flag to control the stress test
        
        // Add performance metrics
        private static long _totalProcessingTime = 0; // Total server processing time in ms
        private static readonly Stopwatch _testStopwatch = new Stopwatch();
        private static readonly object _lockObject = new object();
        private static List<double> _responseTimes = new List<double>(); // Track individual response times
        
        // Request tracking
        private static int _lastRequestId = 0;
        private static string _lastResponse = "";
        private static Queue<string> _recentResponses = new Queue<string>(5); // Keep track of 5 most recent responses
        private static int _totalRequestsSent = 0; // Track total requests sent (not just completed)
        private static int _lastTotalRequestsSent = 0;
        private static DateTime _lastStatsUpdate = DateTime.Now;
        private static int _timeoutCount = 0; // Specifically track timeouts
        private static ConcurrentDictionary<int, DateTime> _activeRequests = new ConcurrentDictionary<int, DateTime>(); // Track active requests by ID
        private static int _lostRequests = 0; // Requests that never returned
        
        // Console control
        private static readonly object _consoleLock = new object();
        private static int _statsSectionStart = 0;
        private static int _requestLogStart = 0;
        private static bool _consoleInitialized = false;
        
        // Configure HttpClient once at startup
        static Program()
        {
            // Set up connection limits at the ServicePoint level
            ServicePointManager.DefaultConnectionLimit = 10000;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            
            var handler = new HttpClientHandler
            {
                // Bypass certificate validation for testing
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                MaxConnectionsPerServer = 5000,
                UseProxy = false
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30) // 30 second timeout
            };
        }
        
        static void Main(string[] args)
        {
            Console.Clear();
            Console.CursorVisible = false;
            
            // Initial console setup
            Console.WriteLine("=== SERVER OVERLOAD TEST - BASELINE (NO MIDDLEWARE) ===");
            Console.WriteLine("Floods the server with far more requests than it can handle");
            Console.WriteLine("This is the baseline test WITHOUT the middleware");
            Console.WriteLine("Most requests will be lost due to connection saturation");
            Console.WriteLine("Press Enter to stop the test\n");
            Console.WriteLine();
            
            // Mark where the stats section will start
            _statsSectionStart = Console.CursorTop;
            
            // Initialize statistics display
            InitializeStatDisplay();
            
            // Save position for request log
            _requestLogStart = Console.CursorTop + 2;
            Console.WriteLine("RECENT REQUESTS:");
            Console.WriteLine("----------------------------");
            // Pre-allocate 5 lines for recent requests
            for (int i = 0; i < 5; i++)
            {
                Console.WriteLine(new string(' ', 80));
            }
            
            _consoleInitialized = true;
            
            _testStopwatch.Start();
            _lastStatsUpdate = DateTime.Now;
            
            // Start the main stress test - use all available processors
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                Task.Run(() => RunStressThread(i));
            }
            
            // Start stats reporting on a separate thread
            Task.Run(UpdateStats);
            
            // Start request monitoring to check for lost requests
            Task.Run(MonitorRequests);
            
            // Wait for user to press Enter to stop
            Console.ReadLine();
            _continueStress = false;
            
            // Restore console
            Console.CursorVisible = true;
            Console.SetCursorPosition(0, _requestLogStart + 7);
            
            Console.WriteLine("\nTest stopping. Calculating final statistics...");
            Thread.Sleep(2000); // Allow time for final responses
            
            // Count any remaining active requests as lost
            foreach (var kvp in _activeRequests)
            {
                Interlocked.Increment(ref _lostRequests);
            }
            _activeRequests.Clear();
            
            // Display summary statistics
            DisplayFinalStats();
            
            Console.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }

        static void InitializeStatDisplay()
        {
            lock (_consoleLock)
            {
                Console.SetCursorPosition(0, _statsSectionStart);
                Console.WriteLine("========== REAL-TIME METRICS ==========");
                Console.WriteLine($"Requests Sent: 0 (0/sec) | Completed: 0 | Lost/Hanging: 0");
                Console.WriteLine($"Success: 0 | Failed: 0 | Timeouts: 0");
                Console.WriteLine($"Completion Rate: 0.000% | Success Rate: 0.0%");
                Console.WriteLine($"Requests/sec: 0.00 | Response: 0.0ms | Time: 00:00");
                Console.WriteLine("======================================");
            }
        }

        static void RunStressThread(int threadId)
        {
            int requestId = threadId * 1000000;
            int batchSize = 50; // Increased batch size for higher load
            
            while (_continueStress)
            {
                for (int i = 0; i < batchSize && _continueStress; i++)
                {
                    // Update last request ID for display
                    int currentRequestId = requestId++;
                    Interlocked.Exchange(ref _lastRequestId, currentRequestId);
                    Interlocked.Increment(ref _totalRequestsSent);
                    
                    // Add to active requests
                    _activeRequests[currentRequestId] = DateTime.Now;
                    
                    // Simply fire and forget - don't wait for or track responses
                    Task.Run(() => SendRequest(currentRequestId));
                }
                
                // Minimal delay to prevent thread exhaustion on the client side
                Thread.Sleep(10); 
            }
        }
        
        static async Task SendRequest(int requestNumber)
        {
            Stopwatch requestTimer = new Stopwatch();
            requestTimer.Start();
            
            try
            {
                // Use the shared HttpClient for connection pooling
                var response = await _httpClient.GetAsync($"https://localhost:7032/test/{requestNumber}");
                
                // Read response content
                var content = await response.Content.ReadAsStringAsync();
                
                requestTimer.Stop();
                double responseTimeMs = requestTimer.Elapsed.TotalMilliseconds;
                
                // Remove from active requests
                _activeRequests.TryRemove(requestNumber, out _);
                
                if (response.IsSuccessStatusCode)
                {
                    // Parse CPU work time from response if possible
                    int cpuWorkTime = 0;
                    try
                    {
                        // Try to extract "after Xms of CPU work" from the response
                        string[] parts = content.Split(new[] { "after ", "ms of CPU work" }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out cpuWorkTime))
                        {
                            Interlocked.Add(ref _totalProcessingTime, cpuWorkTime);
                        }
                    }
                    catch { /* Ignore parsing errors */ }
                    
                    lock (_lockObject)
                    {
                        _responseTimes.Add(responseTimeMs);
                    }
                    
                    Interlocked.Increment(ref _successCount);
                    
                    // Update last response
                    UpdateLastResponse($"Success: {requestNumber} (CPU: {cpuWorkTime}ms, Response: {responseTimeMs:F0}ms)");
                }
                else
                {
                    Interlocked.Increment(ref _failureCount);
                    
                    // Update last response
                    UpdateLastResponse($"Error ({response.StatusCode}): Request {requestNumber} (Response: {responseTimeMs:F0}ms)");
                }
            }
            catch (TaskCanceledException)
            {
                requestTimer.Stop();
                
                // Remove from active requests
                _activeRequests.TryRemove(requestNumber, out _);
                
                Interlocked.Increment(ref _failureCount);
                Interlocked.Increment(ref _timeoutCount);
                
                // Update last response
                UpdateLastResponse($"Timeout: Request {requestNumber} after {requestTimer.Elapsed.TotalMilliseconds:F0}ms");
            }
            catch (HttpRequestException ex)
            {
                requestTimer.Stop();
                
                // Remove from active requests
                _activeRequests.TryRemove(requestNumber, out _);
                
                Interlocked.Increment(ref _failureCount);
                
                // Update last response
                UpdateLastResponse($"Connection error: Request {requestNumber}");
            }
            catch (Exception ex)
            {
                requestTimer.Stop();
                
                // Remove from active requests
                _activeRequests.TryRemove(requestNumber, out _);
                
                Interlocked.Increment(ref _failureCount);
                
                // Update last response
                UpdateLastResponse($"Error: Request {requestNumber} - {ex.Message}");
            }
        }
        
        static async Task MonitorRequests()
        {
            while (_continueStress)
            {
                // Go through active requests and find any that are 31+ seconds old
                var now = DateTime.Now;
                var keysToRemove = new List<int>();
                
                foreach (var kvp in _activeRequests)
                {
                    if ((now - kvp.Value).TotalSeconds > 31) // 31 seconds, just over the 30-second timeout
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    if (_activeRequests.TryRemove(key, out _))
                    {
                        Interlocked.Increment(ref _lostRequests);
                    }
                }
                
                await Task.Delay(1000); // Check every second
            }
        }
        
        static void UpdateLastResponse(string response)
        {
            if (!_consoleInitialized) return;
            
            lock (_consoleLock)
            {
                try
                {
                    // Store the response
                    _lastResponse = response;
                    
                    // Add to recent responses queue
                    lock (_recentResponses)
                    {
                        _recentResponses.Enqueue(response);
                        if (_recentResponses.Count > 5)
                            _recentResponses.Dequeue();
                        
                        // Update the display with all recent responses
                        int lineIndex = 0;
                        foreach (var r in _recentResponses.Reverse())
                        {
                            Console.SetCursorPosition(0, _requestLogStart + 2 + lineIndex);
                            Console.Write(new string(' ', 80)); // Clear line
                            Console.SetCursorPosition(0, _requestLogStart + 2 + lineIndex);
                            Console.Write(r);
                            lineIndex++;
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore console errors
                }
            }
        }
        
        static async Task UpdateStats()
        {
            int lastSuccessCount = 0;
            int lastFailureCount = 0;
            Stopwatch updateTimer = new Stopwatch();
            
            while (_continueStress)
            {
                updateTimer.Restart();
                
                // Get current values
                int currentSuccess = _successCount;
                int currentFailure = _failureCount;
                int lostRequests = _lostRequests;
                int totalCompletedRequests = currentSuccess + currentFailure;
                int totalSent = _totalRequestsSent;
                int inFlightRequests = totalSent - totalCompletedRequests - lostRequests;
                
                // Calculate deltas and rates
                int successDelta = currentSuccess - lastSuccessCount;
                int failureDelta = currentFailure - lastFailureCount;
                int completedDelta = successDelta + failureDelta;
                int sentDelta = totalSent - _lastTotalRequestsSent;
                
                // Calculate request send rate
                double requestSendRate = sentDelta / (DateTime.Now - _lastStatsUpdate).TotalSeconds;
                _lastStatsUpdate = DateTime.Now;
                _lastTotalRequestsSent = totalSent;
                
                // Calculate statistics
                double elapsedSeconds = _testStopwatch.Elapsed.TotalSeconds;
                double requestsPerSecondTotal = totalCompletedRequests / elapsedSeconds;
                double successRate = totalCompletedRequests > 0 ? (double)currentSuccess / totalCompletedRequests * 100 : 0;
                double completionRate = totalSent > 0 ? (double)totalCompletedRequests / totalSent * 100 : 0;
                
                // Calculate average response time
                double avgResponseTime = 0;
                lock (_lockObject)
                {
                    if (_responseTimes.Count > 0)
                    {
                        avgResponseTime = _responseTimes.Average();
                    }
                }
                
                // Update display
                UpdateStatsDisplay(totalSent, requestSendRate, totalCompletedRequests, lostRequests, 
                                   currentSuccess, currentFailure, _timeoutCount, successRate, completionRate,
                                   requestsPerSecondTotal, avgResponseTime, elapsedSeconds);
                
                // Store current values for next iteration
                lastSuccessCount = currentSuccess;
                lastFailureCount = currentFailure;
                
                // Wait for the remainder of 2 seconds, accounting for processing time
                int remainingMs = 2000 - (int)updateTimer.ElapsedMilliseconds;
                if (remainingMs > 0)
                {
                    await Task.Delay(remainingMs);
                }
            }
        }
        
        static void UpdateStatsDisplay(int totalSent, double sendRate, int totalCompleted, int lostRequests,
                                   int successCount, int failureCount, int timeoutCount, double successRate, double completionRate,
                                   double requestsPerSec, double avgResponseTime, double elapsedSeconds)
        {
            if (!_consoleInitialized) return;
            
            lock (_consoleLock)
            {
                try
                {
                    // Update the statistics section
                    Console.SetCursorPosition(0, _statsSectionStart + 1);
                    Console.Write($"Requests Sent: {totalSent:#,##0} ({sendRate:F1}/sec) | Completed: {totalCompleted:#,##0} | Lost/Hanging: {lostRequests:#,##0}".PadRight(80));
                    
                    Console.SetCursorPosition(0, _statsSectionStart + 2);
                    Console.Write($"Success: {successCount:#,##0} | Failed: {failureCount:#,##0} | Timeouts: {timeoutCount:#,##0}".PadRight(80));
                    
                    Console.SetCursorPosition(0, _statsSectionStart + 3);
                    Console.Write($"Completion Rate: {completionRate:F3}% | Success Rate: {successRate:F1}%".PadRight(80));
                    
                    Console.SetCursorPosition(0, _statsSectionStart + 4);
                    TimeSpan duration = TimeSpan.FromSeconds(elapsedSeconds);
                    Console.Write($"Requests/sec: {requestsPerSec:F2} | Response: {avgResponseTime:F1}ms | Time: {duration.Minutes:D2}:{duration.Seconds:D2}".PadRight(80));
                }
                catch (Exception)
                {
                    // Ignore console errors
                }
            }
        }
        
        static void DisplayFinalStats()
        {
            double elapsedSeconds = _testStopwatch.Elapsed.TotalSeconds;
            int totalCompleted = _successCount + _failureCount;
            
            // Calculate percentiles if we have response times
            string p50 = "N/A";
            string p90 = "N/A";
            string p99 = "N/A";
            
            lock (_lockObject)
            {
                if (_responseTimes.Count > 0)
                {
                    _responseTimes.Sort();
                    int p50Index = (int)(_responseTimes.Count * 0.5);
                    int p90Index = (int)(_responseTimes.Count * 0.9);
                    int p99Index = (int)(_responseTimes.Count * 0.99);
                    
                    if (p50Index < _responseTimes.Count) p50 = $"{_responseTimes[p50Index]:F1}ms";
                    if (p90Index < _responseTimes.Count) p90 = $"{_responseTimes[p90Index]:F1}ms";
                    if (p99Index < _responseTimes.Count) p99 = $"{_responseTimes[p99Index]:F1}ms";
                }
            }
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n============ BASELINE RESULTS (NO MIDDLEWARE) ============");
            Console.WriteLine($"Test Duration: {elapsedSeconds:F1} seconds");
            Console.WriteLine($"Total Requests Sent: {_totalRequestsSent:#,##0}");
            Console.WriteLine($"Total Requests Completed: {totalCompleted:#,##0} ({(totalCompleted > 0 ? (double)totalCompleted / _totalRequestsSent * 100 : 0):F3}%)");
            Console.WriteLine($"Requests Lost/Hanging: {_lostRequests:#,##0} ({(double)_lostRequests / _totalRequestsSent * 100:F3}%)");
            Console.WriteLine($"Successful Requests: {_successCount:#,##0} ({(_successCount > 0 ? (double)_successCount / totalCompleted * 100 : 0):F1}% of completed)");
            Console.WriteLine($"Failed Requests: {_failureCount:#,##0} ({(_failureCount > 0 ? (double)_failureCount / totalCompleted * 100 : 0):F1}% of completed)");
            Console.WriteLine($"Timeouts: {_timeoutCount:#,##0}");
            Console.WriteLine($"Completed Requests/sec: {(totalCompleted / elapsedSeconds):F2}");
            Console.WriteLine($"Median Response Time (P50): {p50}");
            Console.WriteLine($"90th Percentile Response Time (P90): {p90}");
            Console.WriteLine($"99th Percentile Response Time (P99): {p99}");
            Console.WriteLine("======================================================\n");
            Console.WriteLine("NOTE: Lost/Hanging requests are those that never returned a response");
            Console.WriteLine("or error. This is normal when flooding a server without middleware.");
            Console.WriteLine("They would eventually timeout, but the server queue is completely full.");
            Console.WriteLine("\nWith these baseline results, run the test again WITH middleware enabled");
            Console.WriteLine("to compare how the middleware helps manage server overload.");
            Console.ResetColor();
        }
    }
}
