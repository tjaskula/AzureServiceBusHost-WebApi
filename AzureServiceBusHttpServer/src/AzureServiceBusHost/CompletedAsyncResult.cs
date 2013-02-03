using System;
using System.Diagnostics.Contracts;

namespace Tjaskula.AzureServiceBusHost
{
    internal class CompletedAsyncResult<T> : AsyncResult
    {
        private T data;

        public CompletedAsyncResult(T data, AsyncCallback callback, object state)
            : base(callback, state)
        {
            this.data = data;
            Complete(true);
        }

        public static T End(IAsyncResult result)
        {
            Contract.Assert(result != null, "CompletedAsyncResult<T> was null.");
            Contract.Assert(result.IsCompleted, "CompletedAsyncResult<T> was not completed!");
            CompletedAsyncResult<T> completedResult = AsyncResult.End<CompletedAsyncResult<T>>(result);
            return completedResult.data;
        }
    }
}