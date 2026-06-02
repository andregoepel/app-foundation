using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AndreGoepel.Website.Tests.Components;

// Base context for the website error component tests. ErrorPage and ErrorLayout
// bootstrap their theme by calling the JS function "site.getInitialState" inside
// OnAfterRenderAsync, which bUnit invokes during render. This base supplies a
// fake IJSRuntime so rendering does not throw on the null result a loose mock
// would return. The fake builds whatever InitialState record the caller expects
// — each component declares its own private copy with the same (Lang, Theme,
// ResolvedTheme) shape — and returns default for every other call.
public abstract class ErrorComponentContext : BunitContext
{
    protected ErrorComponentContext()
    {
        Services.AddSingleton<IJSRuntime>(new FakeJsRuntime());
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "site.getInitialState")
            {
                var state = Activator.CreateInstance(typeof(TValue), "en", "light", "light");
                return new ValueTask<TValue>((TValue)state!);
            }

            return new ValueTask<TValue>(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }
}
