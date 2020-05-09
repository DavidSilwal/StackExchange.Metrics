using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Every data point is sent to Bosun. Good for low-volume events.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#eventgauge
    /// </summary>
    public class EventGauge : MetricBase
    {
        private ConcurrentBag<PendingMetric> _pendingSnapshot;
        private ConcurrentBag<PendingMetric> _pendingMetrics = new ConcurrentBag<PendingMetric>();

        /// <summary>
        /// Instantiates a new event gauge.
        /// </summary>
        public EventGauge(string name, string unit = null, string description = null, bool includePrefix = true) : base(name, unit, description, includePrefix)
        {
        }

        /// <summary>
        /// The type of metric (gauge).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <inheritdoc/>
        protected override void Serialize(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags)
        {
            var pending = _pendingSnapshot;
            if (pending == null || pending.Count == 0)
                return;
            
            foreach (var p in pending)
            {
                WriteValue(writer, p.Value, p.Time, prefix, string.Empty, tags);
            }
        }

        /// <summary>
        /// See <see cref="MetricBase.PreSerialize"/>
        /// </summary>
        protected override void PreSerialize()
        {
            _pendingSnapshot = Interlocked.Exchange(ref _pendingMetrics, new ConcurrentBag<PendingMetric>());
        }

        /// <summary>
        /// Records a data point which will be sent to metrics handlers.
        /// </summary>
        public void Record(double value) => _pendingMetrics.Add(new PendingMetric(value, DateTime.UtcNow));

        /// <summary>
        /// Records a data point with an explicit timestamp which will be sent to metrics handlers.
        /// </summary>
        public void Record(double value, DateTime time) => _pendingMetrics.Add(new PendingMetric(value, time));

        private readonly struct PendingMetric
        {
            public PendingMetric(double value, DateTime time)
            {
                Value = value;
                Time = time;
            }

            public double Value { get; }
            public DateTime Time { get; }
        }
    }
}
