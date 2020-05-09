using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Similar to a SnapshotCounter, it calls a user provided Func&lt;double?&gt; to get the current gauge value each time metrics are going to be posted to
    /// a metrics handler. See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#snapshotgauge
    /// </summary>
    public class SnapshotGauge : MetricBase
    {
        private readonly Func<double?> _getValueFunc;

        /// <summary>
        /// The type of metric (gauge, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Gauge;

        /// <summary>
        /// Initializes a new snapshot gauge. The counter will call <paramref name="getValueFunc"/> at each reporting interval in order to get the current
        /// value.
        /// </summary>
        public SnapshotGauge(Func<double?> getValueFunc, string name, string unit = null, string description = null, bool includePrefix = true) : base(name, unit, description, includePrefix)
        {
            if (getValueFunc == null)
                throw new ArgumentNullException("getValueFunc");

            _getValueFunc = getValueFunc;
        }

        /// <inheritdoc/>
        protected override void Serialize(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags)
        {
            var val = GetValue();
            if (!val.HasValue)
                return;

            WriteValue(writer, val.Value, timestamp, prefix, string.Empty, tags);
        }

        /// <summary>
        /// Returns the current value which should be reported for the gauge.
        /// </summary>
        public virtual double? GetValue()
        {
            return _getValueFunc();
        }
    }
}
