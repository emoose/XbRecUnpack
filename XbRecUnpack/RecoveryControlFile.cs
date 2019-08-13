using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace XbRecUnpack
{
    public class RecoveryControlEntry
    {
        private RecoveryControlFile _baseFile; // ControlFile instance so we can retrieve version/device arrays
        public long DataOffset; // DataOffset isn't stored inside entry, so we have to track this ourselves

        // All following fields are stored in the entry itself, in little-endian format
        public ushort VersionIndex;
        public ushort DeviceIndex;
        public uint DecompressedSize;
        public ulong FileTime;
        public string FilePath;

        // Array of LZX compression-block sizes, all should decompress to a 0x8000 byte block
        public List<ushort> LzxBlocks;

        // Final LZX block sizes - decompressed & compressed sizes
        public ushort FinalBlockDecSize;
        public ushort FinalBlockCompSize;

        // Calculate compressed size by tallying up the LZX block sizes
        public int CompressedSize
        {
            get
            {
                int ret = FinalBlockCompSize;
                foreach (var i in LzxBlocks)
                    ret += i;
                return ret;
            }
        }

        public RecoveryControlEntry(RecoveryControlFile baseFile)
        {
            _baseFile = baseFile;
        }

        public void Read(BinaryReader reader)
        {
            // Index of version number from recctrl header
            // >= 1 is a verison number from header, 0 seems to mean all versions?
            // (we solve this by adding an "All" version before reading the versions from the header, works great)
            VersionIndex = reader.ReadUInt16();

            // Index of device name/path pair from recctrl header
            DeviceIndex = reader.ReadUInt16();

            // Size of decompressed file, raw decompressed data is usually a bit larger
            // So we need this so we can trim it down to the proper size
            DecompressedSize = reader.ReadUInt32();

            // FILETIME timestamp of the file, we should probably convert this...
            FileTime = reader.ReadUInt64();

            // Path of this file
            FilePath = reader.ReadMSString();

            // List of LZX block sizes, list terminated with 0-sized block
            LzxBlocks = new List<ushort>();
            ushort curBlock = reader.ReadUInt16();
            while (curBlock != 0)
            {
                LzxBlocks.Add(curBlock);
                curBlock = reader.ReadUInt16();
            }

            // Final LZX block uncompressed/compressed size
            FinalBlockDecSize = reader.ReadUInt16();
            FinalBlockCompSize = reader.ReadUInt16();
        }

        // Gets a path for this file based on the version & device indexes
        // LongPath uses the device path instead of the device name
        public string GetLongPath()
        {
            if (_baseFile == null)
                return FilePath;

            var version = _baseFile.Versions[VersionIndex];
            var device = _baseFile.Devices[DeviceIndex].Value;
            if (!device.StartsWith("\\"))
                device = "\\" + device;

            return version + device + "\\" + FilePath;
        }

        // Gets a path for this file based on the version & device indexes
        // ShortPath uses the device name instead of the path, which is usually a lot shorter
        public string GetShortPath()
        {
            if (_baseFile == null)
                return FilePath;

            var version = _baseFile.Versions[VersionIndex];
            var device = _baseFile.Devices[DeviceIndex].Key;
            if (!device.StartsWith("\\"))
                device = "\\" + device;

            return version + device + "\\" + FilePath;
        }

        // ToString: returns the LongPath if we can
        public override string ToString()
        {
            if (_baseFile == null)
                return base.ToString();

            return GetLongPath();
        }

        // Tries extracting file from the recdata stream
        public void Extract(Stream dataStream, Stream outputStream)
        {
            var lzx = new LzxDecoder(_baseFile.WindowSize, 0x8000);

            dataStream.Position = DataOffset;
            long outStreamPosition = outputStream.Position;

            foreach (var blockSize in LzxBlocks)
                lzx.Decompress(dataStream, blockSize, outputStream, 0x8000);

            lzx.Decompress(dataStream, FinalBlockCompSize, outputStream, FinalBlockDecSize);

            // Trim file to size in the entry header
            outputStream.SetLength(outStreamPosition + DecompressedSize);
        }
    }

    public class RecoveryControlFile
    {
        public List<string> Versions;
        public List<KeyValuePair<string, string>> Devices;
        public int WindowSize;
        public List<RecoveryControlEntry> Entries;

        public void Read(BinaryReader reader)
        {
            // versions list is kinda weird, the count is always +1 of the actual number
            // some files seem to refer to the +1 as their version index too (as 0), so make sure we include it
            ushort num_versions = reader.ReadUInt16();
            Versions = new List<string>();
            Versions.Add("_All");

            for (int i = 0; i < num_versions - 1; i++)
                Versions.Add(reader.ReadMSString());

            // Read in device name/path pairs
            ushort num_devices = reader.ReadUInt16();
            Devices = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < num_devices; i++)
            {
                var device_name = reader.ReadMSString();
                var device_path = reader.ReadMSString();
                Devices.Add(new KeyValuePair<string, string>(device_name, device_path));
            }

            // LZX window size, usually 0x100000 (_NOT_ 0x10000, this misread led to hours of debugging...)
            WindowSize = reader.ReadInt32();

            // Read in each ControlEntry till EOF
            Entries = new List<RecoveryControlEntry>();
            long dataOffset = 0;
            while (reader.BaseStream.Length > reader.BaseStream.Position)
            {
                var entry = new RecoveryControlEntry(this);
                entry.DataOffset = dataOffset;
                entry.Read(reader);
                Entries.Add(entry);
                dataOffset += entry.CompressedSize;
            }
        }
    }
}
