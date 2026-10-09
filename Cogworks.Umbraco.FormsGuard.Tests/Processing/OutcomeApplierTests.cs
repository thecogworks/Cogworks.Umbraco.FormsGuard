using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Forms.Core.Services;
using FormsForm = Umbraco.Forms.Core.Models.Form;
using FormsRecord = Umbraco.Forms.Core.Persistence.Dtos.Record;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

/// <summary>Which Forms call each decision status makes.</summary>
public class OutcomeApplierTests
{
    [Theory]
    [InlineData(DecisionStatus.Approved, "ApproveAsync")]
    [InlineData(DecisionStatus.ApprovedNotChecked, "ApproveAsync")]
    [InlineData(DecisionStatus.Quarantined, "RejectAsync")]
    [InlineData(DecisionStatus.Review, null)]
    [InlineData(DecisionStatus.Pending, null)]
    public async Task Status_MakesItsFormsCall(DecisionStatus status, string? expected)
    {
        var recordService = RecordingRecordService.Create(out var calls);
        var applier = new OutcomeApplier(recordService, [], NullLogger<OutcomeApplier>.Instance);
        var form = new FormsForm { Id = Guid.NewGuid() };
        var record = new FormsRecord { UniqueId = Guid.NewGuid(), Form = form.Id };

        await applier.ApplyToRecordAsync(status, record, form);

        if (expected is null)
        {
            Assert.Empty(calls);
        }
        else
        {
            var call = Assert.Single(calls);
            Assert.Equal(expected, call.Method);
            Assert.Same(record, call.Record);
            Assert.Same(form, call.Form);
        }
    }

    /// <summary>Records every <see cref="IRecordService"/> call and completes it with a default result.</summary>
    public class RecordingRecordService : DispatchProxy
    {
        private List<(string Method, object? Record, object? Form)> _calls = [];

        public static IRecordService Create(out List<(string Method, object? Record, object? Form)> calls)
        {
            var proxy = DispatchProxy.Create<IRecordService, RecordingRecordService>();
            calls = ((RecordingRecordService)(object)proxy)._calls;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add((targetMethod!.Name, args?.ElementAtOrDefault(0), args?.ElementAtOrDefault(1)));
            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var value = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [value]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
