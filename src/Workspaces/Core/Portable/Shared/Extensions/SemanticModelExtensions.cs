﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.LanguageService;
using Microsoft.CodeAnalysis.Shared.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Shared.Extensions;

internal static partial class SemanticModelExtensions
{
    public static SemanticMap GetSemanticMap(this SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
        => SemanticMap.From(semanticModel, node, cancellationToken);

    private static ISymbol? MapSymbol(ISymbol? symbol, ITypeSymbol? type)
    {
        if (symbol.IsConstructor() && symbol.ContainingType.IsAnonymousType)
        {
            return symbol.ContainingType;
        }

        if (symbol.IsThisParameter())
        {
            // Map references to this/base to the actual type that those correspond to.
            return type;
        }

        if (symbol.IsFunctionValue() &&
            symbol.ContainingSymbol is IMethodSymbol method)
        {
            if (method.AssociatedSymbol != null)
            {
                return method.AssociatedSymbol;
            }
            else
            {
                return method;
            }
        }

        // see if we can map the built-in language operator to a real method on the containing
        // type of the symbol.  built-in operators can happen when querying the semantic model
        // for operators.  However, we would prefer to just use the real operator on the type
        // if it has one.
        if (symbol is IMethodSymbol methodSymbol &&
            methodSymbol.MethodKind == MethodKind.BuiltinOperator &&
            methodSymbol.ContainingType is ITypeSymbol containingType)
        {
            var comparer = SymbolEquivalenceComparer.Instance.ParameterEquivalenceComparer;

            // Note: this will find the real method vs the built-in.  That's because the
            // built-in is synthesized operator that isn't actually in the list of members of
            // its 'ContainingType'.
            var mapped = containingType.GetMembers(methodSymbol.Name)
                                       .OfType<IMethodSymbol>()
                                       .FirstOrDefault(s => s.Parameters.SequenceEqual(methodSymbol.Parameters, comparer));
            symbol = mapped ?? symbol;
        }

        return symbol;
    }

    public static TokenSemanticInfo GetSemanticInfo(
        this SemanticModel semanticModel,
        SyntaxToken token,
        SolutionServices services,
        CancellationToken cancellationToken)
    {
        var languageServices = services.GetLanguageServices(token.Language);
        var syntaxFacts = languageServices.GetRequiredService<ISyntaxFactsService>();
        var syntaxKinds = languageServices.GetRequiredService<ISyntaxKindsService>();

        if (!syntaxFacts.IsBindableToken(semanticModel, token))
            return TokenSemanticInfo.Empty;

        var semanticFacts = languageServices.GetRequiredService<ISemanticFactsService>();
        var overridingIdentifier = syntaxFacts.GetDeclarationIdentifierIfOverride(token);

        IAliasSymbol? aliasSymbol = null;
        ITypeSymbol? type = null;
        ITypeSymbol? convertedType = null;
        ISymbol? declaredSymbol = null;
        IPreprocessingSymbol? preprocessingSymbol = null;
        var allSymbols = ImmutableArray<ISymbol?>.Empty;

        var tokenParent = token.Parent;
        if (token.RawKind == syntaxKinds.UsingKeyword &&
            (tokenParent?.RawKind == syntaxKinds.UsingStatement || tokenParent?.RawKind == syntaxKinds.LocalDeclarationStatement))
        {
            var usingStatement = token.Parent;
            declaredSymbol = semanticFacts.TryGetDisposeMethod(semanticModel, tokenParent, cancellationToken);
        }
        else if (overridingIdentifier.HasValue)
        {
            // on an "override" token, we'll find the overridden symbol
            var overridingSymbol = semanticFacts.GetDeclaredSymbol(semanticModel, overridingIdentifier.Value, cancellationToken);
            var overriddenSymbol = overridingSymbol.GetOverriddenMember(allowLooseMatch: true);

            allSymbols = overriddenSymbol is null ? [] : [overriddenSymbol];
        }
        else
        {
            Debug.Assert(tokenParent is not null);
            aliasSymbol = semanticModel.GetAliasInfo(tokenParent, cancellationToken);
            var bindableParent = syntaxFacts.TryGetBindableParent(token);
            var typeInfo = bindableParent != null ? semanticModel.GetTypeInfo(bindableParent, cancellationToken) : default;
            type = typeInfo.Type;
            convertedType = typeInfo.ConvertedType;
            declaredSymbol = MapSymbol(semanticFacts.GetDeclaredSymbol(semanticModel, token, cancellationToken), type);
            preprocessingSymbol = semanticFacts.GetPreprocessingSymbol(semanticModel, tokenParent);

            if (preprocessingSymbol != null)
            {
                allSymbols = [preprocessingSymbol];
            }
            else if (!declaredSymbol.IsKind(SymbolKind.RangeVariable))
            {
                allSymbols = semanticFacts
                    .GetBestOrAllSymbols(semanticModel, bindableParent, token, cancellationToken)
                    .WhereAsArray(s => s != null && !s.Equals(declaredSymbol))
                    .SelectAsArray(s => MapSymbol(s, type));
            }
        }

        // NOTE(cyrusn): This is a workaround to how the semantic model binds and returns
        // information for VB event handlers.  Namely, if you have:
        //
        // Event X]()
        // Sub Goo()
        //      Dim y = New $$XEventHandler(AddressOf bar)
        // End Sub
        //
        // Only GetTypeInfo will return any information for XEventHandler.  So, in this
        // case, we upgrade the type to be the symbol we return.
        if (type is INamedTypeSymbol namedType && allSymbols.Length == 0)
        {
            if (namedType.TypeKind == TypeKind.Delegate ||
                namedType.AssociatedSymbol != null)
            {
                allSymbols = [type];
                type = null;
            }
        }

        if (allSymbols.Length == 0 && syntaxFacts.IsQueryKeyword(token))
        {
            type = null;
            convertedType = null;
        }

        return new TokenSemanticInfo(declaredSymbol, preprocessingSymbol, aliasSymbol, allSymbols, type, convertedType, token.Span);
    }
}
