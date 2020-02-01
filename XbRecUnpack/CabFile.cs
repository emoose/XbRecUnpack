using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;

// TODO: this doesn't support cabs that use multiple CFFOLDERS (if any do)
// Also only supports LZX compression, there's code to support non-compressed data too but it's untested
namespace XbRecUnpack
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CFHEADER
    {
        public uint signature;
        public uint reserved1;
        public uint cbCabinet;
        public uint reserved2;
        public uint coffFiles;
        public uint reserved3;
        public byte versionMinor;
        public byte versionMajor;
        public ushort cFolders;
        public ushort cFiles;
        public ushort flags;
        public ushort setID;
        public ushort iCabinet;

        public bool IsValid
        {
            get
            {
                return signature == 0x4643534D;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CFFOLDER
    {
        public uint coffCabStart;
        public ushort cCFData;
        public ushort typeCompress;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CFFILE
    {
        public uint cbFile;
        public uint uoffFolderStart;
        public ushort iFolder;
        public ushort date; // DOSDATE
        public ushort time; // DOSTIME
        public ushort attribs;
        // szName follows
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CFDATA
    {
        public uint csum;
        public ushort cbData;
        public ushort cbUncomp;

        // ab[cbData] follows
    }
    public class CabFile
    {
        long headerPos = 0;

        CFHEADER header;
        CFFOLDER folder;

        public List<Tuple<CFFILE, string>> Entries = new List<Tuple<CFFILE, string>>();

        BinaryReader reader;

        MemoryStream decompressed;

        long decompressedSize = 0;

        int headerReservedSize = 0;
        int folderReservedSize = 0;
        int dataReservedSize = 0;
        byte[] headerReserved;

        string szCabinetPrev = null; // (optional) name of previous cabinet file
        string szDiskPrev = null; // (optional) name of previous disk

        string szCabinetNext = null; // (optional) name of next cabinet file
        string szDiskNext = null; // (optional) name of next disk

        public CabFile(Stream cabStream)
        {
            reader = new BinaryReader(cabStream);
        }

        public bool Read()
        {
            headerPos = reader.BaseStream.Position;

            header = reader.ReadStruct<CFHEADER>();
            if (!header.IsValid)
                return false;

            if((header.flags & 4) == 4)
            {
                headerReservedSize = reader.ReadUInt16();
                folderReservedSize = reader.ReadByte();
                dataReservedSize = reader.ReadByte();

                if (headerReservedSize > 0)
                    headerReserved = reader.ReadBytes(headerReservedSize);
            }

            if((header.flags & 1) == 1)
            {
                szCabinetPrev = reader.ReadNullTermASCII();
                szDiskPrev = reader.ReadNullTermASCII();
            }

            if((header.flags & 2) == 2)
            {
                szCabinetNext = reader.ReadNullTermASCII();
                szDiskNext = reader.ReadNullTermASCII();
            }

            folder = reader.ReadStruct<CFFOLDER>();

            reader.BaseStream.Position = headerPos + header.coffFiles;
            for(int i = 0; i < header.cFiles; i++)
            {
                var file = reader.ReadStruct<CFFILE>();
                var name = reader.ReadNullTermASCII();

                Entries.Add(new Tuple<CFFILE, string>(file, name));
                decompressedSize += file.cbFile;
            }

            return true;
        }

        bool DecompressFolder()
        {
            if(decompressed != null)
                decompressed.Close();

            decompressed = new MemoryStream();

            Console.WriteLine("Decompressing cabinet into RAM...");

            var compType = (folder.typeCompress & 0xFF);
            if (compType == 3)
            {
                Console.WriteLine($"(~{Util.GetBytesReadable(header.cbCabinet)} -> ~{Util.GetBytesReadable(decompressedSize)})");

                // LZX decompress it
                var windowSize = 1 << ((folder.typeCompress >> 8) & 0x1f);
                var lzx = new LzxDecoder(windowSize, 0x8000);

                reader.BaseStream.Position = headerPos + folder.coffCabStart;

                for (int i = 0; i < folder.cCFData; i++)
                {
                    var data = reader.ReadStruct<CFDATA>();
                    if (dataReservedSize > 0)
                        reader.BaseStream.Position += dataReservedSize;

                    lzx.Decompress(reader.BaseStream, data.cbData, decompressed, data.cbUncomp);
                }
                return true;
            }
            else if(compType == 0)
            {
                reader.BaseStream.Position = headerPos + folder.coffCabStart;
                for (int i = 0; i < folder.cCFData; i++)
                {
                    var data = reader.ReadStruct<CFDATA>();
                    if (dataReservedSize > 0)
                        reader.BaseStream.Position += dataReservedSize;

                    byte[] block = reader.ReadBytes(data.cbData);
                    decompressed.Write(block, 0, block.Length);
                }
                return true;
            }

            Console.WriteLine($"Error: cabinet uses unsupported compression type {compType}!");
            return false;
        }

        public bool GetEntry(string fileName, ref CFFILE entry, bool caseSensitive = true)
        {
            foreach (var pair in Entries)
            {
                if (caseSensitive)
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

        public Stream OpenFile(CFFILE entry)
        {
            if(decompressed == null)
            {
                if (!DecompressFolder())
                    return null;
            }

            return new WindowedStream(decompressed, entry.uoffFolderStart, entry.cbFile);
        }

        public void Close()
        {
            if (decompressed != null)
                decompressed.Close();
        }
    }
}
