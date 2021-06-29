// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Sln.Internal;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Common;

namespace Microsoft.DotNet.Tools.Sln.Add
{
    internal class AddProjectToSolutionCommand : CommandBase
    {
        private readonly string _fileOrDirectory;
        private readonly bool _inRoot;
        private readonly IList<string> _relativeRootSolutionFolders;
        private readonly IReadOnlyCollection<string> _arguments;

        public AddProjectToSolutionCommand(ParseResult parseResult) : base(parseResult)
        {
            _fileOrDirectory = parseResult.ValueForArgument<string>(SlnCommandParser.SlnArgument);

            _arguments = parseResult.ValueForArgument(SlnAddParser.ProjectPathArgument)?.ToArray() ?? (IReadOnlyCollection<string>)Array.Empty<string>();
            if (_arguments.Count == 0)
            {
                throw new GracefulException(CommonLocalizableStrings.SpecifyAtLeastOneProjectToAdd);
            }

            _inRoot = parseResult.ValueForOption<bool>(SlnAddParser.InRootOption);
            string relativeRoot = parseResult.ValueForOption<string>(SlnAddParser.SolutionFolderOption);
            bool hasRelativeRoot = !string.IsNullOrEmpty(relativeRoot);
            
            if (_inRoot && hasRelativeRoot)
            {
                // These two options are mutually exclusive
                throw new GracefulException(LocalizableStrings.SolutionFolderAndInRootMutuallyExclusive);
            }

            if (hasRelativeRoot)
            {
                relativeRoot = PathUtility.GetPathWithDirectorySeparator(relativeRoot);
                _relativeRootSolutionFolders = relativeRoot.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                _relativeRootSolutionFolders = null;
            }

            var slnFile = _arguments.FirstOrDefault(path => path.EndsWith(".sln"));
            if (slnFile != null)
            {
                string args;
                if (_inRoot) args = $"--{SlnAddParser.InRootOption.Name} ";
                else if (hasRelativeRoot) args = $"--{SlnAddParser.SolutionFolderOption.Name} {string.Join(" ", relativeRoot)} ";
                else args = "";

                var projectArgs = string.Join(" ", _arguments.Where(path => !path.EndsWith(".sln")));

                throw new GracefulException(new string[]
                {
                    string.Format(CommonLocalizableStrings.SolutionArgumentMisplaced, slnFile),
                    CommonLocalizableStrings.DidYouMean,
                    $"  dotnet sln {slnFile} add {args}{projectArgs}"
                });
            }
        }

        public override int Execute()
        {
            SlnFile slnFile = SlnFileFactory.CreateFromFileOrDirectory(_fileOrDirectory);

            PathUtility.EnsureAllPathsExist(_arguments, CommonLocalizableStrings.CouldNotFindProjectOrDirectory, true);

            var fullProjectPaths = _arguments.Select(p =>
            {
                var fullPath = Path.GetFullPath(p);
                return Directory.Exists(fullPath) ?
                    MsbuildProject.GetProjectFileFromDirectory(fullPath).FullName :
                    fullPath;
            }).ToArray();

            var preAddProjectCount = slnFile.Projects.Count;

            foreach (var fullProjectPath in fullProjectPaths)
            {
                // Identify the intended solution folders
                var solutionFolders = DetermineSolutionFolder(slnFile, fullProjectPath);

                slnFile.AddProject(fullProjectPath, solutionFolders);
            }

            if (slnFile.Projects.Count > preAddProjectCount)
            {
                slnFile.Write();
            }

            return 0;
        }

        private static IList<string> GetSolutionFoldersFromProjectPath(string projectFilePath)
        {
            var solutionFolders = new List<string>();

            if (!IsPathInTreeRootedAtSolutionDirectory(projectFilePath))
                return solutionFolders;

            var currentDirString = $".{Path.DirectorySeparatorChar}";
            if (projectFilePath.StartsWith(currentDirString))
            {
                projectFilePath = projectFilePath.Substring(currentDirString.Length);
            }

            var projectDirectoryPath = TrimProject(projectFilePath);
            if (string.IsNullOrEmpty(projectDirectoryPath))
                return solutionFolders;

            var solutionFoldersPath = TrimProjectDirectory(projectDirectoryPath);
            if (string.IsNullOrEmpty(solutionFoldersPath))
                return solutionFolders;

            solutionFolders.AddRange(solutionFoldersPath.Split(Path.DirectorySeparatorChar));

            return solutionFolders;
        }

        private IList<string> DetermineSolutionFolder(SlnFile slnFile, string fullProjectPath)
        {
            if (_inRoot)
            {
                // The user requested all projects go to the root folder
                return null;
            }

            if (_relativeRootSolutionFolders != null)
            {
                // The user has specified an explicit root
                return _relativeRootSolutionFolders;
            }

            // We determine the root for each individual project
            var relativeProjectPath = Path.GetRelativePath(
                PathUtility.EnsureTrailingSlash(slnFile.BaseDirectory),
                fullProjectPath);

            return GetSolutionFoldersFromProjectPath(relativeProjectPath);
        }

        private static bool IsPathInTreeRootedAtSolutionDirectory(string path)
        {
            return !path.StartsWith("..");
        }

        private static string TrimProject(string path)
        {
            return Path.GetDirectoryName(path);
        }

        private static string TrimProjectDirectory(string path)
        {
            return Path.GetDirectoryName(path);
        }
    }
}
