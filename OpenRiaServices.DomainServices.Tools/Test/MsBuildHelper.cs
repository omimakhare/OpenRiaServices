﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.BuildEngine;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenRiaServices.DomainServices.Tools.Test
{
    /// <summary>
    /// Helper class for common MSBuild tasks
    /// </summary>
    public static class MsBuildHelper
    {
        /// <summary>
        /// Extract the list of assemblies both generated and referenced by the named project.
        /// </summary>
        /// <returns></returns>
        public static List<string> GetReferenceAssemblies(string projectPath)
        {
            List<string> assemblies = new List<string>();
            GetReferenceAssemblies(projectPath, assemblies);
            return assemblies;
        }

        /// <summary>
        /// Adds the assembly references from the given project to the given list
        /// </summary>
        /// <param name="projectPath">Absolute path to the project file itself</param>
        /// <param name="assemblies">List to add assembly names to</param>
        public static void GetReferenceAssemblies(string projectPath, IList<string> assemblies)
        {
            projectPath = Path.GetFullPath(projectPath);

            Engine engine = new Engine();
            var project = LoadProject(projectPath, engine);

            // Ask to be told of generated outputs
            IDictionary targetOutputs = new Dictionary<object, object>();
            string[] buildTargets = new string[] { "ResolveAssemblyReferences" };

            bool success = engine.BuildProject(project, buildTargets, targetOutputs);
            if (success)
            {
                BuildItemGroup buildItems = project.EvaluatedItems;
                foreach (BuildItem buildItem in buildItems)
                {
                    string otherProjectPath = buildItem.FinalItemSpec;
                    if (!Path.IsPathRooted(otherProjectPath))
                    {
                        otherProjectPath = Path.Combine(Path.GetDirectoryName(projectPath), otherProjectPath);
                    }

                    if (buildItem.Name.Equals("_ResolveAssemblyReferenceResolvedFiles", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!assemblies.Contains(otherProjectPath))
                            assemblies.Add(otherProjectPath);
                    }
                    else if (buildItem.Name.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase))
                    {
                        // Project references recursively extract references
                        string outputAssembly = GetOutputAssembly(otherProjectPath);

                        if (!string.IsNullOrEmpty(outputAssembly) && !assemblies.Contains(outputAssembly))
                            assemblies.Add(outputAssembly);
                    }
                }
            }
            MakeFullPaths(assemblies, Path.GetDirectoryName(projectPath));
        }

        /// <summary>
        /// Gets the absolute path of the output assembly generated by the specified project
        /// </summary>
        /// <param name="projectPath">Absolute path to the project file</param>
        /// <returns>Absolute path to the generated output assembly (which may or may not exist)</returns>
        public static string GetOutputAssembly(string projectPath)
        {
            string outputAssembly = null;
            projectPath = Path.GetFullPath(projectPath);

            Engine engine = new Engine();
            var project = LoadProject(projectPath, engine);

            string outputPath = project.GetEvaluatedProperty("OutputPath");
            string assemblyName = project.GetEvaluatedProperty("AssemblyName");
            string outputType = project.GetEvaluatedProperty("OutputType");

            if (!Path.IsPathRooted(outputPath))
                outputPath = Path.Combine(Path.GetDirectoryName(projectPath), outputPath);
            outputAssembly = Path.Combine(outputPath, assemblyName);
            outputAssembly = Path.GetFullPath(outputAssembly);

            string extension = outputType.Equals("Exe", StringComparison.InvariantCultureIgnoreCase) ? ".exe" : ".dll";
            outputAssembly += extension;
            return MakeFullPath(outputAssembly, Path.GetDirectoryName(projectPath));
        }

        private static Project LoadProject(string projectPath, Engine engine)
        {
            engine.DefaultToolsVersion = "4.0";

            var project = new Project(engine);
            project.Load(projectPath);
#if SIGNED
            project.SetProperty("Configuration", "Signed");
#elif DEBUG
            project.SetProperty("Configuration", "Debug");
#else
            project.SetProperty("Configuration", "Release");
#endif
            project.SetProperty("BuildProjectReferences", "false");
            return project;
        }

        /// <summary>
        /// Gets the source files used by the given project
        /// </summary>
        /// <param name="projectPath">Absolute path to the project file itself</param>
        public static List<string> GetSourceFiles(string projectPath)
        {
            List<string> items = new List<string>();

            projectPath = Path.GetFullPath(projectPath);

            Engine engine = new Engine();
            var project = LoadProject(projectPath, engine);

            ErrorLogger logger = new ErrorLogger();
            engine.RegisterLogger(logger);

            // Ask to be told of generated outputs
            IDictionary targetOutputs = new Dictionary<object, object>();
            string[] buildTargets = new string[] { "Build" };

            bool success = engine.BuildProject(project, buildTargets, targetOutputs);
            if (!success)
            {
                string message = string.Join(Environment.NewLine, logger.Errors.ToArray());
                Assert.Fail(message);
            }

            BuildItemGroup buildItems = project.EvaluatedItems;
            foreach (BuildItem buildItem in buildItems)
            {
                if (buildItem.Name.Equals("Compile", StringComparison.OrdinalIgnoreCase))
                {
                    items.Add(buildItem.FinalItemSpec);
                }
            }

            MakeFullPaths(items, Path.GetDirectoryName(projectPath));
            return items;
        }

        /// <summary>
        /// Expands any relative paths to be full paths, using the given base directory
        /// </summary>
        /// <param name="files"></param>
        /// <param name="baseDir"></param>
        public static void MakeFullPaths(IList<string> files, string baseDir)
        {
            for (int i = 0; i < files.Count; ++i)
            {
                files[i] = MakeFullPath(files[i], baseDir);
            }
        }

        public static string MakeFullPath(string file, string baseDir)
        {
            if (!Path.IsPathRooted(file))
            {
                file = Path.Combine(baseDir, file);
            }
            if (file.Contains(".."))
            {
                file = Path.GetFullPath(file);
            }
            return file;
        }

        /// <summary>
        /// Converts a collection of strings to a collection of task items.
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<ITaskItem> AsTaskItems(IEnumerable<string> items)
        {
            List<ITaskItem> result = new List<ITaskItem>(items.Count());
            foreach (string s in items)
            {
                result.Add(new TaskItem(s));
            }
            return result;
        }

        /// <summary>
        /// Converts a collection of ITaskItem to a collection of strings
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<string> AsStrings(IEnumerable<ITaskItem> items)
        {
            List<string> result = new List<string>(items.Count());
            foreach (ITaskItem item in items)
            {
                result.Add(item.ItemSpec);
            }
            return result;
        }

        private class ErrorLogger : Microsoft.Build.Framework.ILogger
        {
            readonly List<string> _errors = new List<string>();

            public void Initialize(IEventSource eventSource)
            {
                eventSource.ErrorRaised += (s, a) => this._errors.Add($"{a.File}({a.LineNumber},{a.ColumnNumber}): error {a.Code}: {a.Message}");
            }

            public void Shutdown() { }

            public IEnumerable<string> Errors { get { return this._errors; } }
            public string Parameters { get; set; }
            public LoggerVerbosity Verbosity { get; set; }
        }
    }
}
