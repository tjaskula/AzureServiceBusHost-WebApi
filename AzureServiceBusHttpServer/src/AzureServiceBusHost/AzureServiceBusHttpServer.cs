using System;
using System.Collections.Concurrent;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Http;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Hosting;
using Microsoft.ServiceBus;

namespace Tjaskula.AzureServiceBusHost
{
    public class AzureServiceBusHttpServer : HttpServer
    {
        private readonly AzureServiceBusConfiguration _configuration;

        private static readonly AsyncCallback _onCloseListenerComplete = new AsyncCallback(OnCloseListenerComplete);
        private static readonly AsyncCallback _onCloseChannelComplete = new AsyncCallback(OnCloseChannelComplete);

        // State: 0 = new, 1 = open, 2 = closed
        private int _state;

        private static readonly TimeSpan _acceptTimeout = TimeSpan.MaxValue;
        private static readonly TimeSpan _receiveTimeout = TimeSpan.MaxValue;

        private const double IncreaseWindowSizeRatio = .8;
        private const double DecreaseWindowSizeRatio = .2;
        private const int InitialWindowSizeMultiplier = 8;
        private const int MinimumWindowSizeMultiplier = 1;
        private static readonly int InitialWindowSize = AzureServiceBusConfiguration.MultiplyByProcessorCount(InitialWindowSizeMultiplier);
        private static readonly int MinimumWindowSize = AzureServiceBusConfiguration.MultiplyByProcessorCount(MinimumWindowSizeMultiplier);

        private AsyncCallback _onOpenListenerComplete;
        private AsyncCallback _onAcceptChannelComplete;
        private AsyncCallback _onOpenChannelComplete;
        private AsyncCallback _onReceiveRequestContextComplete;
        private AsyncCallback _onReplyComplete;

        private IChannelListener<IReplyChannel> _listener;
        private ConcurrentBag<IReplyChannel> _channels = new ConcurrentBag<IReplyChannel>();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private TaskCompletionSource<bool> _openTaskCompletionSource;
        private TaskCompletionSource<bool> _closeTaskCompletionSource;

        private int _requestsOutstanding;
        private int _windowSize;
        private readonly object _windowSizeLock = new object();

        public AzureServiceBusHttpServer(AzureServiceBusConfiguration configuration) : base(configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            
            _configuration = configuration;

            InitializeCallbacks();
        }

        public AzureServiceBusHttpServer(AzureServiceBusConfiguration configuration, HttpMessageHandler dispatcher)
            : base(configuration, dispatcher)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");

            _configuration = configuration;

            InitializeCallbacks();
        }

        /// <summary>
        /// Initialize async callbacks.
        /// </summary>
        private void InitializeCallbacks()
        {
            _onOpenListenerComplete = new AsyncCallback(OnOpenListenerComplete);
            _onAcceptChannelComplete = new AsyncCallback(OnAcceptChannelComplete);
            _onOpenChannelComplete = new AsyncCallback(OnOpenChannelComplete);
            _onReceiveRequestContextComplete = new AsyncCallback(OnReceiveRequestContextComplete);
            _onReplyComplete = new AsyncCallback(OnReplyComplete);
        }

        /// <summary>
        /// Opens the current <see cref="HttpServer"/> instance.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous <see cref="HttpServer"/> open operation. Once this task completes successfully the server is running.</returns>
        public Task OpenAsync()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 1)
            {
                throw new InvalidOperationException(string.Format("This '{0}' instance has already been started once. To start another instance, please create a new '{0}' object and start that.", typeof(AzureServiceBusHttpServer).Name));
            }

            _openTaskCompletionSource = new TaskCompletionSource<bool>();
            BeginOpenListener(this);
            return _openTaskCompletionSource.Task;
        }

        /// <summary>
        /// Closes the current <see cref="HttpServer"/> instance.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous <see cref="HttpServer"/> close operation.</returns>
        public Task CloseAsync()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
            {
                var tcs = new TaskCompletionSource<AsyncVoid>();
                tcs.SetResult(default(AsyncVoid));
                return tcs.Task;
            }

            _closeTaskCompletionSource = new TaskCompletionSource<bool>();

            // Cancel requests currently being processed
            _cancellationTokenSource.Cancel();

            BeginCloseListener(this);
            return _closeTaskCompletionSource.Task;
        }

        private static void BeginCloseListener(AzureServiceBusHttpServer server)
        {
            Contract.Assert(server != null);

            try
            {
                if (server._listener != null)
                {
                    IAsyncResult result = server._listener.BeginClose(_onCloseListenerComplete, server);
                    if (result.CompletedSynchronously)
                    {
                        CloseListenerComplete(result);
                    }
                }
            }
            catch
            {
                CloseNextChannel(server);
            }
        }

        private static void OnCloseListenerComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            if (result.CompletedSynchronously)
            {
                return;
            }

            CloseListenerComplete(result);
        }

        private static void OnCloseChannelComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            if (result.CompletedSynchronously)
            {
                return;
            }

            CloseChannelComplete(result);
        }

        private static void CloseListenerComplete(IAsyncResult result)
        {
            AzureServiceBusHttpServer server = (AzureServiceBusHttpServer)result.AsyncState;
            Contract.Assert(server != null);
            Contract.Assert(server._listener != null);

            try
            {
                server._listener.EndClose(result);
            }
            catch
            {
            }
            finally
            {
                CloseNextChannel(server);
            }
        }

        private static void CloseNextChannel(AzureServiceBusHttpServer server)
        {
            Contract.Assert(server != null);

            IReplyChannel channel;
            if (server._channels.TryTake(out channel))
            {
                BeginCloseChannel(new ChannelContext(server, channel));
            }
            else
            {
                CompleteTask(server._closeTaskCompletionSource);
            }
        }

        private static void BeginCloseChannel(ChannelContext channelContext)
        {
            Contract.Assert(channelContext != null);

            try
            {
                IAsyncResult result = channelContext.Channel.BeginClose(_onCloseChannelComplete, channelContext);
                if (result.CompletedSynchronously)
                {
                    CloseChannelComplete(result);
                }
            }
            catch
            {
                CloseNextChannel(channelContext.Server);
            }
        }

        private static void CloseChannelComplete(IAsyncResult result)
        {
            ChannelContext channelContext = (ChannelContext)result.AsyncState;
            Contract.Assert(channelContext != null);

            try
            {
                channelContext.Channel.EndClose(result);
            }
            catch
            {
            }
            finally
            {
                CloseNextChannel(channelContext.Server);
            }
        }

        private void BeginOpenListener(AzureServiceBusHttpServer server)
        {
            Contract.Assert(server != null);

            try
            {
                // Create WCF HTTP transport channel
                var binding = new WebHttpRelayBinding(EndToEndWebHttpSecurityMode.None, RelayClientAuthenticationType.None);

                // Get it configured
				BindingParameterCollection bindingParameters = server._configuration.ConfigureBinding(binding);
                if (bindingParameters == null)
                {
                    bindingParameters = new BindingParameterCollection();
                }

                // Build channel listener
				server._listener = binding.BuildChannelListener<IReplyChannel>(server._configuration.BaseAddress, bindingParameters);
                
                if (server._listener == null)
                {
                    throw new InvalidOperationException(string.Format("Error creating '{0}' instance using '{1}'.", typeof(IChannelListener).Name, typeof(IReplyChannel).Name));
                }

                IAsyncResult result = server._listener.BeginOpen(_onOpenListenerComplete, server);
                
                if (result.CompletedSynchronously)
                {
                    OpenListenerComplete(result);
                }
            }
            catch (Exception e)
            {
                FaultTask(server._openTaskCompletionSource, e);
            }
        }

        private void OnOpenListenerComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            if (result.CompletedSynchronously)
            {
                return;
            }

            OpenListenerComplete(result);
        }

        private void OpenListenerComplete(IAsyncResult result)
        {
            var server = (AzureServiceBusHttpServer)result.AsyncState;
            Contract.Assert(server != null);
            Contract.Assert(server._listener != null);

            try
            {
                server._listener.EndOpen(result);

                // Start accepting channel
                BeginAcceptChannel(server);
            }
            catch (Exception e)
            {
                FaultTask(server._openTaskCompletionSource, e);
            }
        }

        private void BeginAcceptChannel(AzureServiceBusHttpServer server)
        {
            Contract.Assert(server != null);
            Contract.Assert(server._listener != null);

            IAsyncResult result = BeginTryAcceptChannel(server, _onAcceptChannelComplete);
            if (result.CompletedSynchronously)
            {
                AcceptChannelComplete(result);
            }
        }

        private static IAsyncResult BeginTryAcceptChannel(AzureServiceBusHttpServer server, AsyncCallback callback)
        {
            Contract.Assert(server != null);
            Contract.Assert(server._listener != null);
            Contract.Assert(callback != null);

            try
            {
                return server._listener.BeginAcceptChannel(_acceptTimeout, callback, server);
            }
            catch (CommunicationObjectAbortedException)
            {
                return new CompletedAsyncResult<bool>(true, callback, server);
            }
            catch (CommunicationObjectFaultedException)
            {
                return new CompletedAsyncResult<bool>(true, callback, server);
            }
            catch (TimeoutException)
            {
                return new CompletedAsyncResult<bool>(false, callback, server);
            }
            catch (CommunicationException)
            {
                return new CompletedAsyncResult<bool>(false, callback, server);
            }
            catch
            {
                return new CompletedAsyncResult<bool>(false, callback, server);
            }
        }

        private void OnAcceptChannelComplete(IAsyncResult result)
        {
            if (result.CompletedSynchronously)
            {
                return;
            }

            AcceptChannelComplete(result);
        }

        private void AcceptChannelComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            AzureServiceBusHttpServer server = (AzureServiceBusHttpServer)result.AsyncState;
            Contract.Assert(server != null, "host cannot be null");
            Contract.Assert(server._listener != null, "host.listener cannot be null");

            IReplyChannel channel;
            if (EndTryAcceptChannel(result, out channel))
            {
                // If we didn't get a channel then we stop accepting new channels
                if (channel != null)
                {
                    server._channels.Add(channel);
                    BeginOpenChannel(new ChannelContext(server, channel));
                }
                else
                {
                    CancelTask(server._openTaskCompletionSource);
                }
            }
            else
            {
                // Start accepting next channel
                BeginAcceptChannel(server);
            }
        }

        private static bool EndTryAcceptChannel(IAsyncResult result, out IReplyChannel channel)
        {
            Contract.Assert(result != null);

            CompletedAsyncResult<bool> handlerResult = result as CompletedAsyncResult<bool>;
            if (handlerResult != null)
            {
                channel = null;
                return CompletedAsyncResult<bool>.End(handlerResult);
            }
            else
            {
                try
                {
                    AzureServiceBusHttpServer server = (AzureServiceBusHttpServer)result.AsyncState;
                    Contract.Assert(server != null);
                    Contract.Assert(server._listener != null);
                    channel = server._listener.EndAcceptChannel(result);
                    return true;
                }
                catch (CommunicationObjectAbortedException)
                {
                    channel = null;
                    return true;
                }
                catch (CommunicationObjectFaultedException)
                {
                    channel = null;
                    return true;
                }
                catch (TimeoutException)
                {
                    channel = null;
                    return false;
                }
                catch (CommunicationException)
                {
                    channel = null;
                    return false;
                }
                catch
                {
                    channel = null;
                    return false;
                }
            }
        }

        private void BeginOpenChannel(ChannelContext channelContext)
        {
            Contract.Assert(channelContext != null);
            Contract.Assert(channelContext.Channel != null);

            try
            {
                IAsyncResult result = channelContext.Channel.BeginOpen(_onOpenChannelComplete, channelContext);
                if (result.CompletedSynchronously)
                {
                    OpenChannelComplete(result);
                }
            }
            catch (Exception e)
            {
                FaultTask(channelContext.Server._openTaskCompletionSource, e);
            }
        }

        private void OnOpenChannelComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            if (result.CompletedSynchronously)
            {
                return;
            }

            OpenChannelComplete(result);
        }

        private void OpenChannelComplete(IAsyncResult result)
        {
            ChannelContext channelContext = (ChannelContext)result.AsyncState;
            Contract.Assert(channelContext != null);
            Contract.Assert(channelContext.Channel != null);

            try
            {
                channelContext.Channel.EndOpen(result);

                // The channel is open and we can complete the open task
                CompleteTask(channelContext.Server._openTaskCompletionSource);

                // Start pumping messages
                int initialWindowSize = Math.Min(InitialWindowSize, _configuration.MaxConcurrentRequests);
                Interlocked.Add(ref _windowSize, initialWindowSize);
                for (int index = 0; index < initialWindowSize; index++)
                {
                    BeginReceiveRequestContext(channelContext);
                }

                // Start accepting next channel
                BeginAcceptChannel(channelContext.Server);
            }
            catch (Exception e)
            {
                FaultTask(channelContext.Server._openTaskCompletionSource, e);
            }
        }

        private void BeginReceiveRequestContext(ChannelContext context)
        {
            Contract.Assert(context != null);

            if (context.Channel.State != CommunicationState.Opened)
            {
                return;
            }

            IAsyncResult result = BeginTryReceiveRequestContext(context, _onReceiveRequestContextComplete);
            if (result.CompletedSynchronously)
            {
                ReceiveRequestContextComplete(result);
            }
        }

        private void OnReceiveRequestContextComplete(IAsyncResult result)
        {
            if (result.CompletedSynchronously)
            {
                return;
            }

            ReceiveRequestContextComplete(result);
        }

        private static IAsyncResult BeginTryReceiveRequestContext(ChannelContext channelContext, AsyncCallback callback)
        {
            Contract.Assert(channelContext != null);
            Contract.Assert(callback != null);

            try
            {
                return channelContext.Channel.BeginTryReceiveRequest(_receiveTimeout, callback, channelContext);
            }
            catch (CommunicationObjectAbortedException)
            {
                return new CompletedAsyncResult<bool>(true, callback, channelContext);
            }
            catch (CommunicationObjectFaultedException)
            {
                return new CompletedAsyncResult<bool>(true, callback, channelContext);
            }
            catch (CommunicationException)
            {
                return new CompletedAsyncResult<bool>(false, callback, channelContext);
            }
            catch (TimeoutException)
            {
                return new CompletedAsyncResult<bool>(false, callback, channelContext);
            }
            catch
            {
                return new CompletedAsyncResult<bool>(false, callback, channelContext);
            }
        }

        private void ReceiveRequestContextComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            ChannelContext channelContext = (ChannelContext)result.AsyncState;
            Contract.Assert(channelContext != null);

            RequestContext requestContext;
            if (EndTryReceiveRequestContext(result, out requestContext))
            {
                if (requestContext != null)
                {
                    Interlocked.Increment(ref _requestsOutstanding);
                    if (TryIncreaseWindowSize())
                    {
                        // Spin off an additional BeginReceiveRequest to increase the window size by 1
                        BeginReceiveRequestContext(channelContext);
                    }
                    ProcessRequestContext(channelContext, requestContext);
                }
            }
        }

        private static bool EndTryReceiveRequestContext(IAsyncResult result, out RequestContext requestContext)
        {
            Contract.Assert(result != null);

            CompletedAsyncResult<bool> handlerResult = result as CompletedAsyncResult<bool>;
            if (handlerResult != null)
            {
                requestContext = null;
                return CompletedAsyncResult<bool>.End(handlerResult);
            }
            else
            {
                try
                {
                    ChannelContext channelContext = (ChannelContext)result.AsyncState;
                    Contract.Assert(channelContext != null, "context cannot be null");
                    return channelContext.Channel.EndTryReceiveRequest(result, out requestContext);
                }
                catch (CommunicationObjectAbortedException)
                {
                    requestContext = null;
                    return true;
                }
                catch (CommunicationObjectFaultedException)
                {
                    requestContext = null;
                    return true;
                }
                catch (CommunicationException)
                {
                    requestContext = null;
                    return false;
                }
                catch (TimeoutException)
                {
                    requestContext = null;
                    return false;
                }
                catch
                {
                    requestContext = null;
                    return false;
                }
            }
        }

        private void ProcessRequestContext(ChannelContext channelContext, RequestContext requestContext)
        {
            Contract.Assert(channelContext != null);
            Contract.Assert(requestContext != null);

            HttpRequestMessage request = null;
            try
            {
                // Get the HTTP request from the WCF Message
                request = requestContext.RequestMessage.ToHttpRequestMessage();
                if (request == null)
                {
                    throw new InvalidOperationException(string.Format("Could not obtain an HTTP request from message of type '{0}'.", requestContext.RequestMessage.GetType()));
                }

                // Add information about whether the request is local or not
                request.Properties.Add(HttpPropertyKeys.IsLocalKey, new Lazy<bool>(() => IsLocal(requestContext.RequestMessage)));

                // Submit request up the stack
                HttpResponseMessage responseMessage = null;

                channelContext.Server.SendAsync(request, channelContext.Server._cancellationTokenSource.Token)
                    .ContinueWith(t =>
                                      {
                                          responseMessage = t.Result;
                                          if (t.IsCanceled || channelContext.Server._cancellationTokenSource.Token.IsCancellationRequested)
                                          {
                                              responseMessage = request.CreateResponse(HttpStatusCode.InternalServerError);
                                              CancelTask(channelContext.Server._openTaskCompletionSource);
                                          }

                                          if (t.IsFaulted)
                                          {
                                              try
                                              {
                                                  responseMessage = request.CreateErrorResponse(HttpStatusCode.InternalServerError, t.Exception);
                                              }
                                              catch (Exception e)
                                              {
                                                  FaultTask(channelContext.Server._openTaskCompletionSource, e);
                                              }
                                          }

                                          if (responseMessage != null)
                                          {
                                              Message reply = responseMessage.ToMessage();
                                              BeginReply(new ReplyContext(channelContext, requestContext, reply));
                                              if (!t.IsCompleted)
                                                CompleteTask(channelContext.Server._openTaskCompletionSource);
                                          }
                                      });
            }
            catch (Exception e)
            {
                HttpResponseMessage response = request != null ?
                    request.CreateErrorResponse(HttpStatusCode.InternalServerError, e) :
                    new HttpResponseMessage(HttpStatusCode.BadRequest);
                Message reply = response.ToMessage();
                BeginReply(new ReplyContext(channelContext, requestContext, reply));
            }
        }

        private void BeginReply(ReplyContext replyContext)
        {
            Contract.Assert(replyContext != null);

            try
            {
                IAsyncResult result = replyContext.RequestContext.BeginReply(replyContext.Reply, _onReplyComplete, replyContext);
                if (result.CompletedSynchronously)
                {
                    ReplyComplete(result);
                }
            }
            catch
            {
                Interlocked.Decrement(ref _requestsOutstanding);
                BeginNextRequest(replyContext.ChannelContext);
                replyContext.Dispose();
            }
        }

        private void ReplyComplete(IAsyncResult result)
        {
            ReplyContext replyContext = (ReplyContext)result.AsyncState;
            Contract.Assert(replyContext != null);

            try
            {
                replyContext.RequestContext.EndReply(result);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Decrement(ref _requestsOutstanding);
                BeginNextRequest(replyContext.ChannelContext);
                replyContext.Dispose();
            }
        }

        private void OnReplyComplete(IAsyncResult result)
        {
            Contract.Assert(result != null);

            if (result.CompletedSynchronously)
            {
                return;
            }

            ReplyComplete(result);
        }

        private void BeginNextRequest(ChannelContext context)
        {
            if (TryDecreaseWindowSize())
            {
                // Decrease the window size by 1 by avoiding calling BeginReceiveRequest
                return;
            }

            BeginReceiveRequestContext(context);
        }

        private static void FaultTask(TaskCompletionSource<bool> taskCompletionSource, Exception exception)
        {
            Contract.Assert(taskCompletionSource != null);
            taskCompletionSource.TrySetException(exception);
        }

        private static void CancelTask(TaskCompletionSource<bool> taskCompletionSource)
        {
            Contract.Assert(taskCompletionSource != null);
            taskCompletionSource.TrySetCanceled();
        }

        private static void CompleteTask(TaskCompletionSource<bool> taskCompletionSource)
        {
            Contract.Assert(taskCompletionSource != null);
            taskCompletionSource.TrySetResult(true);
        }

        private static bool IsLocal(Message message)
        {
            RemoteEndpointMessageProperty remoteEndpointProperty;
            if (message.Properties.TryGetValue(RemoteEndpointMessageProperty.Name, out remoteEndpointProperty))
            {
                IPAddress remoteAddress;
                if (IPAddress.TryParse(remoteEndpointProperty.Address, out remoteAddress))
                {
                    return IPAddress.IsLoopback(remoteAddress);
                }
            }
            return false;
        }

        private bool TryIncreaseWindowSize()
        {
            if (ShouldIncreaseWindowSize())
            {
                // If we can't get the lock, just keep the window size the same
                // It's better to keep the window size the same than risk affecting performance by waiting on a lock
                // And if the lock is taken, some other thread is already updating the window size to a better value
                if (Monitor.TryEnter(_windowSizeLock))
                {
                    try
                    {
                        // Recheck that we should increase the window size to guard for changes between the time we take the lock and the time we increase the window size
                        if (ShouldIncreaseWindowSize())
                        {
                            // Increase Window Size
                            _windowSize++;
                            return true;
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_windowSizeLock);
                    }
                }
            }
            return false;
        }

        private bool TryDecreaseWindowSize()
        {
            if (ShouldDecreaseWindowSize())
            {
                // If we can't get the lock, just keep the window size the same
                // It's better to keep the window size the same than risk affecting performance by waiting on a lock
                // And if the lock is taken, some other thread is already updating the window size to a better value
                if (Monitor.TryEnter(_windowSizeLock))
                {
                    try
                    {
                        // Recheck that we should decrease the window size to guard for changes between the time we take the lock and the time we increase the window size
                        if (ShouldDecreaseWindowSize())
                        {
                            _windowSize--;
                            return true;
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_windowSizeLock);
                    }
                }
            }
            return false;
        }

        private bool ShouldIncreaseWindowSize()
        {
            return _windowSize < _configuration.MaxConcurrentRequests && _requestsOutstanding > _windowSize * IncreaseWindowSizeRatio;
        }

        private bool ShouldDecreaseWindowSize()
        {
            return _windowSize > MinimumWindowSize && _requestsOutstanding < _windowSize * DecreaseWindowSizeRatio;
        }

        /// <summary>
        /// Provides context for receiving an <see cref="System.ServiceModel.Channels.RequestContext"/> instance asynchronously.
        /// </summary>
        private class ChannelContext
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ChannelContext"/> class.
            /// </summary>
            /// <param name="server">The host to associate with this context.</param>
            /// <param name="channel">The channel to associate with this channel.</param>
            public ChannelContext(AzureServiceBusHttpServer server, IReplyChannel channel)
            {
                Contract.Assert(server != null);
                Contract.Assert(channel != null);
                Server = server;
                Channel = channel;
            }

            /// <summary>
            /// Gets the <see cref="AzureServiceBusHttpServer"/> instance.
            /// </summary>
            /// <value>
            /// The <see cref="AzureServiceBusHttpServer"/> instance.
            /// </value>
            public AzureServiceBusHttpServer Server { get; private set; }

            /// <summary>
            /// Gets the <see cref="IReplyChannel"/> instance.
            /// </summary>
            /// <value>
            /// The <see cref="System.ServiceModel.Channels.RequestContext"/> instance.
            /// </value>
            public IReplyChannel Channel { get; private set; }
        }

        /// <summary>
        /// Provides context for sending a <see cref="System.ServiceModel.Channels.Message"/> instance asynchronously in response
        /// to a request.
        /// </summary>
        private class ReplyContext : IDisposable
        {
            private bool _disposed;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChannelContext"/> class.
            /// </summary>
            /// <param name="channelContext">The channel context to associate with this reply context.</param>
            /// <param name="requestContext">The request context to associate with this reply context.</param>
            /// <param name="reply">The reply to associate with this reply context.</param>
            public ReplyContext(ChannelContext channelContext, RequestContext requestContext, Message reply)
            {
                Contract.Assert(channelContext != null);
                Contract.Assert(requestContext != null);
                Contract.Assert(reply != null);

                ChannelContext = channelContext;
                RequestContext = requestContext;
                Reply = reply;
            }

            /// <summary>
            /// Gets the <see cref="ChannelContext"/> instance.
            /// </summary>
            /// <value>
            /// The <see cref="ChannelContext"/> instance.
            /// </value>
            public ChannelContext ChannelContext { get; private set; }

            /// <summary>
            /// Gets the <see cref="System.ServiceModel.Channels.RequestContext"/> instance.
            /// </summary>
            /// <value>
            /// The <see cref="System.ServiceModel.Channels.RequestContext"/> instance.
            /// </value>
            public RequestContext RequestContext { get; private set; }

            /// <summary>
            /// Gets the reply <see cref="System.ServiceModel.Channels.Message"/> instance.
            /// </summary>
            /// <value>
            /// The reply <see cref="System.ServiceModel.Channels.Message"/> instance.
            /// </value>
            public Message Reply { get; private set; }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged SRResources.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Releases unmanaged and - optionally - managed resources
            /// </summary>
            /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged SRResources.</param>
            protected virtual void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // RequestContext.Close can throw if the client disconnects before it finishes receiving the response
                        // Catch here to avoid throwing in a Dispose method
                        try
                        {
                            RequestContext.Close();
                        }
                        catch
                        {
                        }

                        // HttpMessage.Close can throw if the request message throws in its Dispose implementation
                        // Catch here to avoid throwing in a Dispose method
                        try
                        {
                            Reply.Close();
                        }
                        catch
                        {
                        }
                    }

                    _disposed = true;
                }
            }
        }

        /// <summary>
        /// Used as the T in a "conversion" of a Task into a Task{T}
        /// </summary>
        private struct AsyncVoid
        {
        }
    }
}