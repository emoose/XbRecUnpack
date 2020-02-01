using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

// NOTE: Only tested with Xbox OG recovery ISOs, games & X360 ISOs likely won't work well with it
namespace XbRecUnpack
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct GdfHeader
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Magic;

        public uint RootSector;
        public uint RootSize;
        public ulong TimeStamp;
        public uint Version;

        public bool IsValid
        {
            get
            {
                return Magic.SequenceEqual(Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA"));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct GdfEntry
    {
        public ushort LeftEntryIndex;
        public ushort RightEntryIndex;
        public uint FirstSector;
        public uint FileSize;
        public byte FileAttributes;
        public byte FileNameLength;
        // name follows
    }

    public class XboxGDFImage
    {
        const int kSectorSize = 0x800;

        public GdfHeader VolumeDescriptor;

        BinaryReader reader;

        public List<Tuple<GdfEntry, string>> Entries = new List<Tuple<GdfEntry, string>>();

        public XboxGDFImage(Stream imageStream)
        {
            reader = new BinaryReader(imageStream);
        }

        static long SectorToOffset(long sector)
        {
            return sector * (long)kSectorSize;
        }

        public bool Read()
        {
            reader.BaseStream.Position = 0x8000;
            VolumeDescriptor = reader.ReadStruct<GdfHeader>();
            if(!VolumeDescriptor.IsValid)
            {
                reader.BaseStream.Position = 0x10000;
                VolumeDescriptor = reader.ReadStruct<GdfHeader>();
                if (!VolumeDescriptor.IsValid)
                    return false;
            }

            long directoryOffset = SectorToOffset(VolumeDescriptor.RootSector);
            reader.BaseStream.Position = directoryOffset;
            while (reader.BaseStream.Position < directoryOffset + VolumeDescriptor.RootSize)
            {
                var entry = reader.ReadStruct<GdfEntry>();
                if (entry.LeftEntryIndex == 0xFFFF && entry.RightEntryIndex == 0xFFFF)
                    break; // End of Directory

                var filename = Encoding.ASCII.GetString(reader.ReadBytes(entry.FileNameLength));

                // Align to 4 bytes
                reader.BaseStream.Position += (4 - (reader.BaseStream.Position & 3));

                Entries.Add(new Tuple<GdfEntry, string>(entry, filename));
            }

            return true;
        }

        public bool GetEntry(string fileName, ref GdfEntry entry, bool caseSensitive = true)
        {
            foreach(var pair in Entries)
            {
                if(caseSensitive)
                {
                    if (fileName == pair.Item2)
                    {
                        entry = pair.Item1;
                        return true;
                    }
                }
                else
                {
                    if (fileName.ToLower() == pair.Item2.ToLower())
                    {
                        entry = pair.Item1;
                        return true;
                    }
                }
            }

            return false;
        }

        public Stream OpenFile(GdfEntry entry)
        {
            return new WindowedStream(reader.BaseStream, SectorToOffset(entry.FirstSector), entry.FileSize);
        }
    }

    public class WindowedStream : Stream
    {
        private Stream source;

        private long sourceOffset;

        private long length = -1;

        private long position = 0;

        public Stream Source => source;

        public long Offset => sourceOffset;

        public override bool CanRead => source.CanRead;

        public override bool CanWrite => source.CanWrite;

        public override bool CanSeek => source.CanSeek;

        public override long Length => length;

        public override long Position
        {
            get
            {
                return position;
            }
            set
            {
                this.source.Position = value + sourceOffset;
                UpdatePosition();
            }
        }

        void EnsureSourcePosition()
        {
            this.source.Position = sourceOffset + position;
        }

        void UpdatePosition()
        {
            position = this.source.Position - sourceOffset;
        }

        public WindowedStream(Stream source, long offset, long length = -1)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            this.source = source;
            sourceOffset = offset;
            this.length = checked(source.Length - sourceOffset);
            if (length != -1 && this.length > length)
                this.length = length;

            this.source.Seek(sourceOffset, SeekOrigin.Begin);
            UpdatePosition();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureSourcePosition();
            var res = source.Read(buffer, offset, count);
            UpdatePosition();
            return res;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureSourcePosition();
            source.Write(buffer, offset, count);
            UpdatePosition();
        }

        public override int ReadByte()
        {
            EnsureSourcePosition();
            var res = source.ReadByte();
            UpdatePosition();
            return res;
        }

        public override void WriteByte(byte value)
        {
            EnsureSourcePosition();
            source.WriteByte(value);
            UpdatePosition();
        }

        public override void Flush()
        {
            source.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var res = checked(source.Seek(offset + ((origin == SeekOrigin.Begin) ? sourceOffset : 0), origin) - sourceOffset);
            UpdatePosition();
            return res;
        }

        public override void SetLength(long value)
        {
            source.SetLength(checked(value + sourceOffset));
            this.length = value;
        }

        public override void Close()
        {
            source.Close();
        }
    }
}
