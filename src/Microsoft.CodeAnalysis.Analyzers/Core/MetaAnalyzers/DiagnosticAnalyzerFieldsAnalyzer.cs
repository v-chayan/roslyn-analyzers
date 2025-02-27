﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.Analyzers.MetaAnalyzers.Helpers;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.Analyzers.MetaAnalyzers
{
    using static CodeAnalysisDiagnosticsResources;

    public abstract class DiagnosticAnalyzerFieldsAnalyzer<TClassDeclarationSyntax, TStructDeclarationSyntax, TFieldDeclarationSyntax, TTypeSyntax, TVariableTypeDeclarationSyntax> : DiagnosticAnalyzerCorrectnessAnalyzer
        where TClassDeclarationSyntax : SyntaxNode
        where TStructDeclarationSyntax : SyntaxNode
        where TFieldDeclarationSyntax : SyntaxNode
        where TTypeSyntax : SyntaxNode
        where TVariableTypeDeclarationSyntax : SyntaxNode
    {
        private static readonly string s_compilationTypeFullName = typeof(Compilation).FullName;
        private static readonly string s_symbolTypeFullName = typeof(ISymbol).FullName;
        private static readonly string s_operationTypeFullName = typeof(IOperation).FullName;

        public static readonly DiagnosticDescriptor DoNotStorePerCompilationDataOntoFieldsRule = new(
            DiagnosticIds.DoNotStorePerCompilationDataOntoFieldsRuleId,
            CreateLocalizableResourceString(nameof(DoNotStorePerCompilationDataOntoFieldsTitle)),
            CreateLocalizableResourceString(nameof(DoNotStorePerCompilationDataOntoFieldsMessage)),
            DiagnosticCategory.MicrosoftCodeAnalysisPerformance,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: CreateLocalizableResourceString(nameof(DoNotStorePerCompilationDataOntoFieldsDescription), nameof(AnalysisContext), DiagnosticWellKnownNames.RegisterCompilationStartActionName),
            customTags: WellKnownDiagnosticTagsExtensions.Telemetry);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(DoNotStorePerCompilationDataOntoFieldsRule);

#pragma warning disable RS1025 // Configure generated code analysis
        public override void Initialize(AnalysisContext context)
#pragma warning restore RS1025 // Configure generated code analysis
        {
            context.EnableConcurrentExecution();

            base.Initialize(context);
        }

        [SuppressMessage("AnalyzerPerformance", "RS1012:Start action has no registered actions.", Justification = "Method returns an analyzer that is registered by the caller.")]
        protected override DiagnosticAnalyzerSymbolAnalyzer? GetDiagnosticAnalyzerSymbolAnalyzer(CompilationStartAnalysisContext compilationContext, INamedTypeSymbol diagnosticAnalyzer, INamedTypeSymbol diagnosticAnalyzerAttribute)
        {
            Compilation compilation = compilationContext.Compilation;

            INamedTypeSymbol? compilationType = compilation.GetOrCreateTypeByMetadataName(s_compilationTypeFullName);
            if (compilationType == null)
            {
                return null;
            }

            INamedTypeSymbol? symbolType = compilation.GetOrCreateTypeByMetadataName(s_symbolTypeFullName);
            if (symbolType == null)
            {
                return null;
            }

            INamedTypeSymbol? operationType = compilation.GetOrCreateTypeByMetadataName(s_operationTypeFullName);
            if (operationType == null)
            {
                return null;
            }

            var attributeUsageAttribute = compilation.GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemAttributeUsageAttribute);

            return new FieldsAnalyzer(compilationType, symbolType, operationType, attributeUsageAttribute, diagnosticAnalyzer, diagnosticAnalyzerAttribute);
        }

        private sealed class FieldsAnalyzer : SyntaxNodeWithinAnalyzerTypeCompilationAnalyzer<TClassDeclarationSyntax, TStructDeclarationSyntax, TFieldDeclarationSyntax>
        {
            private readonly INamedTypeSymbol _compilationType;
            private readonly INamedTypeSymbol _symbolType;
            private readonly INamedTypeSymbol _operationType;
            private readonly INamedTypeSymbol? _attributeUsageAttribute;

            public FieldsAnalyzer(INamedTypeSymbol compilationType, INamedTypeSymbol symbolType, INamedTypeSymbol operationType, INamedTypeSymbol? attributeUsageAttribute, INamedTypeSymbol diagnosticAnalyzer, INamedTypeSymbol diagnosticAnalyzerAttribute)
                : base(diagnosticAnalyzer, diagnosticAnalyzerAttribute)
            {
                _compilationType = compilationType;
                _symbolType = symbolType;
                _operationType = operationType;
                _attributeUsageAttribute = attributeUsageAttribute;
            }

            protected override void AnalyzeDiagnosticAnalyzer(SymbolAnalysisContext symbolContext)
            {
                var namedType = (INamedTypeSymbol)symbolContext.Symbol;
                if (!HasDiagnosticAnalyzerAttribute(namedType, _attributeUsageAttribute))
                {
                    // We are interested only in DiagnosticAnalyzer types with DiagnosticAnalyzerAttribute.
                    return;
                }

                base.AnalyzeDiagnosticAnalyzer(symbolContext);
            }

            protected override void AnalyzeNode(SymbolAnalysisContext symbolContext, TFieldDeclarationSyntax syntaxNode, SemanticModel semanticModel)
            {
                // Get all the type syntax nodes within the topmost type declaration nodes for field declarations.
                System.Collections.Generic.IEnumerable<TVariableTypeDeclarationSyntax> variableTypeDeclarations = syntaxNode.DescendantNodesAndSelf().OfType<TVariableTypeDeclarationSyntax>();
                System.Collections.Generic.IEnumerable<TTypeSyntax> topMostTypeNodes = variableTypeDeclarations.SelectMany(typeDecl => typeDecl.ChildNodes().OfType<TTypeSyntax>());
                System.Collections.Generic.IEnumerable<TTypeSyntax> typeNodes = topMostTypeNodes.SelectMany(t => t.DescendantNodesAndSelf().OfType<TTypeSyntax>());

                foreach (TTypeSyntax typeNode in typeNodes)
                {
                    ITypeSymbol? type = semanticModel.GetTypeInfo(typeNode, symbolContext.CancellationToken).Type;
                    if (type != null)
                    {
                        foreach (ITypeSymbol innerType in type.GetBaseTypesAndThis())
                        {
                            if (SymbolEqualityComparer.Default.Equals(innerType, _compilationType))
                            {
                                ReportDiagnostic(type, typeNode, symbolContext);
                                return;
                            }
                        }

                        foreach (INamedTypeSymbol iface in type.AllInterfaces)
                        {
                            if (SymbolEqualityComparer.Default.Equals(iface, _symbolType) || SymbolEqualityComparer.Default.Equals(iface, _operationType))
                            {
                                ReportDiagnostic(type, typeNode, symbolContext);
                                return;
                            }
                        }
                    }
                }
            }

            private static void ReportDiagnostic(ITypeSymbol type, TTypeSyntax typeSyntax, SymbolAnalysisContext context)
            {
                Diagnostic diagnostic = typeSyntax.CreateDiagnostic(DoNotStorePerCompilationDataOntoFieldsRule, type.ToDisplayString());
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
