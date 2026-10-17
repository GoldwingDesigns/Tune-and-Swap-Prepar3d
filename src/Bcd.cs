using System;

namespace TuneAndSwap
{
    internal static class Bcd
    {
        /// <summary>
        /// Encodes a COM frequency (MHz) as BCD Hz for COM_STBY_RADIO_SET.
        /// 118.275 MHz = 118275000 Hz; the 9 digits don't fit a 32-bit BCD,
        /// so the leading "1" is dropped: 0x18275000.
        /// </summary>
        public static uint FromMHz(double mhz)
        {
            long hz = (long)Math.Round(mhz * 1000000.0);
            hz %= 100000000; // drop the leading 1 (or any hundreds-of-MHz digit)
            uint result = 0;
            for (int shift = 0; hz > 0; shift += 4, hz /= 10)
                result |= (uint)(hz % 10) << shift;
            return result;
        }

        /// <summary>
        /// Legacy 16-bit BCD (10 kHz resolution, leading 1 dropped): 118.275 -> 0x1827.
        /// </summary>
        public static uint FromMHzLegacy16(double mhz)
        {
            long tenKhz = (long)Math.Round(mhz * 100.0) % 10000;
            uint result = 0;
            for (int shift = 0; tenKhz > 0; shift += 4, tenKhz /= 10)
                result |= (uint)(tenKhz % 10) << shift;
            return result;
        }
    }
}
