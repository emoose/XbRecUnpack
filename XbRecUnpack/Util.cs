using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace XbRecUnpack
{
    public static class Util
    {
        // 16-bit size integer followed by ASCII string
        // Sometimes has a padding byte afterward, depending on position inside file
        public static string ReadMSString(this BinaryReader reader)
        {
            ushort length = reader.ReadUInt16();
            byte[] str = reader.ReadBytes((int)length);

            if ((reader.BaseStream.Position & 1) == 1)
                reader.BaseStream.Position++; // pad to next power of 2

            return Encoding.ASCII.GetString(str);
        }

        // "C# Human Readable File Size Optimized Function" from https://www.somacon.com/p576.php
        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.### XB", e.g. "4.2 KB" or "1.434 GB"
        public static string GetBytesReadable(long i)
        {
            // Get absolute value
            long absolute_i = (i < 0 ? -i : i);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absolute_i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (i >> 50);
            }
            else if (absolute_i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (i >> 40);
            }
            else if (absolute_i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (i >> 30);
            }
            else if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return i.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable = (readable / 1024);
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }
    }
}
