// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    /// <summary>
    /// Wrapper for access to the native heap. Dispose to free the memory. Try to use with using statements.
    /// Does not allocate zero size buffers, and will free the existing native buffer if capacity is dropped to zero.
    /// 
    /// NativeBuffer utilizes a cache of heap buffers.
    /// </summary>
    /// <remarks>
    /// Suggested use through P/Invoke: define DllImport arguments that take a byte buffer as SafeHandle.
    /// 
    /// Using SafeHandle will ensure that the buffer will not get collected during a P/Invoke.
    /// (Notably AddRef and ReleaseRef will be called by the interop layer.)
    /// 
    /// This class is not threadsafe, changing the capacity or disposing on multiple threads risks duplicate heap
    /// handles or worse.
    /// </remarks>
    internal class NativeBuffer : IDisposable
    {
        private readonly static SafeHeapHandleCache s_handleCache;
        [System.Security.SecurityCritical]
        private readonly static SafeHandle s_emptyHandle;
        [System.Security.SecurityCritical]
        private SafeHeapHandle _handle;
        private ulong _capacity;

        [System.Security.SecuritySafeCritical]
        static NativeBuffer()
        {
            s_emptyHandle = new EmptySafeHandle();
            s_handleCache = new SafeHeapHandleCache();
        }

        /// <summary>
        /// Create a buffer with at least the specified initial capacity in bytes.
        /// </summary>
        public NativeBuffer(ulong initialMinCapacity = 0)
        {
            EnsureByteCapacity(initialMinCapacity);
        }

        protected unsafe void* VoidPointer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            [System.Security.SecurityCritical]
            get
            {
                return _handle == null ? null : _handle.DangerousGetHandle().ToPointer();
            }
        }

        protected unsafe byte* BytePointer
        {
            [System.Security.SecurityCritical]
            get
            {
                return (byte*)VoidPointer;
            }
        }

        /// <summary>
        /// Get the handle for the buffer.
        /// </summary>
        [System.Security.SecuritySafeCritical]
        public SafeHandle GetHandle()
        {
            // Marshalling code will throw on null for SafeHandle
            return _handle ?? s_emptyHandle;
        }

        /// <summary>
        /// The capacity of the buffer in bytes.
        /// </summary>
        public ulong ByteCapacity
        {
            get { return _capacity; }
        }

        /// <summary>
        /// Ensure capacity in bytes is at least the given minimum.
        /// </summary>
        /// <exception cref="OutOfMemoryException">Thrown if unable to allocate memory when setting.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if attempting to set <paramref name="nameof(minCapacity)"/> to a value that is larger than the maximum addressable memory.</exception>
        [System.Security.SecuritySafeCritical]
        public void EnsureByteCapacity(ulong minCapacity)
        {
            if (_capacity < minCapacity)
            {
                Resize(minCapacity);
                _capacity = minCapacity;
            }
        }

        public unsafe byte this[ulong index]
        {
            [System.Security.SecuritySafeCritical]
            get
            {
                if (index >= _capacity) throw new ArgumentOutOfRangeException();
                return BytePointer[index];
            }
            [System.Security.SecuritySafeCritical]
            set
            {
                if (index >= _capacity) throw new ArgumentOutOfRangeException();
                BytePointer[index] = value;
            }
        }

        [System.Security.SecuritySafeCritical]
        private unsafe void Resize(ulong byteLength)
        {
            if (byteLength == 0)
            {
                ReleaseHandle();
                return;
            }

            if (_handle == null)
            {
                _handle = s_handleCache.Acquire(byteLength);
            }
            else
            {
                _handle.Resize(byteLength);
            }
        }

        [System.Security.SecuritySafeCritical]
        private void ReleaseHandle()
        {
            if (_handle != null)
            {
                s_handleCache.Release(_handle);
                _capacity = 0;
                _handle = null;
            }
        }

        /// <summary>
        /// Release the backing buffer
        /// </summary>
        [System.Security.SecuritySafeCritical]
        public virtual void Free()
        {
            ReleaseHandle();
        }

        [System.Security.SecuritySafeCritical]
        public void Dispose()
        {
            Free();
        }

        [System.Security.SecurityCritical]
        private sealed class EmptySafeHandle : SafeHandle
        {
            public EmptySafeHandle() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid
            {
                [System.Security.SecurityCritical]
                get
                { return true; }
            }

            [System.Security.SecurityCritical]
            protected override bool ReleaseHandle()
            {
                return true;
            }
        }
    }
}
