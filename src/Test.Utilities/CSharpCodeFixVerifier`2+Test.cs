﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace Test.Utilities
{
    public static partial class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        public class Test : CSharpCodeFixTest<TAnalyzer, TCodeFix, XUnitVerifier>
        {
            static Test()
            {
                // If we have outdated defaults from the host unit test application targeting an older .NET Framework, use more
                // reasonable TLS protocol version for outgoing connections.
#pragma warning disable CA5364 // Do Not Use Deprecated Security Protocols
#pragma warning disable CS0618 // Type or member is obsolete
                if (ServicePointManager.SecurityProtocol == (SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls))
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CA5364 // Do Not Use Deprecated Security Protocols
                {
#pragma warning disable CA5386 // Avoid hardcoding SecurityProtocolType value
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#pragma warning restore CA5386 // Avoid hardcoding SecurityProtocolType value
                }
            }

            internal static readonly ImmutableDictionary<string, ReportDiagnostic> NullableWarnings = GetNullableWarningsFromCompiler();

            public Test()
            {
                ReferenceAssemblies = AdditionalMetadataReferences.Default;
            }

            private static ImmutableDictionary<string, ReportDiagnostic> GetNullableWarningsFromCompiler()
            {
                string[] args = { "/warnaserror:nullable" };
                var commandLineArguments = CSharpCommandLineParser.Default.Parse(args, baseDirectory: Environment.CurrentDirectory, sdkDirectory: Environment.CurrentDirectory);
                var nullableWarnings = commandLineArguments.CompilationOptions.SpecificDiagnosticOptions;

                return nullableWarnings;
            }

            public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp7_3;

            protected override CompilationOptions CreateCompilationOptions()
            {
                var compilationOptions = base.CreateCompilationOptions();
                return compilationOptions.WithSpecificDiagnosticOptions(
                    compilationOptions.SpecificDiagnosticOptions.SetItems(NullableWarnings));
            }

            protected override ImmutableArray<(Project project, Diagnostic diagnostic)> SortDistinctDiagnostics(IEnumerable<(Project project, Diagnostic diagnostic)> diagnostics)
            {
                var baseResult = base.SortDistinctDiagnostics(diagnostics);
                if (typeof(DiagnosticSuppressor).IsAssignableFrom(typeof(TAnalyzer)))
                {
                    // Include suppressed diagnostics when testing diagnostic suppressors
                    return baseResult;
                }

                // Treat suppressed diagnostics as non-existent. Normally this wouldn't be necessary, but some of the
                // tests include diagnostics reported in code wrapped in '#pragma warning disable'.
                return baseResult.WhereAsArray(diagnostic => !diagnostic.diagnostic.IsSuppressed);
            }

            protected override ParseOptions CreateParseOptions()
            {
                return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion);
            }
        }
    }
}
