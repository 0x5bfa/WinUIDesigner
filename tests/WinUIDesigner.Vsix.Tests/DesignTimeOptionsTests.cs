// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.DesignTime;

[TestClass]
public sealed class DesignTimeOptionsTests
{
    [TestMethod]
    public void TargetPlatformsKeepTheirIdentifiersAndOrder()
    {
        CollectionAssert.AreEqual(
            new[] { "win", "android", "ios", "wasm", "skia", "androidskia", "iosskia", "wasmskia" },
            DesignTimeTargetPlatform.All.Select(platform => platform.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Windows", "Android", "iOS", "WebAssembly", "Skia Desktop", "Android (Skia)", "iOS (Skia)", "WebAssembly (Skia)" },
            DesignTimeTargetPlatform.All.Select(platform => platform.DisplayName).ToArray());
        Assert.AreSame(DesignTimeTargetPlatform.Windows, DesignTimeTargetPlatform.All[0]);
    }

    [TestMethod]
    [DataRow("win", "win")]
    [DataRow("WIN", "win")]
    [DataRow(" Android ", "android")]
    [DataRow("iOS", "ios")]
    [DataRow("wasm", "wasm")]
    [DataRow("skia", "skia")]
    [DataRow("AndroidSkia", "androidskia")]
    [DataRow("iosskia", "iosskia")]
    [DataRow("wasmskia", "wasmskia")]
    [DataRow("Windows", "win")]
    [DataRow("webassembly", "wasm")]
    [DataRow("Skia Desktop", "skia")]
    [DataRow("android (skia)", "androidskia")]
    [DataRow("iOS (Skia)", "iosskia")]
    [DataRow("WebAssembly (Skia)", "wasmskia")]
    public void TargetPlatformIsFoundByIdentifierOrDisplayName(string text, string expectedId)
    {
        Assert.IsTrue(DesignTimeTargetPlatform.TryParse(text, out DesignTimeTargetPlatform platform));
        Assert.AreEqual(expectedId, platform.Id);
        Assert.AreSame(platform, DesignTimeTargetPlatform.FromId(text));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("macos")]
    [DataRow("windows phone")]
    [DataRow("()")]
    public void UnknownTargetPlatformFallsBackToWindows(string? text)
    {
        Assert.IsFalse(DesignTimeTargetPlatform.TryParse(text, out DesignTimeTargetPlatform platform));
        Assert.AreSame(DesignTimeTargetPlatform.Windows, platform);
        Assert.AreSame(DesignTimeTargetPlatform.Windows, DesignTimeTargetPlatform.FromId(text));
    }

    [TestMethod]
    public void DefaultOptionsPreviewWindowsOnTheInstalledWindows()
    {
        DesignTimeOptions options = DesignTimeOptions.Default;

        Assert.AreSame(DesignTimeTargetPlatform.Windows, options.TargetPlatform);
        Assert.AreEqual("win", options.TargetPlatformId);
        Assert.IsNull(options.ContractVersion);
        Assert.IsTrue(options.IsDefault);
        Assert.AreEqual("v=1;target=win", options.Serialize());
        Assert.AreEqual(options, new DesignTimeOptions(null, null));
        Assert.AreEqual(options, new DesignTimeOptions(DesignTimeTargetPlatform.Windows, 0));
        Assert.AreEqual(options, new DesignTimeOptions(DesignTimeTargetPlatform.Windows, -3));
    }

    [TestMethod]
    public void OptionsSerializeToTheDocumentedTextForm()
    {
        var options = new DesignTimeOptions(DesignTimeTargetPlatform.Android, 7);

        Assert.AreEqual("v=1;target=android;contract=7", options.Serialize());
        Assert.AreEqual("v=1;target=android;contract=7", options.ToString());
        Assert.IsFalse(options.IsDefault);
    }

    [TestMethod]
    public void EveryOptionsValueSurvivesARoundTrip()
    {
        foreach (DesignTimeTargetPlatform platform in DesignTimeTargetPlatform.All)
        {
            foreach (int? contractVersion in new int?[] { null, 1, 7, 19, 99 })
            {
                var options = new DesignTimeOptions(platform, contractVersion);

                Assert.IsTrue(DesignTimeOptions.TryParse(options.Serialize(), out DesignTimeOptions parsed), options.Serialize());
                Assert.AreEqual(options, parsed);
                Assert.AreSame(platform, parsed.TargetPlatform);
                Assert.AreEqual(contractVersion, parsed.ContractVersion);
            }
        }
    }

    [TestMethod]
    [DataRow("v=1;target=android;contract=7", "android", 7)]
    [DataRow("v=1;theme=dark;target=ios;contract=10;flag", "ios", 10)]
    [DataRow("v=2;target=wasm;scale=2", "wasm", 0)]
    [DataRow(" V = 1 ; TARGET = Android ; Contract = 7 ; ", "android", 7)]
    [DataRow("contract=19;target=skia;v=1", "skia", 19)]
    [DataRow("v=1;target=ios;target=android", "android", 0)]
    [DataRow("v=1", "win", 0)]
    [DataRow("v=1;contract=7", "win", 7)]
    [DataRow("v=1;target=macos;contract=7", "win", 7)]
    [DataRow("v=1;target=ios;contract=latest", "ios", 0)]
    [DataRow("v=1;target=ios;contract=0", "ios", 0)]
    [DataRow("v=1;target=ios;contract=-3", "ios", 0)]
    [DataRow("v=1;target=ios;contract=os", "ios", 0)]
    [DataRow("v=1;target=ios;contract=", "ios", 0)]
    public void OptionsTextIsReadLeniently(string text, string expectedTarget, int expectedContract)
    {
        Assert.IsTrue(DesignTimeOptions.TryParse(text, out DesignTimeOptions options));
        Assert.AreEqual(expectedTarget, options.TargetPlatformId);
        Assert.AreEqual(ToContractVersion(expectedContract), options.ContractVersion);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("garbage")]
    [DataRow("target=android;contract=7")]
    [DataRow("v=0;target=android")]
    [DataRow("v=one;target=android")]
    [DataRow("v=-1;target=android")]
    [DataRow("v=1.5;target=android")]
    [DataRow("v=;target=android")]
    [DataRow("=1;target=android")]
    [DataRow("v")]
    [DataRow(";;;")]
    [DataRow("{\"target\":\"android\"}")]
    public void GarbageReadsAsTheDefaultOptions(string? text)
    {
        Assert.IsFalse(DesignTimeOptions.TryParse(text, out DesignTimeOptions options));
        Assert.AreSame(DesignTimeOptions.Default, options);
    }

    [TestMethod]
    public void OptionsCompareByValue()
    {
        var first = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 10);
        var same = new DesignTimeOptions(DesignTimeTargetPlatform.FromId("IOS"), 10);
        var otherTarget = new DesignTimeOptions(DesignTimeTargetPlatform.IosSkia, 10);
        var otherContract = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 14);
        var noContract = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, null);

        Assert.AreEqual(first, same);
        Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(first, otherTarget);
        Assert.AreNotEqual(first, otherContract);
        Assert.AreNotEqual(first, noContract);

        // The operators follow Equals, including for null.
        DesignTimeOptions? none = null;
        bool equalsNone = first.Equals(none);
        Assert.IsFalse(equalsNone);
        bool equalToSame = first == same;
        bool differentFromSame = first != same;
        bool differentFromOther = first != otherContract;
        bool equalToNone = first == none;
        bool noneEqualsNone = none == null;
        Assert.IsTrue(equalToSame);
        Assert.IsFalse(differentFromSame);
        Assert.IsTrue(differentFromOther);
        Assert.IsFalse(equalToNone);
        Assert.IsTrue(noneEqualsNone);
    }

    [TestMethod]
    public void OptionsCanBeChangedOneAtATime()
    {
        DesignTimeOptions options = DesignTimeOptions.Default
            .WithTargetPlatform(DesignTimeTargetPlatform.AndroidSkia)
            .WithContractVersion(15);

        Assert.AreEqual(new DesignTimeOptions(DesignTimeTargetPlatform.AndroidSkia, 15), options);
        Assert.AreEqual(new DesignTimeOptions(DesignTimeTargetPlatform.AndroidSkia, null), options.WithContractVersion(null));
        Assert.AreEqual(new DesignTimeOptions(DesignTimeTargetPlatform.Windows, 15), options.WithTargetPlatform(null));
        Assert.IsTrue(DesignTimeOptions.Default.IsDefault, "The default options must not change.");
    }

    [TestMethod]
    // A contract version.
    [DataRow("1", 1)]
    [DataRow("7", 7)]
    [DataRow("10", 10)]
    [DataRow("11", 11)]
    [DataRow("19", 19)]
    [DataRow("99", 99)]
    // A build number. A build between two releases counts as the earlier one.
    [DataRow("10240", 1)]
    [DataRow("17763", 7)]
    [DataRow("17800", 7)]
    [DataRow("18363", 8)]
    [DataRow("19041", 10)]
    [DataRow("19045", 10)]
    [DataRow("20348", 12)]
    [DataRow("22000", 14)]
    [DataRow("22621", 15)]
    [DataRow("22631", 15)]
    [DataRow("26100", 19)]
    [DataRow("26200", 19)]
    [DataRow("26300", 19)]
    [DataRow("28000", 20)]
    [DataRow("99999", 20)]
    // A full version, as in TargetPlatformVersion.
    [DataRow("10.0.17763.0", 7)]
    [DataRow("10.0.17763", 7)]
    [DataRow("10.0.19041.0", 10)]
    [DataRow("10.0.19045.3803", 10)]
    [DataRow("10.0.22000.0", 14)]
    [DataRow("10.0.22621.0", 15)]
    [DataRow(" 10.0.26100.0 ", 19)]
    // A release name.
    [DataRow("1507", 1)]
    [DataRow("1809", 7)]
    [DataRow("1903", 8)]
    [DataRow("1909", 8)]
    [DataRow("2004", 10)]
    [DataRow("2104", 12)]
    [DataRow("20H2", 10)]
    [DataRow("21h1", 10)]
    [DataRow("23H2", 15)]
    [DataRow("24H2", 19)]
    [DataRow("25H2", 19)]
    [DataRow("26H1", 20)]
    // A display name, which tells Windows 10 from Windows 11.
    [DataRow("Windows 10 1809", 7)]
    [DataRow("windows 10, version 1809", 7)]
    [DataRow("Windows 10 21H2", 10)]
    [DataRow("Windows 10 22H2", 10)]
    [DataRow("Windows 11 21H2", 14)]
    [DataRow("Windows 11 22H2", 15)]
    [DataRow("Win11 24H2", 19)]
    [DataRow("Windows Server 2022", 12)]
    public void WindowsVersionIsReadInEveryAcceptedForm(string text, int expectedContractVersion)
    {
        Assert.IsTrue(WindowsVersionCatalog.TryParse(text, out int contractVersion), text);
        Assert.AreEqual(expectedContractVersion, contractVersion);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("0")]
    [DataRow("-7")]
    [DataRow("+7")]
    [DataRow("v7")]
    [DataRow("7.0")]
    [DataRow("100")]
    [DataRow("999")]
    [DataRow("1234")]
    [DataRow("9999")]
    [DataRow("10239")]
    [DataRow("100000")]
    [DataRow("99999999999999999999")]
    [DataRow("10.0")]
    [DataRow("10.0.5000.0")]
    [DataRow("10.1.17763.0")]
    [DataRow("6.3.9600")]
    [DataRow("21H2")]
    [DataRow("22H2")]
    [DataRow("22h2")]
    [DataRow("Windows 10")]
    [DataRow("Windows 12 22H2")]
    [DataRow("latest")]
    public void UnknownOrAmbiguousWindowsVersionIsRejected(string? text)
    {
        Assert.IsFalse(WindowsVersionCatalog.TryParse(text, out int contractVersion), text);
        Assert.AreEqual(0, contractVersion);
    }

    [TestMethod]
    public void CatalogIsOrderedAndEveryReleaseReadsBackToItsContract()
    {
        IReadOnlyList<WindowsRelease> releases = WindowsVersionCatalog.Releases;

        Assert.AreEqual(releases.Count, releases.Select(release => release.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (int index = 0; index < releases.Count; index++)
        {
            WindowsRelease release = releases[index];
            if (index > 0)
            {
                Assert.IsTrue(releases[index - 1].Build < release.Build, release.DisplayName);
                Assert.IsTrue(releases[index - 1].ContractVersion <= release.ContractVersion, release.DisplayName);
            }

            foreach (string text in new[] { release.Build.ToString(), release.Version, release.DisplayName, release.ContractVersion.ToString() })
            {
                Assert.IsTrue(WindowsVersionCatalog.TryParse(text, out int contractVersion), text);
                Assert.AreEqual(release.ContractVersion, contractVersion, text);
            }

            // A release name is accepted on its own unless another product reuses it for a different contract.
            bool isAmbiguous = releases.Any(other => other.ReleaseName == release.ReleaseName && other.ContractVersion != release.ContractVersion);
            Assert.AreEqual(!isAmbiguous, WindowsVersionCatalog.TryParse(release.ReleaseName, out int byName), release.ReleaseName);
            Assert.AreEqual(isAmbiguous ? 0 : release.ContractVersion, byName, release.ReleaseName);
        }

        Assert.AreEqual("Windows 10 1809", WindowsVersionCatalog.FindByContract(7)?.DisplayName);
        Assert.AreEqual("Windows 10 2004", WindowsVersionCatalog.FindByContract(10)?.DisplayName);
        Assert.IsNull(WindowsVersionCatalog.FindByContract(9));
        Assert.AreEqual("Windows 11 24H2 (contract 19)", WindowsVersionCatalog.Describe(19));
        Assert.AreEqual("contract 9", WindowsVersionCatalog.Describe(9));
        Assert.AreEqual("installed Windows version", WindowsVersionCatalog.Describe(null));
    }

    [TestMethod]
    public void CatalogAgreesWithTheInstalledWindowsSdks()
    {
        string platforms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Platforms", "UAP");
        if (!Directory.Exists(platforms))
        {
            Assert.Inconclusive($"No Windows SDK is installed under '{platforms}'.");
        }

        var verified = new SortedSet<int>();
        foreach (string file in Directory.EnumerateFiles(platforms, "*.xml", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) is not ("Platform.xml" or "PreviousPlatforms.xml"))
            {
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch (XmlException)
            {
                // A file that cannot be read verifies nothing; it is not the catalog's fault.
                continue;
            }

            foreach (XElement platform in document.Descendants().Where(element => element.Name.LocalName == "ApplicationPlatform"))
            {
                XElement? contract = platform.Descendants().FirstOrDefault(element =>
                    element.Name.LocalName == "ApiContract" && (string?)element.Attribute("name") == WindowsVersionCatalog.ContractName);
                if (!Version.TryParse((string?)platform.Attribute("version"), out Version? platformVersion) || platformVersion is null
                    || !Version.TryParse((string?)contract?.Attribute("version"), out Version? contractVersion) || contractVersion is null)
                {
                    continue;
                }

                // An SDK newer than the catalog is not an error; a known build with another contract is.
                WindowsRelease? release = WindowsVersionCatalog.Releases.FirstOrDefault(candidate => candidate.Build == platformVersion.Build);
                if (release is not null)
                {
                    Assert.AreEqual(contractVersion.Major, release.ContractVersion, $"{release.DisplayName} in '{file}'");
                    verified.Add(release.Build);
                }
            }
        }

        if (verified.Count == 0)
        {
            Assert.Inconclusive($"The Windows SDKs under '{platforms}' describe no release of the catalog.");
        }

        Console.WriteLine("Verified against the Windows SDK: " + string.Join(", ", verified));
    }

    [TestMethod]
    [DataRow(null, null, null, "win", 0)]
    // The project file sets the defaults; a value that cannot be read is ignored.
    [DataRow(null, "android", null, "android", 0)]
    [DataRow(null, null, "1809", "win", 7)]
    [DataRow(null, "Skia Desktop", "10.0.22621.0", "skia", 15)]
    [DataRow(null, "playstation", "Windows 95", "win", 0)]
    [DataRow(null, "", "  ", "win", 0)]
    // The user's override wins, one option at a time.
    [DataRow("v=1;target=ios;contract=10", "android", "1809", "ios", 10)]
    [DataRow("v=1;target=ios", "android", "1809", "ios", 7)]
    [DataRow("v=1;contract=10", "android", "1809", "android", 10)]
    [DataRow("v=1;contract=os", "android", "1809", "android", 0)]
    [DataRow("v=1;target=ios;contract=OS", null, "1809", "ios", 0)]
    [DataRow("v=1;target=wasm;contract=14", null, null, "wasm", 14)]
    // An override that cannot be read, wholly or in part, leaves the decision to the project file.
    [DataRow("v=1", "android", "1809", "android", 7)]
    [DataRow("garbage", "android", "1809", "android", 7)]
    [DataRow("target=ios;contract=10", "android", "1809", "android", 7)]
    [DataRow("v=1;target=macos;contract=soon", "android", "1809", "android", 7)]
    [DataRow("v=1;target=macos;contract=10", null, null, "win", 10)]
    public void OptionsFollowTheUserThenTheProjectFileThenTheDefaults(
        string? userOverride, string? targetPlatformProperty, string? windowsVersionProperty, string expectedTarget, int expectedContract)
    {
        DesignTimeOptions options = DesignTimeOptionsResolver.Resolve(userOverride, targetPlatformProperty, windowsVersionProperty);

        Assert.AreEqual(expectedTarget, options.TargetPlatformId);
        Assert.AreEqual(ToContractVersion(expectedContract), options.ContractVersion);
    }

    [TestMethod]
    [DataRow("win", 0, null, null, null)]
    [DataRow("android", 7, "android", "1809", null)]
    [DataRow("ios", 7, "android", "1809", "v=1;target=ios")]
    [DataRow("android", 10, "android", "1809", "v=1;contract=10")]
    [DataRow("android", 0, "android", "1809", "v=1;contract=os")]
    [DataRow("win", 0, "android", "1809", "v=1;target=win;contract=os")]
    [DataRow("ios", 0, null, null, "v=1;target=ios")]
    [DataRow("win", 7, null, null, "v=1;contract=7")]
    [DataRow("wasmskia", 19, "bogus", "bogus", "v=1;target=wasmskia;contract=19")]
    public void TheUserOverrideHoldsOnlyWhatDiffersFromTheProjectFile(
        string target, int contract, string? targetPlatformProperty, string? windowsVersionProperty, string? expectedOverride)
    {
        var options = new DesignTimeOptions(DesignTimeTargetPlatform.FromId(target), ToContractVersion(contract));

        Assert.AreEqual(expectedOverride, DesignTimeOptionsResolver.CreateUserOverride(options, targetPlatformProperty, windowsVersionProperty));
    }

    [TestMethod]
    public void AStoredOverrideAlwaysResolvesBackToTheChosenOptions()
    {
        foreach (string? targetPlatformProperty in new[] { null, "android", "Skia Desktop", "bogus" })
        {
            foreach (string? windowsVersionProperty in new[] { null, "1809", "10.0.26100.0", "bogus" })
            {
                foreach (DesignTimeTargetPlatform platform in DesignTimeTargetPlatform.All)
                {
                    foreach (int? contractVersion in new int?[] { null, 1, 7, 19 })
                    {
                        var options = new DesignTimeOptions(platform, contractVersion);
                        string? stored = DesignTimeOptionsResolver.CreateUserOverride(options, targetPlatformProperty, windowsVersionProperty);

                        Assert.AreEqual(
                            options,
                            DesignTimeOptionsResolver.Resolve(stored, targetPlatformProperty, windowsVersionProperty),
                            $"'{stored}' with {targetPlatformProperty}/{windowsVersionProperty}");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void TheProtocolSerializerCarriesAMemberAddedByADerivedRequest()
    {
        // Visual Studio's Message builds its serializer from the runtime type of the payload and the receiving side
        // builds one from the type its observer was registered with. These contracts stand in for the three types
        // involved: Visual Studio's request, the request the extension sends and the contract the surface reads.
        string options = new DesignTimeOptions(DesignTimeTargetPlatform.AndroidSkia, 7).Serialize();
        object payload = new SentRequest { Document = new DocumentInfo { DocumentId = 42 }, Options = options };

        byte[] wire = Serialize(payload);

        ReceivedRequest? received = Deserialize<ReceivedRequest>(wire);
        Assert.IsNotNull(received);
        Assert.AreEqual(options, received.Options);
        Assert.AreEqual(42, received.Document?.DocumentId);
        Assert.IsTrue(DesignTimeOptions.TryParse(received.Options, out DesignTimeOptions parsed));
        Assert.AreEqual(new DesignTimeOptions(DesignTimeTargetPlatform.AndroidSkia, 7), parsed);

        // A reader that only knows Visual Studio's request ignores the member.
        Assert.AreEqual(42, Deserialize<BaseRequest>(wire)?.Document?.DocumentId);

        // Without a value the member is not written, so the request is byte for byte Visual Studio's own.
        CollectionAssert.AreEqual(
            Serialize(new BaseRequest { Document = new DocumentInfo { DocumentId = 42 } }),
            Serialize(new SentRequest { Document = new DocumentInfo { DocumentId = 42 } }));

        // A request without the member reads as "no options".
        Assert.IsNull(Deserialize<ReceivedRequest>(Serialize(new BaseRequest { Document = new DocumentInfo { DocumentId = 42 } }))?.Options);
    }

    // Data rows cannot hold a nullable number, so zero stands for "no contract version".
    private static int? ToContractVersion(int contractVersion)
    {
        return contractVersion == 0 ? null : contractVersion;
    }

    // As Message.SerializeToBuffer does it.
    private static byte[] Serialize(object payload)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(payload.GetType());
        using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.Unicode, ownsStream: false))
        {
            serializer.WriteObject(writer, payload);
            writer.Flush();
        }

        return stream.ToArray();
    }

    // As Message.ConvertJsonPayloadToModel does it.
    private static TModel? Deserialize<TModel>(byte[] wire)
        where TModel : class
    {
        var serializer = new DataContractJsonSerializer(typeof(TModel));
        using var stream = new MemoryStream(wire);
        using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(stream, Encoding.Unicode, XmlDictionaryReaderQuotas.Max, null);
        return (TModel?)serializer.ReadObject(reader);
    }

    [DataContract]
    private sealed class DocumentInfo
    {
        [DataMember]
        public int DocumentId { get; set; }
    }

    [DataContract]
    private class BaseRequest
    {
        [DataMember]
        public DocumentInfo? Document { get; set; }

        [DataMember]
        public DocumentInfo? Application { get; set; }
    }

    [DataContract]
    private sealed class SentRequest : BaseRequest
    {
        [DataMember(Name = DesignTimeOptions.WireMemberName, EmitDefaultValue = false)]
        public string? Options { get; set; }
    }

    [DataContract]
    private sealed class ReceivedRequest : BaseRequest
    {
        [DataMember(Name = DesignTimeOptions.WireMemberName, EmitDefaultValue = false)]
        public string? Options { get; set; }
    }
}
