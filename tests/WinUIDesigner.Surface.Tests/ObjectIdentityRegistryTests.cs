// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class ObjectIdentityRegistryTests
{
    private const long _ApplicationHandle = 1;

    [TestMethod]
    public void ProcessObjectKeepsItsHandleWhenTheDocumentThatUsedItIsReleased()
    {
        var registry = new ObjectIdentityRegistry();
        var application = new object();
        registry.RegisterProcessObject(_ApplicationHandle, application);

        // Building a document resolves the application, asks for its handle and attaches markup information to it.
        using (registry.EnterDocument(7))
        {
            registry.Track(application);
            Assert.AreEqual(_ApplicationHandle, registry.GetHandle(application));
            registry.RegisterMarkupInfo(application, markupHandle: 3, changeVersion: 1);
        }

        HashSet<object> released = registry.ReleaseDocument(7);

        Assert.IsFalse(released.Contains(application), "The application is not part of any document.");
        Assert.IsTrue(registry.TryGetObject(_ApplicationHandle, out object? resolved));
        Assert.AreSame(application, resolved);
        Assert.AreEqual(0, registry.GetDocumentId(application));
    }

    [TestMethod]
    public void ProcessObjectSurvivesEveryDocument()
    {
        var registry = new ObjectIdentityRegistry();
        var application = new object();
        registry.RegisterProcessObject(_ApplicationHandle, application);

        foreach (int documentId in new[] { 7, 8, 7 })
        {
            using (registry.EnterDocument(documentId))
            {
                registry.GetHandle(application);
            }

            registry.ReleaseDocument(documentId);
            Assert.IsTrue(registry.TryGetObject(_ApplicationHandle, out _), $"Lost after document {documentId}.");
        }
    }

    [TestMethod]
    public void DocumentObjectIsReleasedWithItsDocument()
    {
        var registry = new ObjectIdentityRegistry();
        var application = new object();
        var element = new object();
        registry.RegisterProcessObject(_ApplicationHandle, application);

        long handle;
        using (registry.EnterDocument(7))
        {
            handle = registry.GetHandle(element);
        }

        Assert.AreNotEqual(_ApplicationHandle, handle, "A new object must not take the handle of the application.");

        HashSet<object> released = registry.ReleaseDocument(7);

        Assert.IsTrue(released.Contains(element));
        Assert.IsFalse(registry.TryGetObject(handle, out _));
    }

    [TestMethod]
    public void ObjectSharedByTwoDocumentsOutlivesTheFirstOne()
    {
        var registry = new ObjectIdentityRegistry();
        var shared = new object();

        long handle;
        using (registry.EnterDocument(7))
        {
            handle = registry.GetHandle(shared);
        }

        using (registry.EnterDocument(8))
        {
            registry.Track(shared);
        }

        Assert.AreEqual(0, registry.ReleaseDocument(7).Count);
        Assert.IsTrue(registry.TryGetObject(handle, out _));

        Assert.IsTrue(registry.ReleaseDocument(8).Contains(shared));
        Assert.IsFalse(registry.TryGetObject(handle, out _));
    }

    [TestMethod]
    public void RemovingAProcessObjectForgetsIt()
    {
        var registry = new ObjectIdentityRegistry();
        var application = new object();
        registry.RegisterProcessObject(_ApplicationHandle, application);

        registry.RemoveHandle(_ApplicationHandle);
        Assert.IsFalse(registry.TryGetObject(_ApplicationHandle, out _));

        // Registered again as an ordinary object, it belongs to the document that touches it.
        using (registry.EnterDocument(7))
        {
            registry.RegisterHandle(_ApplicationHandle, application);
        }

        Assert.IsTrue(registry.ReleaseDocument(7).Contains(application));
    }
}
