/* XbRecUnpack - tool for extracting Xbox/Xbox360 SDKs & recoveries
 * by emoose
 */

using DiscUtils.Iso9660;
using DiscUtils.Udf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace XbRecUnpack
{
    class Program
    {
        static string outputPath;

        // Versions & Mobos supported by the input recovery
        static readonly List<int> Versions = new List<int>();
        static readonly Dictionary<string, List<int>> Motherboards = new Dictionary<string, List<int>>();
        static readonly List<string> XboxOGMotherboards = new List<string>();

        [STAThread]
        static void Main(string[] args)
        {
            bool extractFiles = true;
            bool printRomInfo = false;

            Console.WriteLine("XbRecUnpack - tool for extracting Xbox/Xbox360 SDKs & recoveries");
            Console.WriteLine("v3.456 by emoose");
            Console.WriteLine();
            {
                var maxPathSize = Util.GetMaxPathSize();
                if (maxPathSize < 300)
                {
                    Console.WriteLine($"Warning: system max path size limit is {maxPathSize} characters.");
                    Console.WriteLine($"You may run into problems extracting files with long paths!");
                    Console.WriteLine();
                }
            }

            string filePath = @"";
            int pathIdx = 0;
            if (args.Length > pathIdx)
            {
                if (args[0].ToLower() == "-l")
                {
                    extractFiles = false;
                    pathIdx++; // use next arg as filepath
                }
                else if (args[0].ToLower() == "-r")
                {
                    printRomInfo = true;
                    pathIdx++;
                }
                if (args.Length > pathIdx)
                    filePath = args[pathIdx];
            }
            pathIdx++;

            if (string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine("Usage: ");
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-recctrl.bin> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-SDK/remote-recovery.exe> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-recovery.iso> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-recovery.zip> [output-folder]");
                Console.WriteLine("Will try extracting all files to the given output folder");
                Console.WriteLine("If output folder isn't specified, will extract to \"<input-file-path>_ext\"");
                Console.WriteLine("-L will only list entries inside input file without extracting them");
                Console.WriteLine("-R will print info about any extracted X360 xboxrom images");
                Console.WriteLine("  (if -R isn't used, will print a summary instead)");
                return;
            }

            outputPath = filePath + "_ext";
            if (args.Length > pathIdx)
                outputPath = args[pathIdx];

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: failed to open file from path {filePath}!");
                return;
            }

            bool result = false;
            if (Path.GetExtension(filePath).ToLower() == ".exe")
                result = ProcessRecoveryEXE(filePath, outputPath, extractFiles);
            else if (Path.GetExtension(filePath).ToLower() == ".iso")
                result = ProcessRecoveryISO(File.OpenRead(filePath), outputPath, extractFiles);
            else if (Path.GetExtension(filePath).ToLower() == ".zip")
                result = ProcessRecoveryZIP(filePath, outputPath, extractFiles);
            else
            {
                string dataPath = filePath.Replace("recctrl", "recdata");
                if (extractFiles && !File.Exists(dataPath))
                {
                    Console.WriteLine($"Error: failed to open datafile from path {dataPath}!");
                    return;
                }

                result = ProcessRecovery(filePath, dataPath, outputPath, extractFiles);
            }

            if (!extractFiles || !result)
                return;

            if (printRomInfo)
            {
                Console.WriteLine();
                Console.WriteLine("xboxrom info:");
            }

            SearchForXboxRoms(outputPath, printRomInfo);

            var moboCount = Motherboards.Count + XboxOGMotherboards.Count;

            if (Versions.Count > 0 || moboCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine("xboxrom summary:");

                if (Versions.Count > 0)
                {
                    Versions.Sort();

                    Console.WriteLine($"{Versions.Count} included kernel build{(Versions.Count == 1 ? "" : "s")}");
                    foreach (var ver in Versions)
                        Console.WriteLine($"  {ver}");
                }

                if (moboCount > 0)
                {
                    Console.WriteLine($"{moboCount} supported motherboard{(moboCount == 1 ? "" : "s")}");

                    List<string> moboKeys = Motherboards.Keys.ToList();
                    moboKeys.Sort();
                    foreach (var mobo in moboKeys)
                    {
                        var versions = Motherboards[mobo];
                        versions.Sort();
                        var info = $"  {mobo} (kernels: ";
                        for (int i = 0; i < versions.Count; i++)
                            info += (i > 0 ? ", " : "") + versions[i].ToString();
                        info += ")";
                        Console.WriteLine(info);
                    }

                    XboxOGMotherboards.Sort();
                    foreach (var mobo in XboxOGMotherboards)
                        Console.WriteLine($"  {mobo}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Extract complete, hit enter to exit");
            Console.ReadLine();
        }

        static void SearchForXboxRoms(string path, bool printRomInfo = false)
        {
            foreach (var file in Directory.GetFiles(path))
            {
                var fileName = Path.GetFileName(file).ToLower();

                // Check filenames for Xbox OG motherboard types
                // TODO: check recoveries for any other types!
                {
                    string ogmobo = null;
                    if (fileName == "xboxrom_dvt2.bin")
                        ogmobo = "dvt2";
                    else if (fileName == "xboxrom_dvt4.bin")
                        ogmobo = "dvt4";
                    else if (fileName == "xboxrom_dvt6.bin")
                        ogmobo = "dvt6";
                    else if (fileName == "xboxrom_xblade.bin")
                        ogmobo = "xblade";
                    // Haven't seen any of the following yet:
                    else if (fileName == "xboxrom_dvt.bin" || fileName == "xboxrom_dvt1.bin")
                        ogmobo = "dvt1";
                    else if (fileName == "xboxrom_dvt3.bin")
                        ogmobo = "dvt3";
                    else if (fileName == "xboxrom_dvt5.bin")
                        ogmobo = "dvt5";
                    else if (fileName == "xboxrom_dvt7.bin")
                        ogmobo = "dvt7";

                    if (ogmobo != null && !XboxOGMotherboards.Contains(ogmobo))
                    {
                        XboxOGMotherboards.Add(ogmobo);
                        continue;
                    }
                }

                if (fileName == "xboxrom_update.bin")
                {
                    using (var reader = new BinaryReader(File.OpenRead(file)))
                    {
                        var header = new XboxRomHeader();
                        header.Read(reader);

                        if (printRomInfo)
                        {
                            Console.WriteLine();
                            var miniPath = path.Substring(outputPath.Length + 1);
                            if (miniPath.EndsWith(@"\KERNEL"))
                                miniPath = miniPath.Substring(0, miniPath.Length - 7);
                            Console.WriteLine(miniPath + ":");
                            Console.WriteLine($"  {header.MotherboardType} v{header.Version} (SMC v{header.SmcBuild})");
                        }

                        if (!Versions.Contains(header.Version))
                            Versions.Add(header.Version);
                        if (!Motherboards.ContainsKey(header.MotherboardType))
                            Motherboards[header.MotherboardType] = new List<int>();
                        if (!Motherboards[header.MotherboardType].Contains(header.Version))
                            Motherboards[header.MotherboardType].Add(header.Version);
                    }
                }
            }

            foreach (var dir in Directory.GetDirectories(path))
                SearchForXboxRoms(dir, printRomInfo);
        }

        static bool ProcessRecoveryEXE(string exePath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var fileStream = File.OpenRead(exePath))
            {
                var recovery = new RemoteRecovery(fileStream);
                if (!recovery.Read())
                    return false;

                if (extractFiles)
                    Console.WriteLine($"Extracting to {outputPath}...");

                return recovery.Extract(outputPath, !extractFiles);
            }
        }

        static bool ProcessRecoveryZIP(string zipPath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var fileStream = File.OpenRead(zipPath))
            {
                using (var archive = new ZipArchive(fileStream))
                {
                    ZipArchiveEntry isoEntry = null;
                    foreach (var entry in archive.Entries)
                    {
                        if (Path.GetExtension(entry.Name).ToLower() == ".iso")
                            isoEntry = entry;
                    }
                    if (isoEntry == null)
                    {
                        Console.WriteLine("Failed to find ISO inside ZIP file!");
                        return false;
                    }

                    // ZIP stream doesn't support setting position, have to copy it to another stream...
                    using (var memoryStream = new MemoryStream())
                    {
                        if (consoleOutput)
                            Console.WriteLine($"Extracting {isoEntry.Name} from ZIP file...");
                        isoEntry.Open().CopyTo(memoryStream);
                        return ProcessRecoveryISO(memoryStream, outputPath, extractFiles, consoleOutput);
                    }
                }
            }
        }

        static bool ProcessRecoveryGDF(Stream isoStream, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            var gdf = new XboxGDFImage(isoStream);
            if (!gdf.Read())
                return false;

            var entry = new GdfEntry();
            if (!gdf.GetEntry("recctrl.bin", ref entry, false))
            {
                Console.WriteLine("Failed to find recctrl.bin inside GDF image!");
                return false;
            }

            using (var reader = new BinaryReader(gdf.OpenFile(entry)))
            {
                Stream dataStream = null;
                if (extractFiles)
                {
                    if (!gdf.GetEntry("recdata.bin", ref entry, false))
                    {
                        Console.WriteLine("Failed to find recdata.bin inside GDF image!");
                        return false;
                    }
                    dataStream = gdf.OpenFile(entry);
                }

                bool res = ProcessRecovery(reader, dataStream, outputPath, consoleOutput);

                if (dataStream != null)
                    dataStream.Close();

                return res;
            }
        }

        static bool ProcessRecoveryISO(Stream isoStream, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            if (ProcessRecoveryGDF(isoStream, outputPath, extractFiles, consoleOutput))
                return true;

            DiscUtils.Vfs.VfsFileSystemFacade vfs = new CDReader(isoStream, true, false);
            if (!vfs.FileExists("recctrl.bin"))
                vfs = new UdfReader(isoStream);
            if (!vfs.FileExists("recctrl.bin"))
            {
                Console.WriteLine("Failed to find recctrl.bin inside image!");
                return false;
            }

            using (var reader = new BinaryReader(vfs.OpenFile("recctrl.bin", FileMode.Open)))
            {
                Stream dataStream = null;
                if (extractFiles)
                    dataStream = vfs.OpenFile("recdata.bin", FileMode.Open);

                bool res = ProcessRecovery(reader, dataStream, outputPath, consoleOutput);

                if (dataStream != null)
                    dataStream.Close();

                return res;
            }
        }

        static bool ProcessRecovery(string controlPath, string dataPath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var reader = new BinaryReader(File.OpenRead(controlPath)))
            {
                Stream dataStream = null;
                if (extractFiles)
                    dataStream = File.OpenRead(dataPath);

                bool res = ProcessRecovery(reader, dataStream, outputPath, consoleOutput);

                if (dataStream != null)
                    dataStream.Close();

                return res;
            }
        }

        static bool ProcessRecovery(BinaryReader controlReader, Stream dataStream, string outputPath, bool consoleOutput = true)
        {
            var controlFile = new RecoveryControlFile();
            controlFile.Read(controlReader);

            int i;
            if (consoleOutput)
            {
                Console.WriteLine($"recctrl.bin contents:");
                Console.WriteLine($"{controlFile.Entries.Count} file{(controlFile.Entries.Count == 1 ? "" : "s")}");
                Console.WriteLine($"{controlFile.Versions.Count} variant{(controlFile.Versions.Count == 1 ? "" : "s")}:");
                for (i = 1; i < controlFile.Versions.Count; i++)
                {
                    var numFiles = 0;
                    foreach (var entry in controlFile.Entries)
                        if (entry.VersionIndex == i)
                            numFiles++;

                    Console.WriteLine($"  - {controlFile.Versions[i]} ({numFiles} file{(numFiles == 1 ? "" : "s")})");
                }

                Console.WriteLine();
                Console.WriteLine($"{controlFile.Devices.Count} device{(controlFile.Devices.Count == 1 ? "" : "s")}:");
                for (i = 0; i < controlFile.Devices.Count; i++)
                {
                    var numFiles = 0;
                    foreach (var entry in controlFile.Entries)
                        if (entry.DeviceIndex == i)
                            numFiles++;

                    var device = controlFile.Devices[i];
                    Console.WriteLine($"  - {device.Key} = {device.Value} ({numFiles} file{(numFiles == 1 ? "" : "s")})");
                }

                Console.WriteLine();
            }

            if (dataStream != null)
            {
                Console.WriteLine($"Extracting to {outputPath}...");
            }

            i = 0;
            foreach (var entry in controlFile.Entries)
            {
                i++;

                // Make sure directory exists for this file
                var destPath = Path.Combine(outputPath, entry.GetShortPath());

                if (consoleOutput)
                    Console.WriteLine($"({i}/{controlFile.Entries.Count}) {entry.GetShortPath()} ({Util.GetBytesReadable(entry.DecompressedSize)})");

                // Try extracting!
                if (dataStream == null)
                    continue;

                var destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                try
                {
                    using (var destStream = File.Create(destPath))
                        entry.Extract(dataStream, destStream);

                    // Set last modified time to entry's timestamp
                    File.SetLastWriteTime(destPath, entry.DateTime);
                }
                catch (LzxInvalidWindowSize)
                {
                    Console.WriteLine("!!! Failed to extract due to invalid LZX window size, is the control file corrupt?");
                }
                catch (LzxDataLargerThanAgreed)
                {
                    Console.WriteLine("!!! Failed to extract as LZX data block is larger than expected, is the control file corrupt?");
                }
                catch (LzxDataInvalid)
                {
                    Console.WriteLine("!!! Failed to extract as LZX data is invalid, is the data file corrupt?");
                }
            }

            return true;
        }
    }
}
