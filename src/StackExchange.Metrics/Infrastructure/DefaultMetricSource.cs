using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Default implementation of <see cref="MetricSource"/> that allows adding arbitrary
    /// metrics. This is automatically consumed by a <see cref="MetricsCollector"/>.
    /// </summary>
    public class DefaultMetricSource : MetricSource
    {
        private ImmutableArray<MetricBase> _metrics = ImmutableArray<MetricBase>.Empty;

        internal DefaultMetricSource()
        {
        }

        /// <summary>
        /// Creates a metric (time series) and adds it to this source.
        /// </summary>
        /// <param name="metric">A pre-instantiated metric.</param>
        public T AddMetric<T>(T metric) where T : MetricBase
        {
            if (metric == null)
            {
                throw new ArgumentNullException(nameof(metric));
            }

            _metrics = _metrics.Add(metric);
            OnMetricAdded(metric);
            return metric;
        }

        /// <inheritdoc/>
        public override IEnumerable<MetricBase> GetAll() => _metrics;

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
        }
    }
}
