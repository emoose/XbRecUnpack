using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;

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

            folder = reader.ReadStruct<CFFOLDER>();

            reader.BaseStream.Position = headerPos + header.coffFiles;
            for(int i = 0; i < header.cFiles; i++)
            {
                var file = reader.ReadStruct<CFFILE>();
                var name = reader.ReadNullTermASCII();

                Entries.Add(new Tuple<CFFILE, string>(file, name));
            }

            return true;
        }

        bool DecompressFolder(Stream outputStream)
        {
            var compType = (folder.typeCompress & 0xFF);
            if (compType == 3)
            {
                var windowSize = 1 << ((folder.typeCompress >> 8) & 0x1f);
                var lzx = new LzxDecoder(windowSize, 0x8000);

                reader.BaseStream.Position = headerPos + folder.coffCabStart;

                for (int i = 0; i < folder.cCFData; i++)
                {
                    var data = reader.ReadStruct<CFDATA>();
                    lzx.Decompress(reader.BaseStream, data.cbData, outputStream, data.cbUncomp);
                }
                return true;
            }
            else if(compType == 0)
            {
                reader.BaseStream.Position = headerPos + folder.coffCabStart;
                for (int i = 0; i < folder.cCFData; i++)
                {
                    var data = reader.ReadStruct<CFDATA>();
                    byte[] block = reader.ReadBytes(data.cbData);
                    outputStream.Write(block, 0, block.Length);
                }
                return true;
            }

            Console.WriteLine($"Error: Cabinet uses unsupported compression type {compType}!");
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
                Console.WriteLine("Decompressing cabinet into RAM...");
                decompressed = new MemoryStream();
                if (!DecompressFolder(decompressed))
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
