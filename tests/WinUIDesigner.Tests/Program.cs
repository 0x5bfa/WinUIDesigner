using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Action = System.Action;
using WinUIDesigner.Surface;
using WinUIDesigner.Surface.Services;

int failures = 0;
await Test("fragmented frames preserve all header and payload bytes", () =>
{
    byte[] expected = new byte[100];
    BitConverter.GetBytes(expected.Length - 4).CopyTo(expected, 0);
    for (int i = 4; i < expected.Length; i++) expected[i] = (byte)i;
    int position = 0;
    byte[]? actual = MessageFrameReader.Read((buffer, offset, count) =>
    {
        int chunk = Math.Min(3, Math.Min(count, expected.Length - position));
        Array.Copy(expected, position, buffer, offset, chunk); position += chunk; return chunk;
    });
    Check(actual is not null && actual.SequenceEqual(expected));
    return Task.CompletedTask;
});
await Test("EOF between frames is orderly", () => { Check(MessageFrameReader.Read((_, _, _) => 0) is null); return Task.CompletedTask; });
foreach (int cutoff in new[] { 1, 3, 4, 8, 15 })
{
    await Test($"EOF at byte {cutoff} rejects the partial frame", () =>
    {
        byte[] input = new byte[16]; BitConverter.GetBytes(12).CopyTo(input, 0); int position = 0;
        Throws<EndOfStreamException>(() => MessageFrameReader.Read((buffer, offset, count) =>
        {
            int chunk = Math.Min(count, cutoff - position);
            Array.Copy(input, position, buffer, offset, chunk); position += chunk; return chunk;
        }));
        return Task.CompletedTask;
    });
}
foreach (int length in new[] { -1, 0, 11, int.MaxValue, MessageFrameReader.MaximumLength + 1 })
{
    await Test($"invalid frame length {length} is rejected before allocation", () =>
    {
        Throws<InvalidDataException>(() => MessageFrameReader.Read((buffer, offset, _) => { BitConverter.GetBytes(length).CopyTo(buffer, offset); return 4; }));
        return Task.CompletedTask;
    });
}
await Test("UI callback exceptions reach the requester", async () =>
{
    await ThrowsAsync<InvalidOperationException>(() => QueuedOperation.RunAsync<int>(action => { action(); return true; }, () => throw new InvalidOperationException(), TimeSpan.FromSeconds(1), default));
});
await Test("a rejected dispatcher request does not hang", async () =>
{
    await ThrowsAsync<OperationCanceledException>(() => QueuedOperation.RunAsync(_ => false, () => 1, TimeSpan.FromSeconds(1), default));
});
await Test("expired queued requests cannot mutate the document later", async () =>
{
    Action? queued = null; bool changed = false;
    await ThrowsAsync<TimeoutException>(() => QueuedOperation.RunAsync(action => { queued = action; return true; }, () => changed = true, TimeSpan.FromMilliseconds(20), default));
    queued!(); Check(!changed);
});
await Test("shutdown cancels queued work without a late mutation", async () =>
{
    Action? queued = null; bool changed = false; using var cancellation = new CancellationTokenSource();
    Task<bool> pending = QueuedOperation.RunAsync(action => { queued = action; return true; }, () => changed = true, TimeSpan.FromSeconds(1), cancellation.Token);
    cancellation.Cancel(); await ThrowsAsync<OperationCanceledException>(() => pending); queued!(); Check(!changed);
});
await Test("document close releases nonvisual and visual identities", () =>
{
    var registry = new ObjectIdentityRegistry(); var visual = new object(); var resource = new object(); long visualHandle;
    using (registry.EnterDocument(1)) { visualHandle = registry.GetHandle(visual); registry.GetHandle(resource); registry.RegisterMarkupInfo(resource, 27, 3); }
    var released = registry.ReleaseDocument(1);
    Check(released.SetEquals(new[] { visual, resource })); Check(!registry.TryGetObject(visualHandle, out _)); Check(registry.GetSourceInfo(resource) is null);
    Check(registry.GetHandle(new object()) > visualHandle); return Task.CompletedTask;
});
await Test("shared resources survive closing one document", () =>
{
    var registry = new ObjectIdentityRegistry(); var resource = new object(); long handle;
    using (registry.EnterDocument(1)) handle = registry.GetHandle(resource);
    using (registry.EnterDocument(2)) Check(registry.GetHandle(resource) == handle);
    Check(registry.ReleaseDocument(1).Count == 0); Check(registry.TryGetObject(handle, out _));
    Check(registry.ReleaseDocument(2).Contains(resource)); Check(!registry.TryGetObject(handle, out _)); return Task.CompletedTask;
});
await Test("nested construction scopes restore the document owner", () =>
{
    var registry = new ObjectIdentityRegistry(); var first = new object(); var second = new object();
    using (registry.EnterDocument(1)) { using (registry.EnterDocument(2)) registry.GetHandle(second); registry.GetHandle(first); }
    Check(registry.GetDocumentId(first) == 1 && registry.GetDocumentId(second) == 2); return Task.CompletedTask;
});
await Test("source coordinates are returned as independent snapshots", () =>
{
    var registry = new ObjectIdentityRegistry(); var value = new object();
    registry.RegisterSourceInfo(value, new SourceInfo { FileName = "Page.xaml", LineNumber = 7, ColumnNumber = 3 });
    SourceInfo first = registry.GetSourceInfo(value)!; first.LineNumber = 999;
    Check(registry.GetSourceInfo(value)!.LineNumber == 7); return Task.CompletedTask;
});
await Test("overloads use protocol parameter types rather than declaration order", () =>
{
    var method = RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Select), [typeof(string)], false);
    Check((string?)method!.Invoke(new Overloads(), ["value"]) == "string");
    Check(RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Select), [typeof(double)], false) is null);
    Check(RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Factory), [], false) is null);
    Check((string?)RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Factory), [], true)!.Invoke(null, []) == "factory");
    return Task.CompletedTask;
});
await Test("constructor selection does not substitute an unrelated equal-arity overload", () =>
{
    var constructor = RuntimeMemberResolver.FindConstructor(typeof(Overloads), [typeof(int)]);
    Check(((Overloads)constructor!.Invoke([42])).Value == 42);
    Check(RuntimeMemberResolver.FindConstructor(typeof(Overloads), [typeof(double)]) is null);
    return Task.CompletedTask;
});
await Test("construction errors round-trip through the frontend ActionError contract", () =>
{
    var action = new SetSurfaceContentAction(42, 123);
    var error = new DocumentConstructionException(new InvalidOperationException("fixture failure"), action);
    var decoded = ActionErrorJsonSerializer.Deserialize(error.SerializedErrors);
    Check(decoded.Count == 1 && decoded[0].Error.Contains("fixture failure"));
    Check(decoded[0].XamlAction is SetSurfaceContentAction content && content.DocumentId == 42 && content.RootProxyHandle == 123);
    return Task.CompletedTask;
});
await Test("explicit removal detaches document ownership without leaking released objects", () =>
{
    var registry = new ObjectIdentityRegistry(); var value = new object(); long handle;
    using (registry.EnterDocument(7)) handle = registry.GetHandle(value);
    registry.RemoveHandle(handle);
    Check(registry.GetDocumentId(value) == 0 && registry.ReleaseDocument(7).Count == 0);
    using (registry.EnterDocument(8)) Check(registry.GetHandle(value) > handle);
    Check(registry.ReleaseDocument(8).Contains(value));
    return Task.CompletedTask;
});
await Test("collection type arguments use project reference contracts", () =>
{
    string name = RuntimeTypeNameSerializer.Serialize(typeof(ICollection<object>));
    Check(name.StartsWith("System.Collections.Generic.ICollection`1[[System.Object, System.Runtime,", StringComparison.Ordinal));
    Check(!name.Contains("System.Private.CoreLib", StringComparison.Ordinal));
    Check(name.EndsWith("PublicKeyToken=b03f5f7f11d50a3a", StringComparison.Ordinal));
    return Task.CompletedTask;
});
await Test("dictionary entries normalize both generic arguments", () =>
{
    string name = RuntimeTypeNameSerializer.Serialize(typeof(KeyValuePair<object, string>));
    Check(name.StartsWith("System.Collections.Generic.KeyValuePair`2[[System.Object, System.Runtime,", StringComparison.Ordinal));
    Check(name.Contains("[System.String, System.Runtime,", StringComparison.Ordinal));
    Check(!name.Contains("System.Private.CoreLib", StringComparison.Ordinal));
    return Task.CompletedTask;
});
await Test("array shapes retain rank and normalized element identity", () =>
{
    Check(RuntimeTypeNameSerializer.Serialize(typeof(object[])).StartsWith("System.Object[], System.Runtime,", StringComparison.Ordinal));
    Check(RuntimeTypeNameSerializer.Serialize(typeof(object[,])).StartsWith("System.Object[,], System.Runtime,", StringComparison.Ordinal));
    Check(RuntimeTypeNameSerializer.Serialize(typeof(object).MakeArrayType(1)).StartsWith("System.Object[*], System.Runtime,", StringComparison.Ordinal));
    return Task.CompletedTask;
});
await Test("project nested types keep their authored assembly identity", () =>
{
    Check(RuntimeTypeNameSerializer.Serialize(typeof(SerializationTypes.Nested)) == typeof(SerializationTypes.Nested).AssemblyQualifiedName);
    return Task.CompletedTask;
});
await Test("projection collections expose public contracts instead of ABI nested types", () =>
{
    var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
        new System.Reflection.AssemblyName("WinRT.Runtime"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
    var module = assembly.DefineDynamicModule("ProjectionTest");
    var parent = module.DefineType("ABI.System.Collections.Generic.IDictionaryMethods`2",
        System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract);
    var view = parent.DefineNestedType("DictionaryKeyCollection",
        System.Reflection.TypeAttributes.NestedPublic | System.Reflection.TypeAttributes.Abstract);
    view.AddInterfaceImplementation(typeof(ICollection<object>));
    Type viewType = view.CreateType()!;
    parent.CreateType();
    Check(RuntimeTypeNameSerializer.Serialize(viewType) == RuntimeTypeNameSerializer.Serialize(typeof(ICollection<object>)));
    return Task.CompletedTask;
});
return failures == 0 ? 0 : 1;

async Task Test(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
static void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}.");
}
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class Overloads
{
    public int Value { get; }
    public Overloads() { }
    public Overloads(int value) => Value = value;
    public Overloads(string value) => Value = value.Length;
    public string Select(object value)
    {
        return "object";
    }

    public string Select(string value)
    {
        return "string";
    }

    public string Select(int value)
    {
        return "int";
    }

    public static string Factory()
    {
        return "factory";
    }
}

public static class SerializationTypes
{
    public sealed class Nested { }
}
