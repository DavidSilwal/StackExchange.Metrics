using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// A general-purpose manually incremented long-integer counter.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#counter
    /// </summary>
    public class Counter : MetricBase
    {
        long _countSnapshot;

        /// <summary>
        /// The underlying field for <see cref="Value"/>. This allows for direct manipulation via Interlocked methods.
        /// </summary>
        long _count;

        /// <summary>
        /// The current value of the counter. This will reset to zero at each reporting interval.
        /// </summary>
        public long Value => _count;

        /// <summary>
        /// The metric type (counter, in this case).
        /// </summary>
        public override MetricType MetricType => MetricType.Counter;

        /// <summary>
        /// Instantiates a new counter.
        /// </summary>
        public Counter(string name, string unit = null, string description = null, bool includePrefix = true) : base(name, unit, description, includePrefix)
        {

            collector(prefix: "app", defaultTags: { "host":"myhost", "btag": "A"})
            metric(name: "mycounter", tags: { "ctag": "hi"})
            metric(name: "app.mycounter", tags: { "ctag": "hi"},includeprefix:false)
        }

        /// <summary>
        /// Increments the counter by <paramref name="amount"/>. This method is thread-safe.
        /// </summary>
        public void Increment(long amount = 1) => Interlocked.Add(ref _count, amount);

        /// <inheritdoc/>
        protected override void Serialize(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags)
        {
            if (_countSnapshot > 0)
            {
                WriteValue(writer, _countSnapshot, timestamp, prefix, string.Empty, tags);
            }
        }

        /// <summary>
        /// See <see cref="MetricBase.PreSerialize"/>
        /// </summary>
        protected override void PreSerialize()
        {
            _countSnapshot = Interlocked.Exchange(ref _count, 0);
        }
    }
}
