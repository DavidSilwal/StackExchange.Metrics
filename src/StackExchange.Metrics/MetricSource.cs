using System;
using System.Collections.Generic;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    public interface IMetricSource
    {
        IObservable<MetricBase>
    }
    /// <summary>
    /// Represents a source of metrics for a <see cref="MetricsCollector" />.
    /// </summary>
    public abstract class MetricSource
    {
        /// <summary>
        /// Gets the default <see cref="MetricSource"/> used for ad-hoc metrics.
        /// </summary>
        public static readonly DefaultMetricSource Default = new DefaultMetricSource();

        /// <summary>
        /// Fired when a metric is added to the source.
        /// </summary>
        public event EventHandler<MetricBase> MetricAdded;

        /// <summary>
        /// Attaches this source to an <see cref="IMetricsCollector"/>.
        /// </summary>
        /// <param name="collector">
        /// An <see cref="IMetricsCollector"/> to connect to.
        /// </param>
        public abstract void Attach(IMetricsCollector collector);

        /// <summary>
        /// Detaches this source from an <see cref="IMetricsCollector"/>.
        /// </summary>
        /// <param name="collector">
        /// An <see cref="IMetricsCollector"/> to connect to.
        /// </param>
        public abstract void Detach(IMetricsCollector collector);

        /// <summary>
        /// Gets the metrics contained in this source.
        /// </summary>
        public abstract IEnumerable<MetricBase> GetAll();

        /// <summary>
        /// Fires the <see cref="MetricAdded"/> event.
        /// </summary>
        protected void OnMetricAdded(MetricBase metric) => MetricAdded?.Invoke(this, metric);
    }
}
