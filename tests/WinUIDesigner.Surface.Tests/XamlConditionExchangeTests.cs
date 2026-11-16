// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

/// <summary>
/// Covers the exchange of condition questions between Visual Studio and the surface. The two processes run on
/// different runtimes, so these tests are compiled into both test projects: the JSON written by one runtime
/// has to be the JSON the other one reads.
/// </summary>
[TestClass]
public sealed class XamlConditionExchangeTests
{
    private const string _Contract = "Windows.Foundation.UniversalApiContract";

    private const string _RequestJson =
        "{\"Conditions\":[\"IsApiContractNotPresent(Windows.Foundation.UniversalApiContract,7)\",\"cond:FeatureFlagCondition(A, B)\",\"IsTypePresent(\"]," +
        "\"PredicateNamespaces\":[null,\"using:My.App\",null]}";

    private const string _ResponseJson = "{\"Outcomes\":[true,false,null]}";

    private const string _FailedResponseJson = "{\"Error\":\"the surface is closing\",\"Outcomes\":[]}";

    [TestMethod]
    public void MessageIdIsFarFromTheIdentifiersOfVisualStudio()
    {
        // Visual Studio numbers its messages 0 to 20, 99 and 500 to 559 and dispatches by number alone.
        int id = (int)DesignerMessageId.EvaluateXamlConditions;

        Assert.IsTrue(id > 0x00100000, id.ToString());
        Assert.AreEqual(0x57550001, id);
    }

    [TestMethod]
    public void RequestIsWrittenAsTheJsonTheOtherProcessReads()
    {
        EvaluateXamlConditionsRequest request = XamlConditionExchange.CreateRequest(CreateQueries());

        Assert.AreEqual(_RequestJson, Serialize(request));
    }

    [TestMethod]
    public void RequestIsReadFromTheJsonTheOtherProcessWrites()
    {
        EvaluateXamlConditionsRequest request = Deserialize<EvaluateXamlConditionsRequest>(_RequestJson);

        List<XamlConditionQuery> queries = XamlConditionExchange.ReadQueries(request);

        // The questions arrive with the keys they were asked under.
        CollectionAssert.AreEqual(CreateQueries().Select(query => query.Key).ToArray(), queries.Select(query => query.Key).ToArray());
        Assert.AreEqual("My.App.FeatureFlagCondition", queries[1].TypeName);
        Assert.AreEqual("A, B", queries[1].Condition.Argument);
        Assert.IsTrue(queries[0].Condition.IsNegated);
        Assert.AreEqual(XamlConditionKind.Unknown, queries[2].Condition.Kind);
    }

    [TestMethod]
    public void ResponseIsWrittenAndReadAsTheSameJson()
    {
        var response = new EvaluateXamlConditionsResponse { Outcomes = [true, false, null] };

        Assert.AreEqual(_ResponseJson, Serialize(response));
        CollectionAssert.AreEqual(new bool?[] { true, false, null }, Deserialize<EvaluateXamlConditionsResponse>(_ResponseJson).Outcomes);
        Assert.IsNull(Deserialize<EvaluateXamlConditionsResponse>(_ResponseJson).Error);
    }

    [TestMethod]
    public void FailedResponseCarriesItsError()
    {
        var response = new EvaluateXamlConditionsResponse { Outcomes = [], Error = "the surface is closing" };

        Assert.AreEqual(_FailedResponseJson, Serialize(response));
        Assert.AreEqual("the surface is closing", Deserialize<EvaluateXamlConditionsResponse>(_FailedResponseJson).Error);
    }

    [TestMethod]
    public void MessageWithoutContentIsAnEmptyRequest()
    {
        // The protocol reads a message without a payload as "{}".
        EvaluateXamlConditionsRequest request = Deserialize<EvaluateXamlConditionsRequest>("{}");

        Assert.AreEqual(0, XamlConditionExchange.ReadQueries(request).Count);
        Assert.AreEqual(0, XamlConditionExchange.ReadQueries(null).Count);
    }

    [TestMethod]
    public void RequestWithFewerNamespacesThanConditionsIsStillRead()
    {
        var request = new EvaluateXamlConditionsRequest { Conditions = ["cond:Flag(On)", "IsTypePresent(My.Type)", null], PredicateNamespaces = ["using:My.App"] };

        List<XamlConditionQuery> queries = XamlConditionExchange.ReadQueries(request);

        CollectionAssert.AreEqual(new[] { "using:My.App|Flag(On)", "IsTypePresent(My.Type)", string.Empty }, queries.Select(query => query.Key).ToArray());
    }

    [TestMethod]
    public void OnlyCustomConditionsNeedObjectsOfTheApplication()
    {
        List<XamlConditionQuery> builtIn = [Query("IsApiContractPresent(X,7)"), Query("IsTypePresent(My.Type)"), Query("IsTypePresent("), Query(string.Empty)];
        List<XamlConditionQuery> withCustom = [Query("IsApiContractPresent(X,7)"), Query("cond:Flag(On)", "using:My.App")];

        Assert.IsFalse(XamlConditionExchange.NeedsApplicationObjects(builtIn));
        Assert.IsTrue(XamlConditionExchange.NeedsApplicationObjects(withCustom));
        Assert.IsFalse(XamlConditionExchange.NeedsApplicationObjects([]));
    }

    [TestMethod]
    public void AnswersOfTheSurfaceBecomeTheAnswersOfVisualStudio()
    {
        // The surface can evaluate everything; Visual Studio knows nothing by itself.
        var surface = new XamlConditionService(new XamlConditionProbeSet
        {
            IsApiContractPresent = (_, major, _) => major <= 10,
            IsManagedTypePresent = name => name == "Microsoft.UI.Xaml.Controls.ColorPicker",
            EvaluateCustom = (type, argument) => type == "My.App.FeatureFlagCondition" ? argument == "NewExperience" : null,
        });
        var visualStudio = new XamlConditionService(new XamlConditionProbeSet());
        List<XamlConditionQuery> asked =
        [
            Query($"IsApiContractNotPresent({_Contract},7)"),
            Query($"IsApiContractPresent({_Contract},99)"),
            Query("IsTypePresent(Microsoft.UI.Xaml.Controls.ColorPicker)"),
            Query("cond:FeatureFlagCondition(NewExperience)", "using:My.App"),
            Query("cond:FeatureFlagCondition(LegacyMode)", "using:My.App"),
            Query("cond:NoSuchCondition(X)", "using:My.App"),
            Query("IsTypePresent("),
        ];

        // Through the wire format, as between the processes.
        EvaluateXamlConditionsRequest request = Deserialize<EvaluateXamlConditionsRequest>(Serialize(XamlConditionExchange.CreateRequest(asked)));
        EvaluateXamlConditionsResponse response = XamlConditionExchange.Answer(XamlConditionExchange.ReadQueries(request), surface);
        response = Deserialize<EvaluateXamlConditionsResponse>(Serialize(response));

        CollectionAssert.AreEqual(new bool?[] { false, false, true, true, false, false, false }, response.Outcomes);
        Assert.IsTrue(XamlConditionExchange.TryApply(asked, response, visualStudio, out bool changed));
        Assert.IsTrue(changed);

        // Visual Studio now answers like the surface, also for the other branch of a pair and for other spellings.
        Func<string, string?> namespaces = prefix => prefix == "cond" ? "using:My.App" : null;
        Assert.AreEqual(true, visualStudio.Evaluate($"IsApiContractPresent( {_Contract}, 7 )", namespaces));
        Assert.AreEqual(false, visualStudio.Evaluate($"IsApiContractNotPresent({_Contract},7)", namespaces));
        Assert.AreEqual(true, visualStudio.Evaluate($"IsApiContractNotPresent({_Contract},99)", namespaces));
        Assert.AreEqual(false, visualStudio.Evaluate("IsTypeNotPresent(Microsoft.UI.Xaml.Controls.ColorPicker)", namespaces));
        Assert.AreEqual(true, visualStudio.Evaluate("cond:FeatureFlagCondition(NewExperience)", namespaces));
        Assert.AreEqual(false, visualStudio.Evaluate("cond:FeatureFlagCondition(LegacyMode)", namespaces));
        Assert.AreEqual(false, visualStudio.Evaluate("cond:NoSuchCondition(X)", namespaces));

        // The same answers a second time change nothing.
        Assert.IsTrue(XamlConditionExchange.TryApply(asked, response, visualStudio, out changed));
        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void ReplyThatDoesNotMatchTheRequestIsNotApplied()
    {
        var conditions = new XamlConditionService(new XamlConditionProbeSet());
        List<XamlConditionQuery> asked = [Query("IsApiContractPresent(X,7)"), Query("IsTypePresent(My.Type)")];

        Assert.IsFalse(XamlConditionExchange.TryApply(asked, new EvaluateXamlConditionsResponse { Outcomes = [true] }, conditions, out bool changed));
        Assert.IsFalse(changed);
        Assert.IsFalse(XamlConditionExchange.TryApply(asked, new EvaluateXamlConditionsResponse(), conditions, out _));
        Assert.IsFalse(XamlConditionExchange.TryApply(asked, null, conditions, out _));
        Assert.IsNull(conditions.Evaluate("IsApiContractPresent(X,7)", _ => null));
    }

    [TestMethod]
    public void AnswerTheSurfaceCouldNotGiveLeavesTheQuestionOpen()
    {
        var conditions = new XamlConditionService(new XamlConditionProbeSet());
        List<XamlConditionQuery> asked = [Query("IsApiContractPresent(X,7)"), Query("IsTypePresent(My.Type)")];

        Assert.IsTrue(XamlConditionExchange.TryApply(asked, new EvaluateXamlConditionsResponse { Outcomes = [null, true] }, conditions, out bool changed));

        Assert.IsTrue(changed);
        Assert.IsNull(conditions.Evaluate("IsApiContractPresent(X,7)", _ => null));
        Assert.AreEqual(true, conditions.Evaluate("IsTypePresent(My.Type)", _ => null));
    }

    [TestMethod]
    public void EmptyRequestIsAnsweredByAnEmptyReply()
    {
        var conditions = new XamlConditionService(new XamlConditionProbeSet());

        EvaluateXamlConditionsResponse response = XamlConditionExchange.Answer([], conditions);

        Assert.AreEqual("{\"Outcomes\":[]}", Serialize(response));
        Assert.IsTrue(XamlConditionExchange.TryApply([], Deserialize<EvaluateXamlConditionsResponse>("{\"Outcomes\":[]}"), conditions, out bool changed));
        Assert.IsFalse(changed);
        Assert.IsTrue(XamlConditionExchange.TryApply([], Deserialize<EvaluateXamlConditionsResponse>("{}"), conditions, out _));
    }

    private static List<XamlConditionQuery> CreateQueries()
    {
        return
        [
            Query($"IsApiContractNotPresent({_Contract},7)"),
            Query("cond:FeatureFlagCondition(A, B)", "using:My.App"),
            Query("IsTypePresent("),
        ];
    }

    private static XamlConditionQuery Query(string condition, string? predicateNamespace = null)
    {
        return XamlConditionQuery.Create(condition, predicateNamespace);
    }

    /// <summary>
    /// Serializes a payload the way the designer protocol does: as UTF-16 JSON written by a data contract serializer.
    /// </summary>
    private static string Serialize(object payload)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(payload.GetType());
        using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.Unicode, ownsStream: false))
        {
            serializer.WriteObject(writer, payload);
            writer.Flush();
        }

        return Encoding.Unicode.GetString(stream.ToArray());
    }

    private static T Deserialize<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.Unicode.GetBytes(json));
        using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(stream, Encoding.Unicode, XmlDictionaryReaderQuotas.Max, null);
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(reader)!;
    }
}
