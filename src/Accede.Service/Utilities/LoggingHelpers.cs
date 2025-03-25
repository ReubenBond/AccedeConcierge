// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

#pragma warning disable CA2213 // Disposable fields should be disposed
#pragma warning disable EA0002 // Use 'System.TimeProvider' to make the code easier to test

namespace Accede.Service.Utilities;

/// <summary>Provides internal helpers for implementing logging.</summary>
internal static class LoggingHelpers
{
    /// <summary>Serializes <paramref name="value"/> as JSON for logging purposes.</summary>
    public static string AsJson<T>(T value, JsonSerializerOptions? options)
    {
        if (options?.TryGetTypeInfo(typeof(T), out var typeInfo) is true ||
            AIJsonUtilities.DefaultOptions.TryGetTypeInfo(typeof(T), out typeInfo))
        {
            try
            {
                return JsonSerializer.Serialize(value, typeInfo);
            }
            catch
            {
            }
        }

        // If we're unable to get a type info for the value, or if we fail to serialize,
        // return an empty JSON object. We do not want lack of type info to disrupt application behavior with exceptions.
        return "{}";
    }
}
