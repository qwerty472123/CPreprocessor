using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CPreprocessor
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class LevelColorAttribute : Attribute
    {
        public LevelColorAttribute(ConsoleColor color)
        {
            Color = color;
        }

        public ConsoleColor Color { get; private set; }
    }
    public enum ShowLevel
    {
        [LevelColor(ConsoleColor.DarkGray)]
        Verbose,
        [LevelColor(ConsoleColor.DarkGray)]
        Detail,
        [LevelColor(ConsoleColor.White)]
        Infomation,
        [LevelColor(ConsoleColor.Yellow)]
        Warning,
        [LevelColor(ConsoleColor.Red)]
        Error,
        [LevelColor(ConsoleColor.Red)]
        Critical
    }
    public static class ShowLevelExtension
    {
        public static ConsoleColor GetColor(this ShowLevel level)
        {
            var field = level.GetType().GetField(level.ToString());
            if (field.IsDefined(typeof(LevelColorAttribute), true))
            {
                object[] attrs = field.GetCustomAttributes(typeof(LevelColorAttribute), true);
                if (attrs.Length > 1) return ConsoleColor.DarkRed;
                if (attrs.Length == 0) return ConsoleColor.White;
                LevelColorAttribute attr = (LevelColorAttribute)attrs[0];
                return attr.Color;
            }
            else
            {
                return ConsoleColor.White;
            }
        }
    }
    static class Program
    {
        public static string ProgramPath = "";
        public static ShowLevel AtLeastLevel = ShowLevel.Warning;
        public static bool IsMultiThread = false;
        public static string OutputDirectory = null;
        public static string OutputFileName = null;
        public static bool ShowHelp = false;
        public static bool Overwrite = false;
        public static List<string> Includes = new List<string>();
        private static object showLock = new object();
        public static void Show(ShowLevel level, string content, bool prefix = true)
        {
            if (level >= AtLeastLevel)
            {
                lock (showLock)
                {
                    Console.ForegroundColor = level.GetColor();
                    if (prefix) Console.Write(level.ToString().ToLower() + ": ");
                    Console.WriteLine(content);
                    Console.ResetColor();
                }
            }
        }
        public static void Show(ShowLevel level, Exception exception)
        {
            if (level >= AtLeastLevel)
            {
                Show(level, exception.Message);
                Show(ShowLevel.Detail, exception.StackTrace, false);
            }
        }
        static void Main(string[] args)
        {
            ProgramPath = typeof(Program).Assembly.Location;
            var fileArgs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-") || args[i] == "/?")
                {
                    try
                    {
                        switch (args[i])
                        {
                            case "-m":
                            case "--multi-thread":
                                IsMultiThread = true;
                                break;
                            case "-s":
                            case "--show-level":
                                AtLeastLevel = Enum.Parse<ShowLevel>(args[++i], true);
                                break;
                            case "-o":
                            case "--output-directoy":
                                OutputDirectory = args[++i];
                                break;
                            case "-of":
                            case "--output-file":
                                OutputFileName = args[++i];
                                break;
                            case "-f":
                            case "--overwrite":
                                Overwrite = true;
                                break;
                            case "-i":
                            case "--include-path":
                                Includes.AddRange(args[++i].Split(Path.PathSeparator));
                                break;
                            case "-h":
                            case "--help":
                            case "/?":
                                ShowHelp = true;
                                break;
                            default:
                                throw new PreprocessorException("No such type");
                        }
                    }
                    catch (PreprocessorException err)
                    {
                        Show(ShowLevel.Warning, $"{args[i]} cannot be parsed: {err.Message}");
                    }
                    catch(Exception err)
                    {
                        Show(ShowLevel.Critical, err);
                    }
                }
                else
                {
                    fileArgs.Add(args[i]);
                }
            }
            if (ShowHelp || args.Length == 0)
            {
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(ProgramPath)} - A C Preprocessor");
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(ProgramPath)} [options...] [c files...]");
                Console.WriteLine("option:");
                Console.WriteLine("-h | --help | /?\tshow this text");
                Console.WriteLine("-m | --multi-thread\tuse multi thread to assemble");
                Console.WriteLine("-s | --show-level\tdefine the output show level (verbose|detail|infomation|warning|error|critical)");
                Console.WriteLine("-f | --overwrite\tforce overwrite file");
                Console.WriteLine("-o | --output-directoy <directory>\toutput directory");
                Console.WriteLine("-of | --output-file <filename>\toutput file name");
                Console.WriteLine("-i | --include-path <directory>\tadd an include path");
                return;
            }
            var newIncludes = new List<string>();
            string includeStr = Environment.GetEnvironmentVariable("INCLUDE");
            if (includeStr != null) Includes.AddRange(includeStr.Split(Path.PathSeparator));
            foreach (var path in Includes){
                if (path.Length == 0) continue;
                if (Directory.Exists(path))
                {
                    newIncludes.Add(path);
                }
                else
                {
                    Show(ShowLevel.Warning, $"include directory ({path}) not found");
                }
            }
            Includes = newIncludes;
            var inFileNames = new List<string>();
            foreach (string fn in fileArgs)
            {
                if (fn.StartsWith("-")) continue;
                if (fn.Contains("?") || fn.Contains("*"))
                {
                    string[] files = Directory.GetFiles(Path.GetDirectoryName(fn), Path.GetFileName(fn), SearchOption.TopDirectoryOnly);
                    inFileNames.AddRange(files);
                }
                inFileNames.Add(fn);
            }
            if (inFileNames.Count > 1 && OutputFileName != null)
            {
                Show(ShowLevel.Error, "output file name shall not used in multi inputs");
                return;
            }
            var actions = new List<Action>();
            foreach(var name in inFileNames)
            {
                if (Path.GetExtension(name).ToLower() != ".c")
                {
                    Show(ShowLevel.Warning, $"file({name}) may not a c file");
                }
                string outName = OutputFileName;
                if (outName == null)
                {
                    if (OutputDirectory != null)
                    {
                        outName = Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(name) + ".p.c");
                    }
                    else
                    {
                        outName = Path.Combine(Path.GetDirectoryName(name), Path.GetFileNameWithoutExtension(name) + ".p.c");
                    }
                }
                if (File.Exists(outName))
                {
                    if (Overwrite)
                    {
                        Show(ShowLevel.Infomation, $"required overwrite file({outName})");
                    }
                    else
                    {
                        Show(ShowLevel.Error, $"file({outName}) existed, require overwrite");
                        continue;
                    }
                }
                string curName = name;
                actions.Add(() =>
                {
                    RunForFile(curName, outName);
                });
            }
            if (IsMultiThread)
            {
                Task.WaitAll(actions.Select(x => Task.Run(x)).ToArray());
            }
            else
            {
                foreach (var action in actions)
                {
                    action();
                }
            }
        }
        private static void RunForFile(string input, string output)
        {
            try
            {
                var executor = new Executor();
                File.WriteAllBytes(output, executor.ExecuteTop(input));
            }
            catch (PreprocessorException err)
            {
                Show(ShowLevel.Error, err);
            }
            catch (Exception err)
            {
                Show(ShowLevel.Critical, err);
            }
        }
    }
}
