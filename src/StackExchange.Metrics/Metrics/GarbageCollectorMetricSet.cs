#if !NETCOREAPP
using System;
using System.Collections.Generic;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide GC metrics:
    ///  - Gen0 collections
    ///  - Gen1 collections
    ///  - Gen2 collections
    /// </summary>
    public sealed class GarbageCollectorMetricSet : MetricSource
    {
        private SamplingGauge _gen0;
        private SamplingGauge _gen1;
        private SamplingGauge _gen2;

        /// <summary>
        /// Constructs a new instance of <see cref="GarbageCollectorMetricSet" />.
        /// </summary>
        public GarbageCollectorMetricSet()
        {
            _gen0 = Metric.Initialize<SamplingGauge>("dotnet.mem.collections.gen0", "collections", "Number of gen-0 collections", includePrefix: false);
            _gen1 = Metric.Initialize<SamplingGauge>("dotnet.mem.collections.gen1", "collections", "Number of gen-1 collections", includePrefix: false);
            _gen2 = Metric.Initialize<SamplingGauge>("dotnet.mem.collections.gen2", "collections", "Number of gen-2 collections", includePrefix: false);
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

        /// <inheritdoc/>
        public override IEnumerable<MetricBase> GetAll()
        {
            yield return _gen0;
            yield return _gen1;
            yield return _gen2;
        }

        private void Snapshot()
        {
            _gen0.Record(GC.CollectionCount(0));
            _gen1.Record(GC.CollectionCount(1));
            _gen2.Record(GC.CollectionCount(2));
        }
    }
}
#endif
