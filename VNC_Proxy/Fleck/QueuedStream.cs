using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Fleck
{
    /// <summary>
    /// Wraps a stream and queues multiple write operations.
    /// Useful for wrapping SslStream as it does not support multiple simultaneous write operations.
    /// </summary>
    public class QueuedStream : Stream
    {
        private Stream _stream;
        private Queue<WriteData> _queue = new Queue<WriteData>();
        private int _pendingWrite = 0;
        private bool _disposed = false;

        public QueuedStream(Stream stream)
        {
            _stream = stream;
        }

        public override bool CanRead
        {
            get { return _stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _stream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _stream.CanWrite; }
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override long Length
        {
            get { return _stream.Length; }
        }

        public override long Position
        {
            get
            {
                return _stream.Position;
            }
            set
            {
                _stream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("QueuedStream does not support synchronous write operations yet.");
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return _stream.BeginRead(buffer, offset, count, callback, state);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            lock (_queue)
            {
                var data = new WriteData(buffer, offset, count, callback, state);
                if (_pendingWrite > 0)
                {
                    _queue.Enqueue(data);
                    return data.AsyncResult;
                }
                else
                {
                    return BeginWriteInternal(buffer, offset, count, callback, state, data);
                }
            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return _stream.EndRead(asyncResult);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            if (asyncResult is QueuedWriteResult)
            {
                var queuedResult = asyncResult as QueuedWriteResult;
                if (queuedResult.Exception != null) throw queuedResult.Exception;
                var ar = queuedResult.ActualResult;
                if (ar == null)
                {
                    throw new NotSupportedException("QueuedStream does not support synchronous write operations. Please wait for callback to be invoked before calling EndWrite.");
                }
                // EndWrite on actual stream should already be invoked. 
            }
            else
            {
                throw new ArgumentException();
            }
        }

        public override void Close()
        {
            _stream.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _stream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private IAsyncResult BeginWriteInternal(byte[] buffer, int offset, int count, AsyncCallback callback, object state, WriteData queued)
        {
            _pendingWrite++;
            var result = _stream.BeginWrite(buffer, offset, count, ar =>
            {
                // callback can be executed even before return value of BeginWriteInternal is set to this property
                queued.AsyncResult.ActualResult = ar;
                try
                {
                    // so that we can call BeginWrite again
                    _stream.EndWrite(ar);
                }
                catch (Exception exc)
                {
                    queued.AsyncResult.Exception = exc;
                }

                // one down, another is good to go
                lock (_queue)
                {
                    _pendingWrite--;
                    while (_queue.Count > 0)
                    {
                        var data = _queue.Dequeue();
                        try
                        {
                            data.AsyncResult.ActualResult = BeginWriteInternal(data.Buffer, data.Offset, data.Count, data.Callback, data.State, data);
                            break;
                        }
                        catch (Exception exc)
                        {
                            _pendingWrite--;
                            data.AsyncResult.Exception = exc;
                            callback(data.AsyncResult);
                            return;
                        }
                    }
                    callback(queued.AsyncResult);
                }

            }, state);
            return result;
        }

        private class WriteData
        {
            public readonly byte[] Buffer;
            public readonly int Offset;
            public readonly int Count;
            public readonly AsyncCallback Callback;
            public readonly object State;
            public readonly QueuedWriteResult AsyncResult;

            public WriteData(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            {
                Buffer = buffer;
                Offset = offset;
                Count = count;
                Callback = callback;
                State = state;
                AsyncResult = new QueuedWriteResult(state);
            }
        }

        private class QueuedWriteResult : IAsyncResult
        {
            private object _state;

            public QueuedWriteResult(object state)
            {
                _state = state;
            }

            public Exception Exception { get; set; }

            public IAsyncResult ActualResult { get; set; }

            public object AsyncState
            {
                get { return _state; }
            }

            public WaitHandle AsyncWaitHandle
            {
                get
                {
                    throw new NotSupportedException("Queued write operations does not support wait handle.");
                }
            }

            public bool CompletedSynchronously
            {
                get { return false; }
            }

            public bool IsCompleted
            {
                get { return ActualResult == null ? false : ActualResult.IsCompleted; }
            }
        }
    }
}
