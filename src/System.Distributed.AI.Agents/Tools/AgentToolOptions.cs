// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using System.Text.Json;

namespace System.Distributed.AI.Agents.Tools;

/// <summary>
/// Represents options that can be provided when creating an <see cref="AIFunction"/> from a method.
/// </summary>
public sealed class AgentToolOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentToolOptions"/> class.
    /// </summary>
    public AgentToolOptions()
    {
    }

    /// <summary>Gets or sets the <see cref="JsonSerializerOptions"/> used to marshal .NET values being passed to the underlying delegate.</summary>
    /// <remarks>
    /// If no value has been specified, the <see cref="AIJsonUtilities.DefaultOptions"/> instance will be used.
    /// </remarks>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="AIJsonSchemaCreateOptions"/> governing the generation of JSON schemas for the function.
    /// </summary>
    /// <remarks>
    /// If no value has been specified, the <see cref="AIJsonSchemaCreateOptions.Default"/> instance will be used.
    /// </remarks>
    public AIJsonSchemaCreateOptions? JsonSchemaCreateOptions { get; set; }
}
