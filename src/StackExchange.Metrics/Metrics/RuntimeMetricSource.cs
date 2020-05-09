#if NETCOREAPP
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide information for
    /// the .NET Core runtime:
    ///  - CPU usage
    ///  - Working set
    ///  - GC counts
    ///  - GC sizes
    ///  - GC time
    ///  - LOH size
    ///  - Threadpool threads
    ///  - Threadpool queue lengths
    ///  - Exception counts
    /// </summary>
    /// <remarks>
    /// For where these are generated in the .NET Core runtime, see the defined counters at:
    /// https://github.com/dotnet/runtime/blob/5eda36ed557d789d888647745782b261472b9fa3/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Tracing/RuntimeEventSource.cs
    /// </remarks>
    public sealed class RuntimeMetricSource : MetricSource
    {
        private readonly ImmutableArray<MetricBase> _metrics;

        /// <summary>
        /// Constructs a new instance of <see cref="RuntimeMetricSource" />.
        /// </summary>
        public RuntimeMetricSource(IDiagnosticsCollector diagnosticsCollector)
        {
            const string SystemRuntimeEventSourceName = "System.Runtime";

            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    SystemRuntimeEventSourceName,
                    EventLevel.Informational,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", "5" }
                    }
                )
            );

            var metricBuilder = ImmutableArray.CreateBuilder<MetricBase>();

            void AddCounterCallback(string name, Counter counter)
            {
                diagnosticsCollector.AddCounterCallback(SystemRuntimeEventSourceName, name, counter.Increment);
                metricBuilder.Add(counter);
            }

            void AddGaugeCallback(string name, SamplingGauge gauge)
            {
                diagnosticsCollector.AddGaugeCallback(SystemRuntimeEventSourceName, name, gauge.Record);
                metricBuilder.Add(gauge);
            }

            var cpuUsage = Metric.Initialize<SamplingGauge>("dotnet.cpu.usage", "percent", "% CPU usage", includePrefix: false);
            var workingSet = Metric.Initialize<SamplingGauge>("dotnet.mem.working_set", "bytes", "Working set for the process", includePrefix: false);

            AddGaugeCallback("cpu-usage", cpuUsage);
            AddGaugeCallback("working-set", workingSet);

            // GC
            var gen0 = Metric.Initialize<Counter>("dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections", includePrefix: false);
            var gen1 = Metric.Initialize<Counter>("dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections", includePrefix: false);
            var gen2 = Metric.Initialize<Counter>("dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections", includePrefix: false);
            var gen0Size = Metric.Initialize<SamplingGauge>("dotnet.mem.size.gen0", "bytes", "Total number of bytes in gen-0", includePrefix: false);
            var gen1Size = Metric.Initialize<SamplingGauge>("dotnet.mem.size.gen1", "bytes", "Total number of bytes in gen-1", includePrefix: false);
            var gen2Size = Metric.Initialize<SamplingGauge>("dotnet.mem.size.gen2", "bytes", "Total number of bytes in gen-2", includePrefix: false);
            var heapSize = Metric.Initialize<SamplingGauge>("dotnet.mem.size.heap", "bytes", "Total number of bytes across all heaps", includePrefix: false);
            var lohSize = Metric.Initialize<SamplingGauge>("dotnet.mem.size.loh", "bytes", "Total number of bytes in the LOH", includePrefix: false);
            var allocRate = Metric.Initialize<Counter>("dotnet.mem.allocation_rate", "bytes/sec", "Allocation Rate (Bytes / sec)", includePrefix: false);

            AddGaugeCallback("gc-heap-size", heapSize);
            AddGaugeCallback("gen-0-size", gen0Size);
            AddGaugeCallback("gen-1-size", gen1Size);
            AddGaugeCallback("gen-2-size", gen2Size);
            AddCounterCallback("gen-0-gc-count", gen0);
            AddCounterCallback("gen-1-gc-count", gen1);
            AddCounterCallback("gen-2-gc-count", gen2);
            AddGaugeCallback("loh-size", lohSize);
            AddCounterCallback("alloc-rate", allocRate);

            // thread pool
            var threadPoolCount = Metric.Initialize<SamplingGauge>("dotnet.threadpool.count", "threads", "Number of threads in the threadpool", includePrefix: false);
            var threadPoolQueueLength = Metric.Initialize<SamplingGauge>("dotnet.threadpool.queue_length", "workitems", "Number of work items queued to the threadpool", includePrefix: false);
            var timerCount = Metric.Initialize<SamplingGauge>("dotnet.timers.count", "timers", "Number of active timers", includePrefix: false);

            AddGaugeCallback("threadpool-thread-count", threadPoolCount);
            AddGaugeCallback("threadpool-queue-length", threadPoolQueueLength);
            AddGaugeCallback("active-timer-count", timerCount);

            _metrics = metricBuilder.ToImmutable();
        }

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<MetricBase> GetAll() => _metrics;
    }
}
#endif
