using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Karvis.Business.Audio
{
    public static class AudioExtensions
    {
        //public static Span<TOut> Cast<TIn, TOut>(this Span<TIn> span)
        //{
        //    return MemoryMarshal.Cast<TIn, TOut>(span);
        //}

        public static Span<short> Reinterpret(this Span<byte> span)
        {
            return MemoryMarshal.Cast<byte, short>(span);
        }

        public static Span<byte> Reinterpret(this Span<short> span)
        {
            return MemoryMarshal.Cast<short, byte>(span);
        }
    }
}
