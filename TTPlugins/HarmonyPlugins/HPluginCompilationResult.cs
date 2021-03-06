﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace com.tiberiumfusion.ttplugins.HarmonyPlugins
{
    /// <summary>
    /// A bundle of data produced by HPluginAssemblyCompiler.Compile()
    /// </summary>
    public class HPluginCompilationResult
    {
        /// <summary>
        /// The compiled usercode assemblies.
        /// </summary>
        public List<Assembly> CompiledAssemblies { get; set; } = new List<Assembly>();

        /// <summary>
        /// List of any compiler errors.
        /// </summary>
        public List<CompilerError> CompileErrors { get; set; } = new List<CompilerError>();

        /// <summary>
        /// If true, a generic exception was thrown during compilation.
        /// </summary>
        public bool GenericCompilationFailure { get; set; } = false;

        /// <summary>
        /// Dictionary that maps the full name of each compiled HPlugin to the relative path of the source file used to compile it.
        /// </summary>
        public Dictionary<string, string> CompiledTypesSourceFileRelativePaths { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// A list of the paths of all output files generated during the compile process. Will include both assembly DLLs and their corresponding PDBs.
        /// </summary>
        public List<string> OutputFilesOnDisk { get; set; } = new List<string>();

        /// <summary>
        /// The root directory containing the files in OutputFilesOnDisk.
        /// </summary>
        public string OutputDirectory { get; set; }
    }
}
