﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Owin;
using SignalR.Hosting;

namespace SignalR.Hosting.Owin
{
    public class OwinResponse : IResponse
    {
        private ResultDelegate _responseCallback;
        private Func<ArraySegment<byte>, Action, bool> _responseNext;
        private Action<Exception> _responseError;
        private Action _responseCompete;

        public OwinResponse(ResultDelegate responseCallback)
        {
            _responseCallback = responseCallback;

            IsClientConnected = true;
        }

        public string ContentType
        {
            get;
            set;
        }

        public bool IsClientConnected
        {
            get;
            private set;
        }

        public Task WriteAsync(string data)
        {
            return WriteAsync(data, end: false);
        }

        public Task EndAsync(string data)
        {
            return WriteAsync(data, end: true);
        }

        public Task End()
        {
            return EnsureResponseStarted().Then(cb => cb(), _responseCompete);
        }

        private Task WriteAsync(string data, bool end)
        {
            return EnsureResponseStarted()
                .Then((d, e) => DoWrite(d, e), data, end);
        }

        private Task EnsureResponseStarted()
        {
            var responseCallback = Interlocked.Exchange(ref _responseCallback, null);
            if (responseCallback == null)
            {
                return TaskAsyncHelper.Empty;
            }

            var tcs = new TaskCompletionSource<object>();
            try
            {
                responseCallback(
                    "200 OK",
                    new Dictionary<string, IEnumerable<string>>
                        {
                            { "Content-Type", new[] { ContentType ?? "text/plain" } },
                        },
                    (write, flush, end, cancel) =>
                    {
                        _responseNext = (data, continuation) => write(data) && flush(continuation);
                        _responseError = ex => end(ex);
                        _responseCompete = () => end(null);
                        cancel.Register(StopSending);
                        tcs.SetResult(null);
                    });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        private void StopSending()
        {
            IsClientConnected = false;
        }

        private Task DoWrite(string data, bool end)
        {
            if (!IsClientConnected)
            {
                return TaskAsyncHelper.Empty;
            }

            var tcs = new TaskCompletionSource<object>();

            try
            {
                var value = new ArraySegment<byte>(Encoding.UTF8.GetBytes(data));
                if (end)
                {
                    // Write and send the response immediately
                    _responseNext(value, null);
                    _responseCompete();

                    tcs.SetResult(null);
                }
                else
                {
                    if (!_responseNext(value, () => tcs.SetResult(null)))
                    {
                        tcs.SetResult(null);
                    }
                }
            }
            catch (Exception ex)
            {
                // Infer client connectedness from fails on write
                IsClientConnected = false;

                // Raise the respnse error callback
                _responseError(ex);

                // Mark the task as complete
                tcs.SetResult(null);
            }

            return tcs.Task;
        }
    }
}
