// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;

namespace System.Runtime.InteropServices
{
    /// <summary>
    /// Allows limited thread safe reuse of heap buffers to limit memory pressure.
    /// 
    /// This cache does not ensure that multiple copies of handles are not released back into the cache.
    /// </summary>
    internal sealed class SafeHeapHandleCache : IDisposable
    {
        private readonly ulong _minSize;
        private readonly ulong _maxSize;

        // internal for testing
        [System.Security.SecurityCritical]
        internal readonly SafeHeapHandle[] _handleCache;

        /// <param name="minSize">Smallest buffer size to allocate in bytes.</param>
        /// <param name="maxSize">The largest buffer size to cache in bytes.</param>
        /// <param name="maxHandles">The maximum number of handles to cache.</param>
        [System.Security.SecuritySafeCritical]
        public SafeHeapHandleCache(ulong minSize = 64, ulong maxSize = 1024 * 2, int maxHandles = 0)
        {
            _minSize = minSize;
            _maxSize = maxSize;
            _handleCache = new SafeHeapHandle[maxHandles > 0 ? maxHandles : Environment.ProcessorCount * 4];
        }

        /// <summary>
        /// Get a HeapHandle
        /// </summary>
        [System.Security.SecurityCritical]
        public SafeHeapHandle Acquire(ulong minSize = 0)
        {
            if (minSize < _minSize) minSize = _minSize;

            SafeHeapHandle handle = null;

            for (int i = 0; i < _handleCache.Length; i++)
            {
                handle = Interlocked.Exchange(ref _handleCache[i], null);
                if (handle != null) break;
            }

            if (handle != null)
            {
                // One possible future consideration is to attempt cycling through to
                // find one that might already have sufficient capacity
                if (handle.ByteLength < minSize)
                    handle.Resize(minSize);
            }
            else
            {
                handle = new SafeHeapHandle(minSize);
            }

            return handle;
        }

        /// <summary>
        /// Give a HeapHandle back for potential reuse
        /// </summary>
        [System.Security.SecurityCritical]
        public void Release(SafeHeapHandle handle)
        {
            if (handle.ByteLength <= _maxSize)
            {
                for (int i = 0; i < _handleCache.Length; i++)
                {
                    // Push the handles down, walking the last one off the end to keep
                    // the top of the "stack" fresh
                    handle = Interlocked.Exchange(ref _handleCache[i], handle);
                    if (handle == null) return;
                }
            }

            handle.Dispose();
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        [System.Security.SecuritySafeCritical]
        private void Dispose(bool disposing)
        {
            if (_handleCache != null)
            {
                for(int i = 0; i < _handleCache.Length; i++)
                {
                    SafeHeapHandle handle = _handleCache[i];
                    _handleCache[i] = null;
                    if (handle != null && disposing) handle.Dispose();
                }
            }
        }

        ~SafeHeapHandleCache()
        {
            Dispose(disposing: false);
        }
    }
}
