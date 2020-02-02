using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XbRecUnpack
{
    struct ManifestEntry
    {
        public string LangID; // usually 0000, sometimes 0409 or 0411
        public string Variant;
        public string Action;
        public string BasePath;
        public string FilePath;
        public string CopyDestPath;

        public string VariantAndLang
        {
            get
            {
                if (LangID == "0000")
                    return Variant;
                return $"{Variant}.{LangID}";
            }
        }
    }
    class RemoteRecovery
    {
        readonly BinaryReader reader;
        List<ManifestEntry> Entries;
        List<long> cabHeaderPos;

        List<string> Variants = new List<string>();

        static DateTime DosToDateTime(ushort date, ushort time)
        {
            var year = (date >> 9) + 1980;
            var month = (date & 0x01e0) >> 5;
            var day = date & 0x1F;
            return new DateTime(year, month, day)
                .AddHours(time >> 11).AddMinutes((time >> 5) & 0x3f).AddSeconds((time & 0x1f) * 2);
        }

        public RemoteRecovery(Stream exeStream)
        {
            reader = new BinaryReader(exeStream);
        }

        public bool Read()
        {
            reader.BaseStream.Position = 0;
            var magic = reader.ReadUInt16();
            if (magic != 0x5a4d && magic != 0x4d5a)
            {
                Console.WriteLine("Error: recovery isn't proper EXE file!");
                return false;
            }

            Console.WriteLine("Scanning EXE...");
            cabHeaderPos = new List<long>();

            // Track position/length ourselves instead of needing to call stream accessors
            long position = reader.BaseStream.Position;
            long length = reader.BaseStream.Length;

            // Fast pattern search via sliding-window, kinda
            // From https://codereview.stackexchange.com/questions/202235/finding-specific-small-byte-arrays-in-large-binary-files
            ulong pattern = ((ulong)0x4643534D).EndianSwap();
            ulong view = 0;
            long viewed = 0;

            while (position + 8 < length)
            {
                view = (view << 8) | reader.ReadByte(); // shift-in next byte
                position++;
                viewed++;
                if (view == pattern && viewed >= 8) // make sure we already got at least 4 bytes
                    cabHeaderPos.Add(position - 8);
            }

            if (cabHeaderPos.Count < 2)
            {
                Console.WriteLine("Error: couldn't find required CAB files inside recovery!");
                return false;
            }

            string[] csv = null;
            // Read the second cab in the file, contains some meta info about the other ones
            while (cabHeaderPos.Count > 1)
            {
                reader.BaseStream.Position = cabHeaderPos[1];

                // Remove the meta cab from cab header list...
                cabHeaderPos.RemoveAt(1);

                // Try reading metacab
                var metaCab = new CabFile(reader.BaseStream);
                if (!metaCab.Read())
                    continue;

                // Read the manifest file...
                var manifest = new CFFILE();
                if (!metaCab.GetEntry("manifest.csv", ref manifest, false))
                    continue;

                Stream manifestStream = null;
                try
                {
                    manifestStream = metaCab.OpenFile(manifest);
                    if (manifestStream == null)
                        continue;
                }
                catch
                {
                    continue;
                }

                using (var reader2 = new BinaryReader(manifestStream))
                {
                    byte[] data = reader2.ReadBytes((int)reader2.BaseStream.Length);
                    var str = Encoding.ASCII.GetString(data);
                    csv = str.Replace("\r\n", "\n").Split(new char[] { '\n' });
                }
                break;
            }

            if (csv == null)
            {
                Console.WriteLine("Error: failed to read manifest.csv from meta-cab section!");
                return false;
            }

            Variants = new List<string>();
            Entries = new List<ManifestEntry>();
            foreach (var line in csv)
            {
                if (string.IsNullOrEmpty(line))
                    continue;

                var parts = new List<string>(line.Split(new char[] { ',' }));
                if (parts.Count < 6)
                    continue;

                // Older SDKs don't have a variant section, so we'll insert one
                if (parts[1] == "file" || parts[1] == "copy" || parts[1] == "sharedfile" || parts[1] == "backupsharedfile" || parts[1] == "singlefile")
                    parts.Insert(1, "");

                if (parts[2] != "file" && parts[2] != "copy" && parts[2] != "sharedfile" && parts[2] != "backupsharedfile" && parts[2] != "singlefile")
                    continue;

                var entry = new ManifestEntry();
                entry.LangID = parts[0];
                entry.Variant = parts[1];
                entry.Action = parts[2];
                entry.BasePath = parts[3];
                entry.FilePath = parts[4];
                entry.CopyDestPath = parts[5];

                if (string.IsNullOrEmpty(entry.Variant))
                    entry.Variant = "_All";

                Entries.Add(entry);

                if (!Variants.Contains(entry.Variant))
                    Variants.Add(entry.Variant);
            }

            Console.WriteLine();
            Console.WriteLine($"EXE contents:");
            Console.WriteLine($"{Entries.Count} file{(Entries.Count == 1 ? "" : "s")}");
            Console.WriteLine($"{Variants.Count} variant{(Variants.Count == 1 ? "" : "s")}:");
            foreach (var variant in Variants)
            {
                int numFiles = 0;
                foreach (var entry in Entries)
                    if (entry.Variant == variant)
                        numFiles++;

                Console.WriteLine($"  - {variant} ({numFiles} file{(numFiles == 1 ? "" : "s")})");
            }

            Console.WriteLine();

            // TODO: devices (need to read settings.ini from metaCab)

            return true;
        }

        public bool Extract(string destDirPath, bool listOnly = false)
        {
            if (Entries == null || cabHeaderPos == null || cabHeaderPos.Count <= 0 || Entries.Count <= 0)
                return false;

            // Read the main cab
            int curCab = 0;
            reader.BaseStream.Position = cabHeaderPos[curCab];
            CabFile mainCab = new CabFile(reader.BaseStream);
            if (!mainCab.Read())
                return false;

            int cabIndex = 0;
            int totalIndex = 1;
            foreach (var entry in Entries)
            {
                var variantPath = Path.Combine(entry.VariantAndLang, entry.FilePath);
                var entryPath = Path.Combine(destDirPath, variantPath);

                if (entry.Action == "file" || entry.Action == "sharedfile" || entry.Action == "backupsharedfile" || entry.Action == "singlefile")
                {
                    if (cabIndex >= mainCab.Entries.Count)
                    {
                        // We've finished this cab, try loading the next one
                        curCab++;
                        cabIndex = 0;
                        if (curCab >= cabHeaderPos.Count)
                        {
                            Console.WriteLine("Error: couldn't find next cab file!");
                            break;
                        }

                        mainCab.Close();

                        reader.BaseStream.Position = cabHeaderPos[curCab];
                        mainCab = new CabFile(reader.BaseStream);
                        if (!mainCab.Read())
                        {
                            Console.WriteLine($"Error: failed to read CAB at offset 0x{cabHeaderPos[curCab]:X}");
                            break;
                        }
                    }

                    var cfEntry = mainCab.Entries[cabIndex];

                    if (cfEntry.Item2.ToLower() != entry.FilePath.ToLower())
                    {
                        Console.WriteLine("Warning: mismatch between manifest entry and cab entry!");
                    }

                    if (listOnly)
                        Console.WriteLine($"({totalIndex}/{Entries.Count}) {variantPath} ({Util.GetBytesReadable(cfEntry.Item1.cbFile)})");
                    else
                    {
                        var srcStream = mainCab.OpenFile(cfEntry.Item1);
                        if (srcStream == null)
                            return false;

                        if (!listOnly)
                        {
                            var destDir = Path.GetDirectoryName(entryPath);
                            if (!Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);
                        }

                        using (var destStream = File.Create(entryPath))
                        {
                            Console.WriteLine($"({totalIndex}/{Entries.Count}) {variantPath} ({Util.GetBytesReadable(cfEntry.Item1.cbFile)})");

                            long sizeRemain = cfEntry.Item1.cbFile;
                            byte[] buffer = new byte[32768];
                            while (sizeRemain > 0)
                            {
                                int read = (int)Math.Min(buffer.Length, sizeRemain);
                                srcStream.Read(buffer, 0, read);
                                destStream.Write(buffer, 0, read);
                                sizeRemain -= read;
                            }
                        }

                        File.SetLastWriteTime(entryPath, DosToDateTime(cfEntry.Item1.date, cfEntry.Item1.time));
                    }

                    cabIndex++;
                }
                else if (entry.Action == "copy")
                {
                    variantPath = Path.Combine(entry.Variant, entry.CopyDestPath);
                    string destPath = "";
                    if (!listOnly)
                    {
                        destPath = Path.Combine(destDirPath, variantPath);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);
                    }

                    Console.WriteLine($"({totalIndex}/{Entries.Count}) {variantPath} (copy)");

                    if (!listOnly)
                    {
                        if (File.Exists(destPath))
                            File.Delete(destPath);

                        File.Copy(entryPath, destPath);
                        File.SetLastWriteTime(destPath, File.GetLastWriteTime(entryPath));
                    }
                }

                totalIndex++;
            }

            if (mainCab.Entries.Count > cabIndex)
            {
                var diff = mainCab.Entries.Count - cabIndex;
                Console.WriteLine($"Note: CAB contains {diff} more file{(diff == 1 ? "" : "s")} than were inside the manifest!");
            }

            return true;
        }
    }
}
