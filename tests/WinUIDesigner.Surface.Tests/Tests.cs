// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Action = System.Action;
using WinUIDesigner.Surface;
using WinUIDesigner.Surface.Services;

[TestClass]
public sealed class SurfaceTests
{
    [TestMethod]
    public void FragmentedFramesPreserveAllHeaderAndPayloadBytes()
    {
        byte[] expected = new byte[100];
        BitConverter.GetBytes(expected.Length - 4).CopyTo(expected, 0);
        for (int i = 4; i < expected.Length; i++) expected[i] = (byte)i;
        int position = 0;

        byte[]? actual = MessageFrameReader.Read((buffer, offset, count) =>
        {
            int chunk = Math.Min(3, Math.Min(count, expected.Length - position));
            Array.Copy(expected, position, buffer, offset, chunk);
            position += chunk;
            return chunk;
        });

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void EndOfStreamBetweenFramesIsOrderly()
    {
        Assert.IsNull(MessageFrameReader.Read((_, _, _) => 0));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(15)]
    public void EndOfStreamInsideAFrameRejectsThePartialFrame(int cutoff)
    {
        byte[] input = new byte[16];
        BitConverter.GetBytes(12).CopyTo(input, 0);
        int position = 0;

        Assert.Throws<EndOfStreamException>(() => MessageFrameReader.Read((buffer, offset, count) =>
        {
            int chunk = Math.Min(count, cutoff - position);
            Array.Copy(input, position, buffer, offset, chunk);
            position += chunk;
            return chunk;
        }));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(11)]
    [DataRow(int.MaxValue)]
    [DataRow(MessageFrameReader.MaximumLength + 1)]
    public void InvalidFrameLengthsAreRejectedBeforeAllocation(int length)
    {
        Assert.Throws<InvalidDataException>(() => MessageFrameReader.Read((buffer, offset, _) =>
        {
            BitConverter.GetBytes(length).CopyTo(buffer, offset);
            return 4;
        }));
    }

    [TestMethod]
    public async Task UiCallbackExceptionsReachTheRequester()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => QueuedOperation.RunAsync<int>(
            action => { action(); return true; },
            () => throw new InvalidOperationException(),
            TimeSpan.FromSeconds(1),
            default));
    }

    [TestMethod]
    public async Task RejectedDispatcherRequestDoesNotHang()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => QueuedOperation.RunAsync(
            _ => false,
            () => 1,
            TimeSpan.FromSeconds(1),
            default));
    }

    [TestMethod]
    public async Task ExpiredQueuedRequestsCannotMutateTheDocumentLater()
    {
        Action? queued = null;
        bool changed = false;

        await Assert.ThrowsAsync<TimeoutException>(() => QueuedOperation.RunAsync(
            action => { queued = action; return true; },
            () => changed = true,
            TimeSpan.FromMilliseconds(20),
            default));

        queued!();
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public async Task ShutdownCancelsQueuedWorkWithoutLateMutation()
    {
        Action? queued = null;
        bool changed = false;
        using var cancellation = new CancellationTokenSource();

        Task<bool> pending = QueuedOperation.RunAsync(
            action => { queued = action; return true; },
            () => changed = true,
            TimeSpan.FromSeconds(1),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        queued!();
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void ClosingDocumentReleasesNonvisualAndVisualIdentities()
    {
        var registry = new ObjectIdentityRegistry();
        var visual = new object();
        var resource = new object();
        long visualHandle;

        using (registry.EnterDocument(1))
        {
            visualHandle = registry.GetHandle(visual);
            registry.GetHandle(resource);
            registry.RegisterMarkupInfo(resource, 27, 3);
        }

        var released = registry.ReleaseDocument(1);
        CollectionAssert.AreEquivalent(new[] { visual, resource }, released.ToArray());
        Assert.IsFalse(registry.TryGetObject(visualHandle, out _));
        Assert.IsNull(registry.GetSourceInfo(resource));
    }

    [TestMethod]
    public void SharedResourcesSurviveClosingOneDocument()
    {
        var registry = new ObjectIdentityRegistry();
        var resource = new object();
        long handle;

        using (registry.EnterDocument(1)) handle = registry.GetHandle(resource);
        using (registry.EnterDocument(2)) Assert.AreEqual(handle, registry.GetHandle(resource));

        Assert.AreEqual(0, registry.ReleaseDocument(1).Count);
        Assert.IsTrue(registry.TryGetObject(handle, out _));
        CollectionAssert.Contains(registry.ReleaseDocument(2).ToArray(), resource);
        Assert.IsFalse(registry.TryGetObject(handle, out _));
    }

    [TestMethod]
    public void NestedConstructionScopesRestoreDocumentOwner()
    {
        var registry = new ObjectIdentityRegistry();
        var first = new object();
        var second = new object();

        using (registry.EnterDocument(1))
        {
            using (registry.EnterDocument(2)) registry.GetHandle(second);
            registry.GetHandle(first);
        }

        Assert.AreEqual(1, registry.GetDocumentId(first));
        Assert.AreEqual(2, registry.GetDocumentId(second));
    }

    [TestMethod]
    public void SourceCoordinatesAreReturnedAsIndependentSnapshots()
    {
        var registry = new ObjectIdentityRegistry();
        var value = new object();
        registry.RegisterSourceInfo(value, new SourceInfo { FileName = "Page.xaml", LineNumber = 7, ColumnNumber = 3 });

        SourceInfo first = registry.GetSourceInfo(value)!;
        first.LineNumber = 999;

        Assert.AreEqual<uint?>(7, registry.GetSourceInfo(value)!.LineNumber);
    }

    [TestMethod]
    public void OverloadsUseProtocolParameterTypesRatherThanDeclarationOrder()
    {
        var method = RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Select), [typeof(string)], false);
        Assert.AreEqual("string", method!.Invoke(new Overloads(), ["value"]));
        Assert.IsNull(RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Select), [typeof(double)], false));
        Assert.IsNull(RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Factory), [], false));
        Assert.AreEqual("factory", RuntimeMemberResolver.FindMethod(typeof(Overloads), nameof(Overloads.Factory), [], true)!.Invoke(null, []));
    }

    [TestMethod]
    public void ConstructorSelectionDoesNotSubstituteAnUnrelatedEqualArityOverload()
    {
        var constructor = RuntimeMemberResolver.FindConstructor(typeof(Overloads), [typeof(int)]);
        Assert.AreEqual(42, ((Overloads)constructor!.Invoke([42])).Value);
        Assert.IsNull(RuntimeMemberResolver.FindConstructor(typeof(Overloads), [typeof(double)]));
    }

    [TestMethod]
    public void ConstructionErrorsRoundTripThroughFrontendActionErrorContract()
    {
        var action = new SetSurfaceContentAction(42, 123);
        var error = new DocumentConstructionException(new InvalidOperationException("fixture failure"), action);
        var decoded = ActionErrorJsonSerializer.Deserialize(error.SerializedErrors);

        Assert.AreEqual(1, decoded.Count);
        StringAssert.Contains(decoded[0].Error, "fixture failure");
        Assert.IsInstanceOfType<SetSurfaceContentAction>(decoded[0].XamlAction);
        var content = (SetSurfaceContentAction)decoded[0].XamlAction!;
        Assert.AreEqual(42, content.DocumentId);
        Assert.AreEqual(123, content.RootProxyHandle);
    }

    [TestMethod]
    public void ExplicitRemovalDetachesDocumentOwnershipWithoutLeakingReleasedObjects()
    {
        var registry = new ObjectIdentityRegistry();
        var value = new object();
        long handle;

        using (registry.EnterDocument(7)) handle = registry.GetHandle(value);
        registry.RemoveHandle(handle);

        Assert.AreEqual(0, registry.GetDocumentId(value));
        Assert.AreEqual(0, registry.ReleaseDocument(7).Count);
        using (registry.EnterDocument(8)) Assert.IsTrue(registry.GetHandle(value) > handle);
        CollectionAssert.Contains(registry.ReleaseDocument(8).ToArray(), value);
    }

    [TestMethod]
    public void CollectionTypeArgumentsUseProjectReferenceContracts()
    {
        string name = RuntimeTypeNameSerializer.Serialize(typeof(ICollection<object>));
        StringAssert.StartsWith(name, "System.Collections.Generic.ICollection`1[[System.Object, System.Runtime,");
        Assert.IsFalse(name.Contains("System.Private.CoreLib", StringComparison.Ordinal));
        StringAssert.EndsWith(name, "PublicKeyToken=b03f5f7f11d50a3a");
    }

    [TestMethod]
    public void DictionaryEntriesNormalizeBothGenericArguments()
    {
        string name = RuntimeTypeNameSerializer.Serialize(typeof(KeyValuePair<object, string>));
        StringAssert.StartsWith(name, "System.Collections.Generic.KeyValuePair`2[[System.Object, System.Runtime,");
        StringAssert.Contains(name, "[System.String, System.Runtime,");
        Assert.IsFalse(name.Contains("System.Private.CoreLib", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArrayShapesRetainRankAndNormalizedElementIdentity()
    {
        StringAssert.StartsWith(RuntimeTypeNameSerializer.Serialize(typeof(object[])), "System.Object[], System.Runtime,");
        StringAssert.StartsWith(RuntimeTypeNameSerializer.Serialize(typeof(object[,])), "System.Object[,], System.Runtime,");
        StringAssert.StartsWith(RuntimeTypeNameSerializer.Serialize(typeof(object).MakeArrayType(1)), "System.Object[*], System.Runtime,");
    }

    [TestMethod]
    public void ProjectNestedTypesKeepTheirAuthoredAssemblyIdentity()
    {
        Assert.AreEqual(typeof(SerializationTypes.Nested).AssemblyQualifiedName,
            RuntimeTypeNameSerializer.Serialize(typeof(SerializationTypes.Nested)));
    }

    [TestMethod]
    public void ProjectionCollectionsExposePublicContractsInsteadOfAbiNestedTypes()
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

        Assert.AreEqual(RuntimeTypeNameSerializer.Serialize(typeof(ICollection<object>)),
            RuntimeTypeNameSerializer.Serialize(viewType));
    }
}

internal sealed class Overloads
{
    public int Value { get; }
    public Overloads() { }
    public Overloads(int value) => Value = value;
    public Overloads(string value) => Value = value.Length;
    public string Select(object value) => "object";
    public string Select(string value) => "string";
    public string Select(int value) => "int";
    public static string Factory() => "factory";
}

public static class SerializationTypes
{
    public sealed class Nested { }
}
