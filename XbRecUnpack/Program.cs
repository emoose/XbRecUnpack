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

namespace XbRecUnpack
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool extractFiles = true;

            Console.WriteLine("XbRecUnpack - tool for extracting Xbox recctrl.bin files");
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
                if (args.Length > pathIdx)
                    filePath = args[pathIdx];
            }
            pathIdx++;

            if (string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine("Usage: ");
                Console.WriteLine("  XbRecUnpack.exe [-L] <path-to-recctrl.bin> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L] <path-to-recovery.iso> [output-folder]");
                Console.WriteLine("  XbRecUnpack.exe [-L] <path-to-recovery.zip> [output-folder]");
                Console.WriteLine("Will try extracting all files to the given output folder");
                Console.WriteLine("If output folder isn't specified, will extract to \"<input-file-path>_ext\"");
                Console.WriteLine("-L will only list files inside recovery without extracting them");
                return;
            }

            string dataPath = filePath.Replace("recctrl", "recdata");
            string outputPath = filePath + "_ext";
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
            var cdReader = new CDReader(isoStream, true, true);

            using(var reader = new BinaryReader(cdReader.OpenFile("recctrl.bin", FileMode.Open)))
            {
                Stream dataStream = null;
                if (extractFiles)
                    dataStream = cdReader.OpenFile("recdata.bin", FileMode.Open);

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
