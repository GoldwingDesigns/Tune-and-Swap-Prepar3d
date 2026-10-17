using System;

namespace TuneAndSwap
{
    internal static class Bcd
    {
        /// <summary>
        /// Encodes a COM frequency (MHz) as the 16-bit BCD value that
        /// COM_STBY_RADIO_SET / COM2_STBY_RADIO_SET expect in Prepar3D v5.
        /// The leading "1" is dropped and the frequency is expressed in 10 kHz steps,
        /// one decimal digit per nibble: 118.27 MHz -> 11827 -> 1827 -> 0x1827.
        /// </summary>
        public static uint FromMHz(double mhz)
        {
            long tenKhz = (long)Math.Round(mhz * 100.0) % 10000;
            uint result = 0;
            for (int shift = 0; tenKhz > 0; shift += 4, tenKhz /= 10)
                result |= (uint)(tenKhz % 10) << shift;
            return result;
        }
    }
}
