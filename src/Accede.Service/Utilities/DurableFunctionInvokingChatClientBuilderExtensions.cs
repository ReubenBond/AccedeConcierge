// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Accede.Service.Utilities;

/// <summary>
/// Provides extension methods for attaching a <see cref="DurableFunctionInvokingChatClient"/> to a chat pipeline.
/// </summary>
public static class DurableFunctionInvokingChatClientBuilderExtensions
{
    /// <summary>
    /// Enables automatic function call invocation on the chat pipeline.
    /// </summary>
    /// <remarks>This works by adding an instance of <see cref="DurableFunctionInvokingChatClient"/> with default options.</remarks>
    /// <param name="builder">The <see cref="ChatClientBuilder"/> being used to build the chat pipeline.</param>
    /// <param name="loggerFactory">An optional <see cref="ILoggerFactory"/> to use to create a logger for logging function invocations.</param>
    /// <param name="configure">An optional callback that can be used to configure the <see cref="DurableFunctionInvokingChatClient"/> instance.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ChatClientBuilder UseDurableFunctionInvocation(
        this ChatClientBuilder builder,
        ILoggerFactory? loggerFactory = null,
        Action<DurableFunctionInvokingChatClient>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= services.GetService<ILoggerFactory>();

            var chatClient = new DurableFunctionInvokingChatClient(innerClient, loggerFactory?.CreateLogger(typeof(DurableFunctionInvokingChatClient)));
            configure?.Invoke(chatClient);
            return chatClient;
        });
    }
}
