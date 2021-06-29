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

namespace Microsoft.DotNet.Tools.Sln.Remove
{
    internal class RemoveProjectFromSolutionCommand : CommandBase
    {
        private readonly string _fileOrDirectory;
        private readonly IReadOnlyCollection<string> _arguments;

        public RemoveProjectFromSolutionCommand(ParseResult parseResult) : base(parseResult)
        {
            _fileOrDirectory = parseResult.ValueForArgument<string>(SlnCommandParser.SlnArgument);

            _arguments = parseResult.ValueForArgument(SlnRemoveParser.ProjectPathArgument)?.ToArray() ?? (IReadOnlyCollection<string>)Array.Empty<string>();
            if (_arguments.Count == 0)
            {
                throw new GracefulException(CommonLocalizableStrings.SpecifyAtLeastOneProjectToRemove);
            }

            var slnFile = _arguments.FirstOrDefault(path => path.EndsWith(".sln"));
            if (slnFile != null)
            {
                var projectArgs = string.Join(" ", _arguments.Where(path => !path.EndsWith(".sln")));
                throw new GracefulException(new string[]
                {
                    string.Format(CommonLocalizableStrings.SolutionArgumentMisplaced, slnFile),
                    CommonLocalizableStrings.DidYouMean,
                    $"  dotnet sln {slnFile} remove {projectArgs}"
                });
            }
        }

        public override int Execute()
        {
            SlnFile slnFile = SlnFileFactory.CreateFromFileOrDirectory(_fileOrDirectory);

            var baseDirectory = PathUtility.EnsureTrailingSlash(slnFile.BaseDirectory);
            var relativeProjectPaths = _arguments.Select(p => {
                var fullPath = Path.GetFullPath(p);
                return Path.GetRelativePath(
                    baseDirectory,
                    Directory.Exists(fullPath) ?
                        MsbuildProject.GetProjectFileFromDirectory(fullPath).FullName :
                        fullPath
                );
            }).ToArray();

            bool slnChanged = false;
            foreach (var path in relativeProjectPaths)
            {
                slnChanged |= slnFile.RemoveProject(path);
            }

            slnFile.RemoveEmptyConfigurationSections();

            slnFile.RemoveEmptySolutionFolders();

            if (slnChanged)
            {
                slnFile.Write();
            }

            return 0;
        }
    }
}
