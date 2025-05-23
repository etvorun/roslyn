﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// helper type to package diagnostic arguments to pass around between remote hosts
/// </summary>
[DataContract]
internal sealed class DiagnosticArguments
{
    /// <summary>
    /// Flag indicating if analyzer performance info, such as analyzer execution times,
    /// should be logged as performance telemetry.
    /// </summary>
    [DataMember(Order = 0)]
    public bool LogPerformanceInfo;

    /// <summary>
    /// Flag indicating if the analyzer telemety info, such as registered analyzer action counts
    /// and analyzer execution times, should be included in the computed result.
    /// </summary>
    [DataMember(Order = 1)]
    public bool GetTelemetryInfo;

    /// <summary>
    /// Optional document ID, if computing diagnostics for a specific document.
    /// For example, diagnostic computation for open file analysis.
    /// </summary>
    [DataMember(Order = 2)]
    public DocumentId? DocumentId;

    /// <summary>
    /// Optional document text span, if computing diagnostics for a specific span for a document.
    /// For example, diagnostic computation for light bulb invocation for a specific line in active document.
    /// </summary>
    [DataMember(Order = 3)]
    public TextSpan? DocumentSpan;

    /// <summary>
    /// Optional <see cref="AnalysisKind"/>, if computing specific kind of diagnostics for a document request,
    /// i.e. <see cref="DocumentId"/> must be non-null for a non-null analysis kind.
    /// Only supported non-null values are <see cref="AnalysisKind.Syntax"/> and <see cref="AnalysisKind.Semantic"/>.
    /// </summary>
    [DataMember(Order = 4)]
    public AnalysisKind? DocumentAnalysisKind;

    /// <summary>
    /// Project ID for the document or project for which diagnostics need to be computed.
    /// </summary>
    [DataMember(Order = 5)]
    public ProjectId ProjectId;

    /// <summary>
    /// Array of analyzer IDs for analyzers that need to be executed for computing diagnostics.
    /// </summary>
    [DataMember(Order = 6)]
    public ImmutableArray<string> ProjectAnalyzerIds;

    /// <summary>
    /// Array of analyzer IDs for analyzers that need to be executed for computing diagnostics.
    /// </summary>
    [DataMember(Order = 7)]
    public ImmutableArray<string> HostAnalyzerIds;

    public DiagnosticArguments(
        bool logPerformanceInfo,
        bool getTelemetryInfo,
        DocumentId? documentId,
        TextSpan? documentSpan,
        AnalysisKind? documentAnalysisKind,
        ProjectId projectId,
        ImmutableArray<string> projectAnalyzerIds,
        ImmutableArray<string> hostAnalyzerIds)
    {
        Debug.Assert(documentId != null || documentSpan == null);
        Debug.Assert(documentId != null || documentAnalysisKind == null);
        Debug.Assert(documentAnalysisKind is null or
            (AnalysisKind?)AnalysisKind.Syntax or (AnalysisKind?)AnalysisKind.Semantic);
        Debug.Assert(projectAnalyzerIds.Length > 0 || hostAnalyzerIds.Length > 0);

        LogPerformanceInfo = logPerformanceInfo;
        GetTelemetryInfo = getTelemetryInfo;
        DocumentId = documentId;
        DocumentSpan = documentSpan;
        DocumentAnalysisKind = documentAnalysisKind;
        ProjectId = projectId;
        ProjectAnalyzerIds = projectAnalyzerIds;
        HostAnalyzerIds = hostAnalyzerIds;
    }
}
