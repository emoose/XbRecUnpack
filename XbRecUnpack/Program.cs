/* XbRecUnpack - tool for extracting Xbox recctrl.bin files
 * by emoose
 * 
 * Changelog:
 * v1.234:
 * - Changed out LZX decoder to a native C# version, no longer need to P/Invoke any windows DLLs :)
 * 
 * v0.123:
 * - Initial version, finally got Xbox/Xbox360 recctrl unpacking to work fine!
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace XbRecUnpack
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool extractFiles = true;

            Console.WriteLine("XbRecUnpack - tool for extracting Xbox recctrl.bin files");
            Console.WriteLine("v1.234 by emoose");

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
                Console.WriteLine("Usage: XbRecUnpack.exe [-L] <path-to-recctrl.bin> [output-folder]");
                Console.WriteLine("Will try extracting all files to the given output folder");
                Console.WriteLine("If output folder isn't specified, will extract to \"<recctrl.bin-path>_ext\"");
                Console.WriteLine("-L will only list files inside recovery without extracting them");
                return;
            }

            string dataPath = filePath.Replace("recctrl", "recdata");
            string outputPath = filePath + "_ext";
            if (args.Length > pathIdx)
                outputPath = args[pathIdx];

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: failed to open recovery control file from path {filePath}!");
                return;
            }

            if (extractFiles && !File.Exists(dataPath))
            {
                Console.WriteLine($"Error: failed to open recovery data from path {dataPath}!");
                return;
            }

            ProcessRecovery(filePath, dataPath, outputPath, extractFiles);

            if (!extractFiles)
                return;

            Console.WriteLine("Extract complete, hit enter to continue");
            Console.ReadLine();
        }

        static void ProcessRecovery(string controlPath, string dataPath, string outputPath, bool extractFiles = true, bool consoleOutput = true)
        {
            using (var reader = new BinaryReader(File.OpenRead(controlPath)))
            {
                var controlFile = new RecoveryControlFile();
                controlFile.Read(reader);

                int i;
                if (consoleOutput)
                {
                    Console.WriteLine($"{controlPath} contents:");
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

                Stream dataStream = null;
                if (extractFiles)
                {
                    dataStream = File.OpenRead(dataPath);
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
                    if (!extractFiles)
                        continue;

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    try
                    {
                        entry.Extract(dataStream, File.Create(destPath));
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

                if (dataStream != null)
                    dataStream.Close();
            }
        }
    }
}
