using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Record as often as you want, but only the last value recorded before the reporting interval is sent to an endpoint (it samples the current value).
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#samplinggauge
    /// </summary>
    public class SamplingGauge : MetricBase
    {
        private double _value = double.NaN;

        /// <summary>
        /// Instantiates a new sampling gauge.
        /// </summary>
        public SamplingGauge(string name, string unit = null, string description = null, bool includePrefix = true) : base(name, unit, description, includePrefix)
        {
        }

        /// <summary>
        /// The current value of the gauge.
        /// </summary>
        public double CurrentValue => _value;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <inheritdoc/>
        protected override void Serialize(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags)
        {
            var value = _value;
            if (double.IsNaN(value))
                return;

            WriteValue(writer, value, timestamp, prefix, string.Empty, tags);
        }

        /// <summary>
        /// Records the current value of the gauge. Use Double.NaN to disable this gauge.
        /// </summary>
        public void Record(double value) => Interlocked.Exchange(ref _value, value);
    }
}
