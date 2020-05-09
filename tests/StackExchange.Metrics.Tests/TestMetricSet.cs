using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Tests
{
    public class TestMetricSet : MetricSource
    {
        private readonly TaskCompletionSource<object> _attachTask;
        private readonly TaskCompletionSource<object> _detachTask;

        public Task AttachTask => _attachTask.Task;
        public Task DetachTask => _detachTask.Task;

        public TestMetricSet()
        {
            _attachTask = new TaskCompletionSource<object>();
            _detachTask = new TaskCompletionSource<object>();
        }

        public override void Attach(IMetricsCollector collector) => _attachTask.TrySetResult(null);

        public override void Detach(IMetricsCollector collector) => _detachTask.TrySetResult(null);

        public override IEnumerable<MetricBase> GetAll() => Enumerable.Empty<MetricBase>();
    }
}
