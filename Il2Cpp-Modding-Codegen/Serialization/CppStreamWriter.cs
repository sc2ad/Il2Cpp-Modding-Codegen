using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppModdingCodegen.Serialization
{
    /// <summary>
    /// A C++ valid syntax writer
    /// </summary>
    public class CppStreamWriter : IndentedTextWriter
    {
        private readonly StreamWriter rawWriter;
        internal CppStreamWriter(StreamWriter writer) : base(writer) { rawWriter = writer; }

        internal CppStreamWriter(StreamWriter writer, string tabString) : base(writer, tabString) { rawWriter = writer; }

        /// <summary>
        /// Write a single line comment
        /// </summary>
        /// <param name="commentString"></param>
        internal void WriteComment(string commentString) => WriteLine("// " + commentString);

        internal void WriteInclude(string include) => WriteLine("#include \"" + include + "\"");

        /// <summary>
        /// Write a single ;-terminated line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        internal void WriteDeclaration(string declString) => WriteLine(declString + ";");

        internal void WriteFieldDeclaration(string fieldType, string fieldName) => WriteDeclaration(fieldType + ' ' + fieldName);

        /// <summary>
        /// Write a single syntax ; terminated line with a comment on the same line (or a declaration)
        /// </summary>
        /// <param name="declString"></param>
        /// <param name="commentString">If null or empty, will call <see cref="WriteDeclaration(string)"/></param>
        internal void WriteDeclaration(string declString, string commentString)
        {
            if (string.IsNullOrEmpty(commentString))
                WriteDeclaration(declString);
            else
                WriteLine(declString + "; // " + commentString);
        }

        /// <summary>
        /// Write a definition and open a body with {
        /// </summary>
        /// <param name="defString"></param>
        internal void WriteDefinition(string defString)
        {
            WriteLine(defString + " {");
            Indent++;
        }

        /// <summary>
        /// Close a body with }
        /// </summary>
        internal void CloseDefinition(string suffix = "")
        {
            Indent--;
            WriteLine("}" + suffix);
        }

        private static HashSet<string> ExistingFiles { get; set; } = new HashSet<string>();
        internal static void PopulateExistingFiles(string dir)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };
            ExistingFiles.UnionWith(Directory.EnumerateFiles(dir, "*", options));
        }

        private static HashSet<string> Written { get; set; } = new HashSet<string>();

        private static long NumChangedFiles { get; set; } = 0;
        internal void WriteIfDifferent(string filePath, CppTypeContext context)
        {
            if (!Written.Add(Path.GetFullPath(filePath)))
                throw new InvalidOperationException($"Was about to overwrite existing file: {filePath} with context: {context.LocalType.This}");

            if (WriteIfDifferent(filePath, rawWriter.BaseStream))
                NumChangedFiles++;
        }

        internal static bool WriteIfDifferent(string filePath, Stream stream)
        {
            stream.Position = 0;
            bool ret = false;
            long positionOfDifference = 0;
            using (var fileRead = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Read))
            {
                int a = 0, b = 0;
                while (a != -1 && b != -1)
                    if ((a = fileRead.ReadByte()) != (b = stream.ReadByte()))
                    {
                        ret = true;
                        if (a != -1) fileRead.Position -= 1;
                        if (b != -1) stream.Position -= 1;
                        if (fileRead.Position != stream.Position)
                            throw new Exception("WriteIfDifferent file position logic is wrong!");
                        positionOfDifference = fileRead.Position;
                        break;
                    }
            }
            if (ret)
            {
                using var fileWrite = File.OpenWrite(filePath);
                fileWrite.Seek(positionOfDifference, SeekOrigin.Begin);
                stream.CopyTo(fileWrite);
                fileWrite.Flush();
                fileWrite.SetLength(fileWrite.Position);
            }
            return ret;
        }

        internal static void DeleteUnwrittenFiles()
        {
            ExistingFiles.Except(Written).AsParallel().ForAll(s => File.Delete(s));
            Console.WriteLine($"Deleted {ExistingFiles.Count - Written.Count} files!");
            Console.WriteLine($"Made changes to {NumChangedFiles} / {Written.Count} files = {NumChangedFiles * 100.0f / Written.Count}%");
        }
    }
}
