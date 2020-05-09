using StackExchange.Metrics.Handlers;
using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// A counter that is recorded using the deltas everytime it is incremented . Used for very low-volume events.
    /// <remarks>
    /// When using a Bosun endpoint <see cref="BosunMetricHandler.EnableExternalCounters"/> must be true
    /// to be reported. You'll also need to make sure your infrastructure is setup with external counters enabled. This currently requires using tsdbrelay.
    /// See https://github.com/StackExchange/StackExchange.Metrics/blob/master/docs/MetricTypes.md#externalcounter
    /// </remarks>
    /// </summary>
    public class CumulativeCounter : MetricBase
    {
        private long _countSnapshot;
        private long _count;

        /// <summary>
        /// Instantiates a new cumulative counter.
        /// </summary>
        public CumulativeCounter(string name, string unit = null, string description = null, bool includePrefix = true) : base(name, unit, description, includePrefix)
        {
        }

        /// <summary>
        /// The current value of this counter. This will reset to zero at each reporting interval.
        /// </summary>
        public long Count => _count;

        /// <summary>
        /// The type of metric (cumulative counter, in this case)
        /// </summary>
        public override MetricType MetricType => MetricType.CumulativeCounter;

        /// <summary>
        /// Increments the counter by one. If you need to increment by more than one at a time, it's probably too high volume for an external counter anyway.
        /// </summary>
        public void Increment() => Interlocked.Increment(ref _count);

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
