/* XbRecUnpack - tool for extracting Xbox recctrl.bin files
 * by emoose
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

namespace XbRecUnpack
{
    class Program
    {

        static List<int> Versions = new List<int>();
        static List<int> Motherboards = new List<int>();
        static string outputPath;

        static void SearchForXboxRoms(string path, bool printRomInfo = false)
        {
            foreach(var file in Directory.GetFiles(path))
            {
                if(Path.GetFileName(file).ToLower() == "xboxrom_update.bin")
                {
                    using (var reader = new BinaryReader(File.OpenRead(file)))
                    {
                        var header = new XboxRomHeader();
                        header.Read(reader);

                        if (printRomInfo)
                        {
                            Console.WriteLine(path.Substring(outputPath.Length + 1));
                            Console.WriteLine($"  {header.MotherboardType} v{header.Version} (SMC v{header.SmcBuild})");
                            Console.WriteLine();
                        }

                        if (!Versions.Contains(header.Version))
                            Versions.Add(header.Version);
                        if (!Motherboards.Contains(header.MotherboardTypeInt))
                            Motherboards.Add(header.MotherboardTypeInt);
                    }
                }
            }

            foreach (var dir in Directory.GetDirectories(path))
                SearchForXboxRoms(dir, printRomInfo);
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool extractFiles = true;
            bool printRomInfo = false;

            Console.WriteLine("XbRecUnpack - tool for extracting Xbox/Xbox360 recovery files");
            Console.WriteLine("v1.2345 by emoose");

            string filePath = @"";
            int pathIdx = 0;
            if (args.Length > pathIdx)
            {
                if (args[0].ToLower() == "-l")
                {
                    extractFiles = false;
                    pathIdx++; // use next arg as filepath
                }
                else if(args[0].ToLower() == "-r")
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
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-recovery.iso> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L/-R] <path-to-recovery.zip> [output-folder]");
                Console.WriteLine("Will try extracting all files to the given output folder");
                Console.WriteLine("If output folder isn't specified, will extract to \"<input-file-path>_ext\"");
                Console.WriteLine("-L will only list files inside recovery without extracting them");
                Console.WriteLine("-R will print info about each extracted X360 xboxrom image");
                Console.WriteLine("  (if -R isn't used, will print a summary instead)");
                return;
            }

            string dataPath = filePath.Replace("recctrl", "recdata");
            outputPath = filePath + "_ext";
            if (args.Length > pathIdx)
                outputPath = args[pathIdx];

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: failed to open recovery file from path {filePath}!");
                return;
            }

            if (Path.GetExtension(filePath).ToLower() == ".iso")
                ProcessRecoveryISO(File.OpenRead(filePath), outputPath, extractFiles);
            else if (Path.GetExtension(filePath).ToLower() == ".zip")
                ProcessRecoveryZIP(filePath, outputPath, extractFiles);
            else
            {
                if (extractFiles && !File.Exists(dataPath))
                {
                    Console.WriteLine($"Error: failed to open recovery data from path {dataPath}!");
                    return;
                }

                ProcessRecovery(filePath, dataPath, outputPath, extractFiles);
            }

            if (!extractFiles)
                return;

            Console.WriteLine();
            Console.WriteLine("xboxrom info:");

            SearchForXboxRoms(outputPath, printRomInfo);

            Console.WriteLine($"{Versions.Count} included kernel build{(Versions.Count == 1 ? "" : "s")}");
            foreach (var ver in Versions)
                Console.WriteLine($"  {ver}");
            Console.WriteLine($"{Motherboards.Count} supported motherboard{(Motherboards.Count == 1 ? "" : "s")}");

            string[] types = { "none/unk", "xenon", "zephyr", "falcon", "jasper", "trinity", "corona", "winchester" };
            for(int i = 0; i < types.Length; i++)
                if(Motherboards.Contains(i))
                    Console.WriteLine($"  {types[i]}");

            Console.WriteLine();
            Console.WriteLine("Extract complete, hit enter to exit");
            Console.ReadLine();
        }

        static void ProcessRecoveryZIP(string zipPath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var fileStream = File.OpenRead(zipPath))
            {
                using (var archive = new ZipArchive(fileStream))
                {
                    ZipArchiveEntry isoEntry = null;
                    foreach(var entry in archive.Entries)
                    {
                        if (Path.GetExtension(entry.Name).ToLower() == ".iso")
                            isoEntry = entry;
                    }
                    if (isoEntry == null)
                        return; // TODO: display error

                    // ZIP stream doesn't support setting position, have to copy it to another stream...
                    using (var memoryStream = new MemoryStream())
                    {
                        if (consoleOutput)
                            Console.WriteLine($"Extracting {isoEntry.Name} from ZIP file...");
                        isoEntry.Open().CopyTo(memoryStream);
                        ProcessRecoveryISO(memoryStream, outputPath, extractFiles, consoleOutput);
                    }
                }
            }
        }

        static void ProcessRecoveryISO(Stream isoStream, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            DiscUtils.Vfs.VfsFileSystemFacade vfs = new CDReader(isoStream, true, false);
            if (!vfs.FileExists("recctrl.bin"))
                vfs = new UdfReader(isoStream);
            if (!vfs.FileExists("recctrl.bin"))
            {
                Console.WriteLine("Failed to find recctrl.bin inside image!");
                return;
            }
            using (var reader = new BinaryReader(vfs.OpenFile("recctrl.bin", FileMode.Open)))
            {
                Stream dataStream = null;
                if (extractFiles)
                    dataStream = vfs.OpenFile("recdata.bin", FileMode.Open);

                ProcessRecovery(reader, dataStream, outputPath, consoleOutput);

                if (dataStream != null)
                    dataStream.Close();
            }
        }

        static void ProcessRecovery(string controlPath, string dataPath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var reader = new BinaryReader(File.OpenRead(controlPath)))
            {
                Stream dataStream = null;
                if (extractFiles)
                    dataStream = File.OpenRead(dataPath);

                ProcessRecovery(reader, dataStream, outputPath, consoleOutput);

                if (dataStream != null)
                    dataStream.Close();
            }
        }

        static void ProcessRecovery(BinaryReader controlReader, Stream dataStream, string outputPath, bool consoleOutput = true)
        {
            var controlFile = new RecoveryControlFile();
            controlFile.Read(controlReader);

            int i;
            if (consoleOutput)
            {
                Console.WriteLine($"recctrl.bin contents:");
                Console.WriteLine($"{controlFile.Entries.Count} files");
                Console.WriteLine($"{controlFile.Versions.Count} variants:");
                for (i = 1; i < controlFile.Versions.Count; i++)
                {
                    var numFiles = 0;
                    foreach (var entry in controlFile.Entries)
                        if (entry.VersionIndex == i)
                            numFiles++;

                    Console.WriteLine($"  - {controlFile.Versions[i]} ({numFiles} files)");
                }

                Console.WriteLine();
                Console.WriteLine($"{controlFile.Devices.Count} devices:");
                for (i = 0; i < controlFile.Devices.Count; i++)
                {
                    var numFiles = 0;
                    foreach (var entry in controlFile.Entries)
                        if (entry.DeviceIndex == i)
                            numFiles++;

                    var device = controlFile.Devices[i];
                    Console.WriteLine($"  - {device.Key} = {device.Value} ({numFiles} files)");
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
        }
    }
}
