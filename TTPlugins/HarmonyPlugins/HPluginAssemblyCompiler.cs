﻿using Microsoft.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace com.tiberiumfusion.ttplugins.HarmonyPlugins
{
    /// <summary>
    /// Provider of the compiled assemblies that contain the usercode HPlugins.
    /// </summary>
    public static class HPluginAssemblyCompiler
    {
        /// <summary>
        /// Name of the temporary folder which will be created on disk if necessary during the assembly compilation (such as for referencing in-memory assemblies with CodeDom)
        /// </summary>
        public static string TemporaryFilesDirectory { get; set; } = ".TTPlugins_CompileTemp";

        /// <summary>
        /// Name of the output folder which will be created to contain the generated dll and pdb files from the compile process.
        /// </summary>
        public static string DefaultOutputFilesDirectory { get; set; } = ".TTPlugins_CompileOutput";
        

        /// <summary>
        /// Attempts to load the specific assemblies which Terraria references (and potentially some others) into the current AppDomain.
        /// Only loads "regular" assemblies, i.e. those that exist on-disk and are not embedded in Terraria itself.
        /// </summary>
        public static void LoadReferencesForCompilingPlugins()
        {
            // Load all of Terraria's explicit references (should be in the gac) that aren't embedded dependency assemblies
            Assembly.Load("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            Assembly.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
            Assembly.Load("System.Runtime.Serialization, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            Assembly.Load("System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            Assembly.Load("WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            Assembly.Load("Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
            Assembly.Load("Microsoft.Xna.Framework.Game, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
            Assembly.Load("Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
            Assembly.Load("Microsoft.Xna.Framework.Xact, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
        }


        /// <summary>
        /// Compiles and returns a list of assemblies using the provided configuration.
        /// </summary>
        /// <param name="configuration">The configuration to use when compiling.</param>
        /// <returns>The compiled assemblies.</returns>
        public static HPluginCompilationResult Compile(HPluginCompilationConfiguration configuration)
        {
            HPluginCompilationResult result = new HPluginCompilationResult();
            
            // Output directory
            string outputDir = configuration.DiskOutputDirectory ?? DefaultOutputFilesDirectory;
            result.OutputDirectory = outputDir;

            try
            {
                // Load all potentially required assemblies into our appdomain
                LoadReferencesForCompilingPlugins();

                // Compiler configuration
                CompilerParameters compilerParams = new CompilerParameters();
                compilerParams.GenerateInMemory = true; // This just affects the compilation process (should be faster than using the disk). The output assembly and its pdb are always written to a file.
                compilerParams.GenerateExecutable = false;
                compilerParams.CompilerOptions = "/optimize";
                compilerParams.IncludeDebugInformation = true;
                compilerParams.TreatWarningsAsErrors = false;
                // References on disk
                foreach (string filePath in configuration.ReferencesOnDisk)
                    compilerParams.ReferencedAssemblies.Add(filePath);
                // References in memory
                if (configuration.ReuseTemporaryFiles)
                {
                    if (Directory.Exists(TemporaryFilesDirectory))
                    {
                        foreach (string refAsmPath in Directory.GetFiles(TemporaryFilesDirectory))
                            compilerParams.ReferencedAssemblies.Add(refAsmPath);
                    }
                }
                else
                {
                    int refAsmNum = 0;
                    foreach (byte[] asmBytes in configuration.ReferencesInMemory)
                    {
                        Directory.CreateDirectory(TemporaryFilesDirectory);
                        string asmFullPath = Path.Combine(Directory.GetCurrentDirectory(), TemporaryFilesDirectory, "RefAsm" + refAsmNum + ".dll");
                        File.WriteAllBytes(asmFullPath, asmBytes);
                        compilerParams.ReferencedAssemblies.Add(asmFullPath);
                        refAsmNum++;
                    }
                }
                // Reference self (TTPlugins)
                compilerParams.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
                // Reference whatever is loaded in our appdomain, which will likely contain most of the common types and namespaces from System.
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !asm.ReflectionOnly && !String.IsNullOrEmpty(asm.Location))
                        compilerParams.ReferencedAssemblies.Add(asm.Location);
                }

                CSharpCodeProvider csProvider = new CSharpCodeProvider();

                // If output directory already exists, clear it
                if (Directory.Exists(outputDir))
                {
                    DirectoryInfo outputDirInfo = new DirectoryInfo("outputDir");
                    outputDirInfo.Delete(true);
                }
                Directory.CreateDirectory(outputDir);

                // Setup output and compile
                if (configuration.SingleAssemblyOutput)
                {
                    string dllName = "TTPlugins_CompiledConglomerate.dll";
                    string pdbName = Path.GetFileNameWithoutExtension(dllName) + ".pdb";
                    string dllOutput = Path.Combine(outputDir, dllName);
                    string pdbOutput = Path.Combine(outputDir, pdbName);
                    compilerParams.OutputAssembly = dllOutput;
                    result.OutputFilesOnDisk.Add(dllOutput);
                    result.OutputFilesOnDisk.Add(pdbOutput);
                    CompileOnce(configuration.SourceFiles, configuration, compilerParams, csProvider, result);
                }
                else
                {
                    foreach (string sourceFile in configuration.SourceFiles)
                    {
                        int numShift = 0;
                        string originDllName = "TTPlugins_CompiledAsm_" + Path.GetFileNameWithoutExtension(sourceFile);
                        string dllName = originDllName;
                        string checkDllOutput = Path.Combine(outputDir, dllName + ".dll");
                        while (File.Exists(checkDllOutput)) // Ensure no file conflicts
                        {
                            numShift++;
                            dllName = originDllName + numShift;
                            checkDllOutput = Path.Combine(outputDir, dllName + ".dll");
                        }
                        string pdbName = Path.GetFileNameWithoutExtension(dllName) + ".pdb";
                        string dllOutput = Path.Combine(outputDir, dllName);
                        string pdbOutput = Path.Combine(outputDir, pdbName);
                        compilerParams.OutputAssembly = dllOutput;
                        result.OutputFilesOnDisk.Add(dllOutput);
                        result.OutputFilesOnDisk.Add(pdbOutput);
                        CompileOnce(new List<string>() { sourceFile }, configuration, compilerParams, csProvider, result);
                    }
                }
            }
            catch (Exception e)
            {
                result.GenericCompilationFailure = true;
            }

            // Clear temporary reference assembly files if config says to or if there was a compile failure
            if (configuration.ClearTemporaryFilesWhenDone || result.GenericCompilationFailure)
                ClearTemporaryCompileFiles();

            // Clear output files if config says to or if there was a compile failure
            if (configuration.DeleteOutputFilesFromDiskWhenDone || result.GenericCompilationFailure)
            {
                TryRemoveDirectory(outputDir);
                result.OutputFilesOnDisk.Clear();
            }
            
            return result;
        }
        
        private static void CompileOnce(List<string> sourceFiles, HPluginCompilationConfiguration configuration, CompilerParameters compilerParams, CSharpCodeProvider csProvider, HPluginCompilationResult result)
        {
            CompilerResults compileResult = csProvider.CompileAssemblyFromFile(compilerParams, sourceFiles.ToArray());

            if (compileResult.Errors.HasErrors)
            {
                foreach (CompilerError error in compileResult.Errors)
                    result.CompileErrors.Add(error);
            }
            else
            {
                // Get the compiled assembly
                Assembly asm = compileResult.CompiledAssembly;  
                result.CompiledAssemblies.Add(asm);

                // Then go through all compiled HPlugin types and deduce the relative path of each one so that it can be associated with its savedata
                // Because of some less-than-great techniques required to do this, it may fail. If that happens, we can at least let the plugin run anyways, just without access to persistent savedata.
                try
                {
                    // We need Cecil to do this. Possible solutions with Reflection and CompilerServices get close (i.e. using StackTrace or CallerFilePath), but don't work on unknown subclassed code.
                    byte[] asmBytes = asm.ToByteArray(); // We could load the on-disk assembly, but this should be much faster, especially for large assemblies with embedded resources.
                    // Turn the compiled assembly into a stream (so we can load it with cecil)
                    using (MemoryStream memStream = new MemoryStream(asmBytes))
                    {
                        // Also load the generated pdb file (which is always placed on the disk and is not held in memory). The pdb should be in the working directory.
                        string pdbFilePath = null;
                        int spot = compilerParams.OutputAssembly.LastIndexOf(".dll");
                        if (spot > -1)
                            pdbFilePath = compilerParams.OutputAssembly.Substring(0, spot) + ".pdb";
                        using (FileStream pdbStream = new FileStream(pdbFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            // Load the assembly with its symbols from the pdb
                            ReaderParameters readerParameters = new ReaderParameters();
                            readerParameters.ReadSymbols = true;
                            readerParameters.SymbolStream = pdbStream;
                            AssemblyDefinition cecilAsmDef = AssemblyDefinition.ReadAssembly(memStream, readerParameters);

                            // Find the source file path of each type, using SequencePoints
                            foreach (Type type in asm.GetTypes().Where(t => t.IsClass && t.IsSubclassOf(typeof(HPlugin))).ToList())
                            {
                                string relPath = "";

                                try
                                {
                                    string foundSourcePath = null;

                                    // Use any method defined in the user's plugin file to get a SequencePoint and thus the source file path
                                    TypeDefinition typeDef = cecilAsmDef.MainModule.GetTypes().Where(x => x.FullName == type.FullName).FirstOrDefault();
                                    foreach (MethodDefinition methodDef in typeDef.Methods)
                                    {
                                        if (foundSourcePath != null)
                                            break;

                                        if (methodDef.Body.Instructions.Count > 0)
                                        {
                                            SequencePoint seqPoint = methodDef.Body.Instructions[0].SequencePoint;
                                            if (seqPoint != null)
                                            {
                                                if (seqPoint.Document != null)
                                                {
                                                    if (!String.IsNullOrEmpty(seqPoint.Document.Url))
                                                        foundSourcePath = seqPoint.Document.Url;
                                                }
                                            }
                                        }
                                    }

                                    string standardizedSourcePath = Path.GetFullPath(foundSourcePath).ToLowerInvariant();
                                    string standardizedRootDir = Path.GetFullPath(configuration.UserFilesRootDirectory).ToLowerInvariant();
                                    int spot2 = standardizedSourcePath.IndexOf(standardizedRootDir);
                                    if (spot2 >= 0)
                                        relPath = (standardizedSourcePath.Substring(0, spot2) + standardizedSourcePath.Substring(spot2 + standardizedRootDir.Length)).TrimStart('\\', '/');
                                }
                                catch (Exception e2) { } // Just swallow it. The plugin probably broke some protocol and thus will not have persistent savedata.

                                result.CompiledTypesSourceFileRelativePaths[type.FullName] = relPath;
                            }
                        }
                    }
                }
                catch (Exception e) { } // Swallow. Persistent savedata will not work, but the plugin(s) themself will be fine.
            }
        }

        /// <summary>
        /// Deletes all files inside the TemporaryFilesDirectory, then removes the directory.
        /// </summary>
        /// <returns>True if no errors occured, false if otherwise.</returns>
        public static bool ClearTemporaryCompileFiles()
        {
            return TryRemoveDirectory(TemporaryFilesDirectory);
        }

        /// <summary>
        /// Deletes all files inside the specified directory, then removes the directory.
        /// </summary>
        /// <param name="directory">The directory to fully delete.</param>
        /// <returns>True if no errors occured, false if otherwise.</returns>
        public static bool TryRemoveDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    DirectoryInfo topDirInfo = new DirectoryInfo(directory);
                    topDirInfo.Delete(true);
                }

                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }
    }
}
