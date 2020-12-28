﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Navigation;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.NavigateTo
{
    internal abstract partial class AbstractNavigateToSearchService
    {
        private class SearchResult : INavigateToSearchResult
        {
            public string AdditionalInformation { get; }
            public string Kind { get; }
            public NavigateToMatchKind MatchKind { get; }
            public bool IsCaseSensitive { get; }
            public string Name { get; }
            public ImmutableArray<TextSpan> NameMatchSpans { get; }
            public string SecondarySort { get; }
            public string? Summary => null;

            public INavigableItem NavigableItem { get; }

            public SearchResult(
                Document document,
                DeclaredSymbolInfo declaredSymbolInfo,
                string kind,
                NavigateToMatchKind matchKind,
                bool isCaseSensitive,
                INavigableItem navigableItem,
                ImmutableArray<TextSpan> nameMatchSpans,
                ImmutableArray<Project> additionalMatchingProjects)
            {
                Name = declaredSymbolInfo.Name;
                Kind = kind;
                MatchKind = matchKind;
                IsCaseSensitive = isCaseSensitive;
                NavigableItem = navigableItem;
                NameMatchSpans = nameMatchSpans;
                AdditionalInformation = ComputeAdditionalInformation(document, declaredSymbolInfo, additionalMatchingProjects);
                SecondarySort = ConstructSecondarySortString(document, declaredSymbolInfo);
            }

            private static string ComputeAdditionalInformation(Document document, DeclaredSymbolInfo declaredSymbolInfo, ImmutableArray<Project> additionalMatchingProjects)
            {
                var projectName = ComputeProjectName(document, additionalMatchingProjects);
                switch (declaredSymbolInfo.Kind)
                {
                    case DeclaredSymbolInfoKind.Class:
                    case DeclaredSymbolInfoKind.Record:
                    case DeclaredSymbolInfoKind.Enum:
                    case DeclaredSymbolInfoKind.Interface:
                    case DeclaredSymbolInfoKind.Module:
                    case DeclaredSymbolInfoKind.Struct:
                        if (!declaredSymbolInfo.IsNestedType)
                        {
                            return string.Format(FeaturesResources.project_0, projectName);
                        }
                        break;
                }

                return string.Format(FeaturesResources.in_0_project_1, declaredSymbolInfo.ContainerDisplayName, projectName);
            }

            private static string ComputeProjectName(Document document, ImmutableArray<Project> additionalMatchingProjects)
            {
                if (TryComputeMergedProjectName(document, additionalMatchingProjects, out var mergedName))
                    return mergedName;

                // Couldn't compute a merged project name (or only had one project).  Just return the name of hte project itself.
                return document.Project.Name;
            }

            private static bool TryComputeMergedProjectName(
                Document document, ImmutableArray<Project> additionalMatchingProjects, [NotNullWhen(true)] out string? mergedName)
            {
                mergedName = null;
                if (additionalMatchingProjects.Length == 0)
                    return false;

                var firstProject = document.Project;
                var (firstProjectName, firstProjectFlavor) = firstProject.State.NameAndFlavor;
                if (firstProjectName == null)
                    return false;

                using var _ = ArrayBuilder<string>.GetInstance(out var flavors);
                flavors.Add(firstProjectFlavor!);

                foreach (var additionalProject in additionalMatchingProjects)
                {
                    var (projectName, projectFlavor) = additionalProject.State.NameAndFlavor;
                    if (projectName != firstProjectName)
                        return false;

                    flavors.Add(projectFlavor!);
                }

                flavors.RemoveDuplicates();
                flavors.Sort();

                mergedName = $"{firstProjectName} ({string.Join(", ", flavors)})";
                return true;
            }

            private static readonly char[] s_dotArray = { '.' };

            private static string ConstructSecondarySortString(Document document, DeclaredSymbolInfo declaredSymbolInfo)
            {
                using var _ = ArrayBuilder<string>.GetInstance(out var parts);

                parts.Add(declaredSymbolInfo.ParameterCount.ToString("X4"));
                parts.Add(declaredSymbolInfo.TypeParameterCount.ToString("X4"));
                parts.Add(declaredSymbolInfo.Name);

                // For partial types, we break up the file name into pieces.  i.e. If we have
                // Outer.cs and Outer.Inner.cs  then we add "Outer" and "Outer Inner" to 
                // the secondary sort string.  That way "Outer.cs" will be weighted above
                // "Outer.Inner.cs"
                var fileName = Path.GetFileNameWithoutExtension(document.FilePath ?? "");
                parts.AddRange(fileName.Split(s_dotArray));

                return string.Join(" ", parts);
            }
        }
    }
}
