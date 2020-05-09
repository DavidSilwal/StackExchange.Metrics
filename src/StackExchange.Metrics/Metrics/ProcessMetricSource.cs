using System.Collections.Generic;
using System.Diagnostics;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide basic process metrics:
    ///  - CPU time
    ///  - Virtual memory
    ///  - Paged memory
    ///  - Threads
    /// </summary>
    public sealed class ProcessMetricSource : MetricSource
    {
        private readonly SamplingGauge _processorTime;
        private readonly SamplingGauge _virtualMemory;
        private readonly SamplingGauge _pagedMemory;
        private readonly SamplingGauge _threadCount;

        /// <summary>
        /// Constructs a new instance of <see cref="ProcessMetricSource" />.
        /// </summary>
        public ProcessMetricSource()
        {
            _processorTime = new SamplingGauge("dotnet.cpu.processortime", "seconds", "Total processor time", includePrefix: false);
            _virtualMemory = Metric.Initialize<SamplingGauge>("dotnet.mem.virtual", "bytes", "Virtual memory for the process", includePrefix: false);
            _pagedMemory = Metric.Initialize<SamplingGauge>("dotnet.mem.paged", "bytes", "Paged memory for the process", includePrefix: false);
            _threadCount = Metric.Initialize<SamplingGauge>("dotnet.cpu.threads", "threads", "Threads for the process", includePrefix: false);
        }

        /// <inheritdoc/>
        public override IEnumerable<MetricBase> GetAll()
        {
            yield return _processorTime;
            yield return _virtualMemory;
            yield return _pagedMemory;
            yield return _threadCount;
        }

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
            collector.BeforeSerialization += Snapshot;
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
            collector.BeforeSerialization -= Snapshot;
        }

        private void Snapshot()
        {
            using (var process = Process.GetCurrentProcess())
            {
                _processorTime.Record(process.TotalProcessorTime.TotalSeconds);
                _virtualMemory.Record(process.VirtualMemorySize64);
                _pagedMemory.Record(process.PagedMemorySize64);
                _threadCount.Record(process.Threads.Count);
            }
        }
    }
}
