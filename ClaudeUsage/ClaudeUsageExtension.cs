using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace ClaudeUsage;

[Guid("48C292C7-73DB-49F4-88AF-3017AB6ECC09")]
public sealed partial class ClaudeUsageExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly ClaudeUsageCommandsProvider _provider = new();

    public ClaudeUsageExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose() => _extensionDisposedEvent.Set();
}
