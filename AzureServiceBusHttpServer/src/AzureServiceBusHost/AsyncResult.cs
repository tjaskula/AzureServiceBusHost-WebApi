using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Threading;

namespace Tjaskula.AzureServiceBusHost
{
    // AsyncResult starts acquired; Complete releases.
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Ported from WCF")]
    internal abstract class AsyncResult : IAsyncResult
    {
        private static AsyncCallback _asyncCompletionWrapperCallback;
        private readonly AsyncCallback _completionCallback;
        private bool _completedSynchronously;
        private bool _endCalled;
        private Exception _exception;
        private bool _isCompleted;
        private AsyncCompletion _nextAsyncCompletion;
        private readonly object _state;
        private Action _beforePrepareAsyncCompletionAction;
        private Func<IAsyncResult, bool> _checkSyncValidationFunc;
        private ManualResetEvent _manualResetEvent;
        private readonly object _manualResetEventLock = new object();

        protected AsyncResult(AsyncCallback callback, object state)
        {
            _completionCallback = callback;
            _state = state;
        }

        /// <summary>
        /// Can be utilized by subclasses to write core completion code for both the sync and async paths
        /// in one location, signalling chainable synchronous completion with the boolean result,
        /// and leveraging PrepareAsyncCompletion for conversion to an AsyncCallback.
        /// </summary>
        /// <remarks>NOTE: requires that "this" is passed in as the state object to the asynchronous sub-call being used with a completion routine.</remarks>
        protected delegate bool AsyncCompletion(IAsyncResult result);

        public object AsyncState
        {
            get { return _state; }
        }

        public WaitHandle AsyncWaitHandle
        {
            get
            {
                lock (_manualResetEventLock)
                {
                    if (_manualResetEvent == null)
                    {
                        _manualResetEvent = new ManualResetEvent(_isCompleted);
                    }
                }

                return _manualResetEvent;
            }
        }

        public bool CompletedSynchronously
        {
            get { return _completedSynchronously; }
        }

        public bool HasCallback
        {
            get { return _completionCallback != null; }
        }

        public bool IsCompleted
        {
            get { return _isCompleted; }
        }

        // used in conjunction with PrepareAsyncCompletion to allow for finally blocks
        protected Action<AsyncResult, Exception> OnCompleting { get; set; }

        // subclasses like TraceAsyncResult can use this to wrap the callback functionality in a scope
        protected Action<AsyncCallback, IAsyncResult> VirtualCallback { get; set; }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Exception is propagated or FailFast")]
        protected void Complete(bool didCompleteSynchronously)
        {
            if (_isCompleted)
            {
                throw new InvalidOperationException(string.Format("The IAsyncResult implementation '{0}' tried to complete a single operation multiple times. This could be caused by an incorrect application IAsyncResult implementation or other extensibility code, such as an IAsyncResult that returns incorrect CompletedSynchronously values or invokes the AsyncCallback multiple times.", GetType()));
            }

            _completedSynchronously = didCompleteSynchronously;
            if (OnCompleting != null)
            {
                // Allow exception replacement, like a catch/throw pattern.
                try
                {
                    OnCompleting(this, _exception);
                }
                catch (Exception e)
                {
                    _exception = e;
                }
            }

            if (didCompleteSynchronously)
            {
                // If we completedSynchronously, then there's no chance that the manualResetEvent was created so
                // we don't need to worry about a race
                Contract.Assert(_manualResetEvent == null, "No ManualResetEvent should be created for a synchronous AsyncResult.");
                _isCompleted = true;
            }
            else
            {
                lock (_manualResetEventLock)
                {
                    _isCompleted = true;
                    if (_manualResetEvent != null)
                    {
                        _manualResetEvent.Set();
                    }
                }
            }

            if (_completionCallback != null)
            {
                try
                {
                    if (VirtualCallback != null)
                    {
                        VirtualCallback(_completionCallback, this);
                    }
                    else
                    {
                        _completionCallback(this);
                    }
                }
#pragma warning disable 1634
#pragma warning suppress 56500 // transferring exception to another thread
                catch (Exception e)
                {
                    // String is not resourced because it occurs only in debug build, only with a fatal assert,
                    // and because it appears as an unused resource in a release build
                    Contract.Assert(false, string.Format("{0}{1}{2}", "Async Callback threw an exception.", Environment.NewLine, e.ToString()));
                }
#pragma warning restore 1634
            }
        }

        protected void Complete(bool didCompleteSynchronously, Exception error)
        {
            _exception = error;
            Complete(didCompleteSynchronously);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Exception is propagated")]
        private static void AsyncCompletionWrapperCallback(IAsyncResult result)
        {
            if (result == null)
            {
                throw new InvalidOperationException("A null value was returned from an async 'Begin' method or passed to an AsyncCallback. Async 'Begin' implementations must return a non-null IAsyncResult and pass the same IAsyncResult object as the parameter to the AsyncCallback.");
            }
            if (result.CompletedSynchronously)
            {
                return;
            }

            AsyncResult thisPtr = (AsyncResult)result.AsyncState;
            if (!thisPtr.OnContinueAsyncCompletion(result))
            {
                return;
            }

            AsyncCompletion callback = thisPtr.GetNextCompletion();
            if (callback == null)
            {
                ThrowInvalidAsyncResult(result);
            }

            bool completeSelf = false;
            Exception completionException = null;
            try
            {
                completeSelf = callback(result);
            }
            catch (Exception e)
            {
                completeSelf = true;
                completionException = e;
            }

            if (completeSelf)
            {
                thisPtr.Complete(false, completionException);
            }
        }

        // Note: this should be only derived by the TransactedAsyncResult
        protected virtual bool OnContinueAsyncCompletion(IAsyncResult result)
        {
            return true;
        }

        // Note: this should be used only by the TransactedAsyncResult
        protected void SetBeforePrepareAsyncCompletionAction(Action completionAction)
        {
            _beforePrepareAsyncCompletionAction = completionAction;
        }

        // Note: this should be used only by the TransactedAsyncResult
        protected void SetCheckSyncValidationFunc(Func<IAsyncResult, bool> validationFunc)
        {
            _checkSyncValidationFunc = validationFunc;
        }

        protected AsyncCallback PrepareAsyncCompletion(AsyncCompletion callback)
        {
            if (_beforePrepareAsyncCompletionAction != null)
            {
                _beforePrepareAsyncCompletionAction();
            }

            _nextAsyncCompletion = callback;
            if (AsyncResult._asyncCompletionWrapperCallback == null)
            {
                AsyncResult._asyncCompletionWrapperCallback = new AsyncCallback(AsyncCompletionWrapperCallback);
            }
            return AsyncResult._asyncCompletionWrapperCallback;
        }

        protected bool CheckSyncContinue(IAsyncResult result)
        {
            AsyncCompletion dummy;
            return TryContinueHelper(result, out dummy);
        }

        protected bool SyncContinue(IAsyncResult result)
        {
            AsyncCompletion callback;
            if (TryContinueHelper(result, out callback))
            {
                return callback(result);
            }
            else
            {
                return false;
            }
        }

        private bool TryContinueHelper(IAsyncResult result, out AsyncCompletion callback)
        {
            if (result == null)
            {
                throw new InvalidOperationException("A null value was returned from an async 'Begin' method or passed to an AsyncCallback. Async 'Begin' implementations must return a non-null IAsyncResult and pass the same IAsyncResult object as the parameter to the AsyncCallback.");
            }

            callback = null;
            if (_checkSyncValidationFunc != null)
            {
                if (!_checkSyncValidationFunc(result))
                {
                    return false;
                }
            }
            else if (!result.CompletedSynchronously)
            {
                return false;
            }

            callback = GetNextCompletion();
            if (callback == null)
            {
                ThrowInvalidAsyncResult("Only call Check/SyncContinue once per async operation (once per PrepareAsyncCompletion).");
            }
            return true;
        }

        private AsyncCompletion GetNextCompletion()
        {
            AsyncCompletion result = _nextAsyncCompletion;
            _nextAsyncCompletion = null;
            return result;
        }

        protected static void ThrowInvalidAsyncResult(IAsyncResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            throw new InvalidOperationException(string.Format("An incorrect implementation of the IAsyncResult interface may be returning incorrect values from the CompletedSynchronously property or calling the AsyncCallback more than once. The type {0} could be the incorrect implementation.", result.GetType()));
        }

        protected static void ThrowInvalidAsyncResult(string debugText)
        {
            string message = "An incorrect implementation of the IAsyncResult interface may be returning incorrect values from the CompletedSynchronously property or calling the AsyncCallback more than once.";
            if (debugText != null)
            {
#if DEBUG
                message += " " + debugText;
#endif
            }
            throw new InvalidOperationException(message);
        }

        [SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter", Justification = "Existing API")]
        protected static TAsyncResult End<TAsyncResult>(IAsyncResult result)
            where TAsyncResult : AsyncResult
        {
            if (result == null)
            {
                throw new ArgumentNullException("result");
            }

            TAsyncResult asyncResult = result as TAsyncResult;

            if (asyncResult == null)
            {
                throw new ArgumentException("An incorrect IAsyncResult was provided to an 'End' method. The IAsyncResult object passed to 'End' must be the one returned from the matching 'Begin' or passed to the callback provided to 'Begin'.", "result");
            }

            if (asyncResult._endCalled)
            {
                throw new InvalidOperationException("End cannot be called twice on an AsyncResult.");
            }

            asyncResult._endCalled = true;

            if (!asyncResult._isCompleted)
            {
                asyncResult.AsyncWaitHandle.WaitOne();
            }

            if (asyncResult._manualResetEvent != null)
            {
                asyncResult._manualResetEvent.Close();
            }

            if (asyncResult._exception != null)
            {
                throw asyncResult._exception;
            }

            return asyncResult;
        }
    }
}