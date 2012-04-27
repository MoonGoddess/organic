﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using orgASM.Plugins;

namespace orgASM
{
    public partial class Assembler
    {
        public static void Main(string[] args)
        {
            DateTime startTime = DateTime.Now;

            DisplaySplash();
            if (args.Length == 0)
            {
                DisplayHelp();
                return;
            }
            string inputFile = null;
            string outputFile = null;
            string listingFile = null;
            string pipe = null;
            string workingDirectory = Directory.GetCurrentDirectory();
            bool bigEndian = false, quiet = false, verbose = false;
            Assembler assembler = new Assembler();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("-"))
                {
                    try
                    {
                        switch (arg)
                        {
                            case "-h":
                            case "-?":
                            case "/h":
                            case "/?":
                            case "--help":
                                DisplayHelp();
                                return;
                            case "-o":
                            case "--output":
                            case "--output-file":
                                outputFile = args[++i];
                                break;
                            case "--input-file":
                                inputFile = args[++i];
                                break;
                            case "-e":
                            case "--equate":
                                ExpressionResult result = assembler.ParseExpression(args[i + 2]);
                                if (!result.Successful)
                                {
                                    Console.WriteLine("Error: " + ListEntry.GetFriendlyErrorMessage(ErrorCode.IllegalExpression));
                                    return;
                                }
                                assembler.Values.Add(args[i + 1].ToLower(), result.Value);
                                i += 2;
                                break;
                            case "-l":
                            case "--listing":
                                listingFile = args[++i];
                                break;
                            case "--big-endian":
                            case "-b":
                                bigEndian = true;
                                break;
                            case "--quiet":
                            case "-q":
                                quiet = true;
                                break;
                            case "--pipe":
                            case "-p":
                                pipe = args[++i];
                                break;
                            case "--include":
                            case "-i":
                                assembler.IncludePath = args[++i];
                                break;
                            case "--plugins":
                                ListPlugins();
                                return;
                            case "--working-directory":
                            case "-w":
                                workingDirectory = args[++i];
                                break;
                            case "--verbose":
                            case "-v":
                                verbose = true;
                                break;
                            case "--debug-mode":
                                Console.ReadKey();
                                break;
                            default:
                                HandleParameterEventArgs hpea = new HandleParameterEventArgs(arg);
                                hpea.Arguments = args;
                                if (assembler.TryHandleParameter != null)
                                    assembler.TryHandleParameter(assembler, hpea);
                                if (!hpea.Handled)
                                {
                                    Console.WriteLine("Error: Invalid parameter: " + arg + "\nUse orgASM.exe --help for usage information.");
                                    return;
                                }
                                if (hpea.StopProgram)
                                    return;
                                break;
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        Console.WriteLine("Error: Missing argument: " + arg + "\nUse orgASM.exe --help for usage information.");
                        return;
                    }
                }
                else
                {
                    if (inputFile == null)
                        inputFile = arg;
                    else if (outputFile == null)
                        outputFile = arg;
                    else
                    {
                        Console.WriteLine("Error: Invalid parameter: " + arg + "\nUse orgASM.exe --help for usage information.");
                        return;
                    }
                }
            }
            if (inputFile == null && pipe == null)
            {
                Console.WriteLine("Error: No input file specified.\nUse orgASM.exe --help for usage information.");
                return;
            }
            if (outputFile == null)
                outputFile = Path.GetFileNameWithoutExtension(inputFile) + ".bin";
            if (!File.Exists(inputFile) && pipe == null && inputFile != "-")
            {
                Console.WriteLine("Error: File not found (" + inputFile + ")");
                return;
            }

            string contents;
            if (pipe == null)
            {
                if (inputFile != "-")
                {
                    StreamReader reader = new StreamReader(inputFile);
                    contents = reader.ReadToEnd();
                    reader.Close();
                }
                else
                    contents = Console.In.ReadToEnd();
            }
            else
                contents = pipe;

            List<ListEntry> output;
            string wdOld = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workingDirectory);
            if (pipe == null)
                output = assembler.Assemble(contents, inputFile);
            else
                output = assembler.Assemble(contents, "[piped input]");
            Directory.SetCurrentDirectory(wdOld);

            if (assembler.AssemblyComplete != null)
                assembler.AssemblyComplete(assembler, new AssemblyCompleteEventArgs(output));

            // Output errors
            if (!quiet)
            {
                foreach (var entry in output)
                {
                    if (entry.ErrorCode != ErrorCode.Success)
                        Console.WriteLine("Error " + entry.FileName + " (line " + entry.LineNumber + "): " + ListEntry.GetFriendlyErrorMessage(entry.ErrorCode));
                    if (entry.WarningCode != WarningCode.None)
                        Console.WriteLine("Warning " + entry.FileName + " (line " + entry.LineNumber + "): " + ListEntry.GetFriendlyWarningMessage(entry.WarningCode));
                }
            }

            Stream binStream = null;
            if (outputFile != "-")
                binStream = File.Open(outputFile, FileMode.Create);
            foreach (var entry in output)
            {
                if (entry.Output != null)
                {
                    foreach (ushort value in entry.Output)
                    {
                        byte[] buffer = BitConverter.GetBytes(value);
                        if (bigEndian)
                            Array.Reverse(buffer);
                        if (inputFile != "-")
                            binStream.Write(buffer, 0, buffer.Length);
                        else
                            Console.Out.Write(Encoding.ASCII.GetString(buffer));
                    }
                }
            }

            string listing = "";

            if (listingFile != null || verbose)
                listing = CreateListing(output);

            if (verbose)
                Console.Write(listing);
            if (listingFile != null)
            {
                StreamWriter writer = new StreamWriter(listingFile);
                writer.Write(listing);
                writer.Close();
            }

            TimeSpan duration = DateTime.Now - startTime;
            Console.WriteLine(".orgASM build complete " + duration.TotalMilliseconds + "ms");
        }

        private static void ListPlugins()
        {
            string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            Console.WriteLine("Listing plugins:");
            foreach (string name in names)
            {
                if (name.EndsWith(".dll"))
                    Console.WriteLine(name);
            }
        }

        public static string CreateListing(List<ListEntry> output)
        {
            string listing = "";
            int maxLength = 0, maxFileLength = 0;
            foreach (var entry in output)
            {
                int length = entry.FileName.Length + 1;
                if (length > maxFileLength)
                    maxFileLength = length;
            }
            foreach (var entry in output)
            {
                int length = maxFileLength + entry.LineNumber.ToString().Length + 9;
                if (length > maxLength)
                    maxLength = length;
            }
            TabifiedStringBuilder tsb;
            foreach (var listentry in output)
            {
                tsb = new TabifiedStringBuilder();
                if ((listentry.Code.StartsWith(".dat") || listentry.Code.StartsWith(".dw") || 
                    listentry.Code.StartsWith(".db") || listentry.Code.StartsWith(".ascii") ||
                    listentry.Code.StartsWith(".asciiz") || listentry.Code.StartsWith(".asciip") ||
                    listentry.Code.StartsWith(".asciic") || listentry.Code.StartsWith(".align") ||
                    listentry.Code.StartsWith(".fill") || listentry.Code.StartsWith(".pad") ||
                    listentry.Code.StartsWith(".incbin") || listentry.Code.StartsWith(".reserve"))
                    && listentry.ErrorCode == ErrorCode.Success) // TODO: Move these to an array?
                {
                    // Write code line
                    tsb = new TabifiedStringBuilder();
                    tsb.WriteAt(0, listentry.FileName);
                    tsb.WriteAt(maxFileLength, "(line " + listentry.LineNumber + "): ");
                    if (listentry.Listed)
                        tsb.WriteAt(maxLength, "[0x" + LongHex(listentry.Address) + "] ");
                    else
                        tsb.WriteAt(maxLength, "[NOLIST] ");
                    tsb.WriteAt(maxLength + 25, listentry.Code);
                    listing += tsb.Value + "\n";
                    // Write data
                    for (int i = 0; i < listentry.Output.Length; i += 8)
                    {
                        tsb = new TabifiedStringBuilder();
                        tsb.WriteAt(0, listentry.FileName);
                        tsb.WriteAt(maxFileLength, "(line " + listentry.LineNumber + "): ");
                        if (listentry.Listed)
                            tsb.WriteAt(maxLength, "[0x" + LongHex((ushort)(listentry.Address + i)) + "] ");
                        else
                            tsb.WriteAt(maxLength, "[NOLIST] ");
                        string data = "";
                        for (int j = 0; j < 8 && i + j < listentry.Output.Length; j++)
                        {
                            data += LongHex(listentry.Output[i + j]) + " ";
                        }
                        tsb.WriteAt(maxLength + 25, data.Remove(data.Length - 1));
                        listing += tsb.Value + "\n";
                    }
                }
                else
                {
                    if (listentry.ErrorCode != ErrorCode.Success)
                    {
                        tsb = new TabifiedStringBuilder();
                        tsb.WriteAt(0, listentry.FileName);
                        tsb.WriteAt(maxFileLength, "(line " + listentry.LineNumber + "): ");
                        if (listentry.Listed)
                            tsb.WriteAt(maxLength, "[0x" + LongHex(listentry.Address) + "] ");
                        else
                            tsb.WriteAt(maxLength, "[NOLIST] ");
                        tsb.WriteAt(maxLength + 8, "ERROR: " + ListEntry.GetFriendlyErrorMessage(listentry.ErrorCode));
                        listing += tsb.Value + "\n";
                    }
                    if (listentry.WarningCode != WarningCode.None)
                    {
                        tsb = new TabifiedStringBuilder();
                        tsb.WriteAt(0, listentry.FileName);
                        tsb.WriteAt(maxFileLength, "(line " + listentry.LineNumber + "): ");
                        if (listentry.Listed)
                            tsb.WriteAt(maxLength, "[0x" + LongHex(listentry.Address) + "] ");
                        else
                            tsb.WriteAt(maxLength, "[NOLIST] ");
                        tsb.WriteAt(maxLength + 8, "WARNING: " + ListEntry.GetFriendlyWarningMessage(listentry.WarningCode));
                        listing += tsb.Value + "\n";
                    }
                    tsb = new TabifiedStringBuilder();
                    tsb.WriteAt(0, listentry.FileName);
                    tsb.WriteAt(maxFileLength, "(line " + listentry.LineNumber + "): ");
                    if (listentry.Listed)
                        tsb.WriteAt(maxLength, "[0x" + LongHex(listentry.Address) + "] ");
                    else
                        tsb.WriteAt(maxLength, "[NOLIST] ");
                    if (listentry.Output != null)
                    {
                        tsb.WriteAt(maxLength + 8, DumpArray(listentry.Output));
                        tsb.WriteAt(maxLength + 25, listentry.Code);
                    }
                    else
                        tsb.WriteAt(maxLength + 23, listentry.Code);
                    listing += tsb.Value + "\n";
                }
            }
            return listing;
        }

        private static string LongHex(ushort p)
        {
            string value = p.ToString("x");
            while (value.Length < 4)
                value = "0" + value;
            return value.ToUpper();
        }

        private static string DumpArray(ushort[] array)
        {
            string output = "";
            foreach (ushort u in array)
            {
                string val = u.ToString("x").ToUpper();
                while (val.Length < 4)
                    val = "0" + val;
                output += " " + val;
            }
            return output.Substring(1);
        }

        private static void DisplaySplash()
        {
            Console.WriteLine(".orgASM DCPU-16 Assembler    Copyright Drew DeVault 2012");
        }

        internal static List<string> PluginHelp = new List<string>();

        private static void DisplayHelp()
        {
            Console.WriteLine("Usage: orgASM.exe [parameters] [input file] [output file]\n" +
                "Output file is optional; if left out, .orgASM will use [input file].bin.\n\n" +
                "===Flags:\n" +
                "--big-endian: Switches output to big-endian mode.\n" +
                "--equate [key] [value]: Adds an equate, with the same syntax as .equ.\n" +
                "--help: Displays this message.\n" +
                "--input-file [filename]: An alternative way to specify the input file.\n" +
                "--include [path]: Adds [path] to the search index for #include <> files.\n" +
                "--listing [filename]: Outputs a listing to [filename].\n" +
                "--output-file [filename]: An alternative way to specify the output file.\n" +
                "--pipe [assembly]: Assemble [assembly], instead of the input file.\n" +
                "--quiet: .orgASM will not output error information.\n" +
                "--verbose: .orgASM will output a listing to the console.\n" +
                "--working-directory [directory]: Change .orgASM's working directory.");
            if (PluginHelp.Count != 0)
            {
                Console.WriteLine("\n===Plugins");
                foreach (var help in PluginHelp)
                    Console.WriteLine(help);
            }
        }
    }
}
