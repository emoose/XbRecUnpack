using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace XbRecUnpack
{
    public static class Util
    {
        // 16-bit size integer followed by ASCII string
        // Sometimes has a padding byte afterward, depending on position inside file
        public static string ReadMSString(this BinaryReader reader)
        {
            ushort length = reader.ReadUInt16();
            byte[] str = reader.ReadBytes(length);

            if ((reader.BaseStream.Position & 1) == 1)
                reader.BaseStream.Position++; // pad to next power of 2

            return Encoding.ASCII.GetString(str);
        }

        public static int GetMaxPathSize()
        {
            // reflection
            FieldInfo maxPathField = typeof(Path).GetField("MaxPath",
                BindingFlags.Static |
                BindingFlags.GetField |
                BindingFlags.NonPublic);

            // invoke the field gettor, which returns 260
            return (int)maxPathField.GetValue(null);
        }

        public static string ReadNullTermASCII(this BinaryReader reader)
        {
            var list = new List<byte>();

            var byt = reader.ReadByte();
            while (byt != 0)
            {
                list.Add(byt);
                byt = reader.ReadByte();
            }

            return Encoding.ASCII.GetString(list.ToArray());
        }

        /// <summary>
        /// Reads in a block from a file and converts it to the struct
        /// type specified by the template parameter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static T ReadStruct<T>(this BinaryReader reader)
        {
            var size = Marshal.SizeOf(typeof(T));
            // Read in a byte array
            var bytes = reader.ReadBytes(size);

            return BytesToStruct<T>(bytes);
        }

        public static T BytesToStruct<T>(byte[] bytes)
        {
            // Pin the managed memory while, copy it out the data, then unpin it
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var theStructure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();

            return theStructure;
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

        public static ushort EndianSwap(this ushort num)
        {
            byte[] data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        public static uint EndianSwap(this uint num)
        {
            byte[] data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public static ulong EndianSwap(this ulong num)
        {
            byte[] data = BitConverter.GetBytes(num);
            Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }
    }
}
