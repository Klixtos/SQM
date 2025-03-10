using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartQueueMiddleware.Services
{
    public interface ICpuUsageProvider
    {
        /// <summary>
        /// Returns the current CPU usage as a percentage.
        /// </summary>
        int GetCpuUsage();
    }
}
