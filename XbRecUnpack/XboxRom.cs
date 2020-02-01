using System;
using System.IO;
using System.Runtime.InteropServices;
namespace XbRecUnpack
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BLDR
    {
        public ushort Magic;
        public ushort Build;
        public ushort Qfe;
        public ushort Flags;
        public uint Entry;
        public uint Size;

        public void EndianSwap()
        {
            Magic = Magic.EndianSwap();
            Build = Build.EndianSwap();
            Qfe = Qfe.EndianSwap();
            Flags = Flags.EndianSwap();
            Entry = Entry.EndianSwap();
            Size = Size.EndianSwap();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct BLDR_FLASH
    {
        public BLDR Header;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] achCopyright;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] abReserved;
        public uint dwKeyVaultSize;
        public uint dwSysUpdateAddr;
        public ushort wSysUpdateCount;
        public ushort wKeyVaultVersion;
        public uint dwKeyVaultAddr;
        public uint dwFileSystemAddr;
        public uint dwSmcConfigAddr;
        public uint dwSmcBootSize;
        public uint dwSmcBootAddr;

        public void EndianSwap()
        {
            Header.EndianSwap();

            dwKeyVaultSize = dwKeyVaultSize.EndianSwap();
            dwSysUpdateAddr = dwSysUpdateAddr.EndianSwap();
            wSysUpdateCount = wSysUpdateCount.EndianSwap();
            wKeyVaultVersion = wKeyVaultVersion.EndianSwap();
            dwKeyVaultAddr = dwKeyVaultAddr.EndianSwap();
            dwFileSystemAddr = dwFileSystemAddr.EndianSwap();
            dwSmcConfigAddr = dwSmcConfigAddr.EndianSwap();
            dwSmcBootSize = dwSmcBootSize.EndianSwap();
            dwSmcBootAddr = dwSmcBootAddr.EndianSwap();
        }
    }

    class XboxRomHeader
    {
        public BLDR_FLASH FlashHeader;
        public BLDR NextHeader; // BLDR_KDNET or BLDR_2BL in pre-release
        public byte[] SmcData;
        public ushort SmcBuild;
        public string MotherboardType;
        public int MotherboardTypeInt;
        public int MotherboardTypeRevision;

        static string[] kMotherboardTypes = {
            "none/unk",
            "xenon", // -w
            "zephyr", // -z
            "falcon", // -f
            "jasper", // -j
            "trinity",  // -t
            "corona",  // -c
            "winchester", // -wi
            "unknown0x8", // ? unreleased ?
            "ridgeway", // -r ? datacenter?, kernel detects this based on Corona + XBOX_HW_FLAG_DATA_CENTER_MODE (0x2) ?
        };

        public int Version
        {
            get
            {
                return FlashHeader.Header.Build > 0 ? FlashHeader.Header.Build : NextHeader.Build;
            }
        }

        public void Read(BinaryReader reader)
        {
            var headerPos = reader.BaseStream.Position;

            FlashHeader = reader.ReadStruct<BLDR_FLASH>();
            FlashHeader.EndianSwap();

            NextHeader = reader.ReadStruct<BLDR>();
            NextHeader.EndianSwap();

            if(FlashHeader.dwSmcBootAddr != 0)
            {
                reader.BaseStream.Position = headerPos + FlashHeader.dwSmcBootAddr;
                SmcData = reader.ReadBytes((int)FlashHeader.dwSmcBootSize);

                UnmungeSmc();

                MotherboardTypeInt = ((SmcData[0x100] >> 4) & 0xF) % kMotherboardTypes.Length;
                MotherboardTypeRevision = SmcData[0x100] & 0xF;
                MotherboardType = $"0x{SmcData[0x100]:X}: {kMotherboardTypes[MotherboardTypeInt]}-r{MotherboardTypeRevision}";

                byte[] smcver = new byte[] { SmcData[0x102], SmcData[0x101] };
                SmcBuild = BitConverter.ToUInt16(smcver, 0);
            }
        }

        void UnmungeSmc()
        {
            byte[] key = new byte[] { 0x42, 0x75, 0x4e, 0x79 };
            for (int i = 0; i < SmcData.Length; i++)
            {
                byte j = SmcData[i];
                int mod = j * 0xFB;
                byte decrypted = (byte)(j ^ (key[i & 3] & 0xFF));
                SmcData[i] = decrypted;
                key[(i + 1) & 3] += (byte)mod;
                key[(i + 2) & 3] += (byte)(mod >> 8);
            }
        }
    }
}
