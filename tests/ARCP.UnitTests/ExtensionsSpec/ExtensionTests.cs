using System.Text.Json;
using ARCP.Errors;
using ARCP.Extensions;
using FluentAssertions;
using Xunit;

namespace ARCP.UnitTests.ExtensionsSpec;

public class ExtensionNamespaceTests
{
    [Theory]
    [InlineData("arcpx.acme.feature.v1", true)]
    [InlineData("com.acme.workflow.v2", true)]
    [InlineData("arcpx.acme.foo.bar.v1", true)]
    [InlineData("x-foo.bar.v1", false)]
    [InlineData("arcpx.acme.v1", false)]
    [InlineData("arcpx..feature.v1", false)]
    [InlineData("ARCPX.acme.feature.v1", false)]
    [InlineData("arcpx.acme.feature", false)]
    [InlineData("session.open", false)]
    [InlineData("", false)]
    public void IsValid(string name, bool expected)
    {
        ExtensionNamespace.IsValid(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("session.open")]
    [InlineData("ping")]
    [InlineData("metric")]
    [InlineData("trace.span")]
    public void IsCoreType_ReturnsTrueForKnown(string type)
    {
        ExtensionNamespace.IsCoreType(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("session.something_invalid")]
    [InlineData("tool.bogus")]
    [InlineData("trace.unknown")]
    public void LooksLikeCoreType_RecognizesUnknownCorePrefixes(string type)
    {
        ExtensionNamespace.LooksLikeCoreType(type).Should().BeTrue();
    }

    [Fact]
    public void ValidateExtensionsObject_AllowsOptionalKey()
    {
        var dict = new Dictionary<string, object?>
        {
            ["optional"] = true,
            ["arcpx.acme.feature.v1"] = "x",
        };
        Action act = () => ExtensionNamespace.ValidateExtensionsObject(dict);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateExtensionsObject_RejectsBadKey()
    {
        var dict = new Dictionary<string, object?> { ["x-bad"] = 1 };
        Action act = () => ExtensionNamespace.ValidateExtensionsObject(dict);
        act.Should().Throw<InvalidArgumentException>();
    }
}

public class ExtensionDispatchTests
{
    [Fact]
    public void UnknownCorePrefix_Nacks()
    {
        var d = ExtensionDispatch.Classify("session.totally_invalid");
        d.Kind.Should().Be(UnknownTypeDispositionKind.Nack);
        d.Code.Should().Be(ErrorCode.Unimplemented);
    }

    [Fact]
    public void UnregisteredOptionalExtension_Drops()
    {
        var ext = new Dictionary<string, object?> { ["optional"] = true };
        var d = ExtensionDispatch.Classify("arcpx.acme.feature.v1", ext);
        d.Kind.Should().Be(UnknownTypeDispositionKind.Drop);
    }

    [Fact]
    public void UnregisteredRequiredExtension_Nacks()
    {
        var d = ExtensionDispatch.Classify("arcpx.acme.feature.v1");
        d.Kind.Should().Be(UnknownTypeDispositionKind.Nack);
    }

    [Fact]
    public void GarbageType_Nacks()
    {
        var d = ExtensionDispatch.Classify("totally_bogus");
        d.Kind.Should().Be(UnknownTypeDispositionKind.Nack);
    }
}

public class ExtensionRegistryTests
{
    private sealed record ExtPayload(string Foo) : ARCP.Envelope.MessageType
    {
        public override string WireType => "arcpx.acme.feature.v1";
    }

    [Fact]
    public void RegisterAndResolve()
    {
        var r = new ExtensionRegistry();
        r.Register<ExtPayload>("arcpx.acme.feature.v1");
        r.Has("arcpx.acme.feature.v1").Should().BeTrue();
        r.Resolve("arcpx.acme.feature.v1").Should().BeSameAs(typeof(ExtPayload));
    }

    [Fact]
    public void RegisterRejectsInvalidNamespace()
    {
        var r = new ExtensionRegistry();
        Action act = () => r.Register<ExtPayload>("session.open");
        act.Should().Throw<InvalidArgumentException>();
    }

    [Fact]
    public void Parse_DeserializesToRegisteredType()
    {
        var r = new ExtensionRegistry();
        r.Register<ExtPayload>("arcpx.acme.feature.v1");
        var elem = JsonDocument.Parse("""{"foo":"bar"}""").RootElement.Clone();
        var result = r.Parse("arcpx.acme.feature.v1", elem, ARCP.Envelope.EnvelopeJson.Options) as ExtPayload;
        result.Should().NotBeNull();
        result!.Foo.Should().Be("bar");
    }

    [Fact]
    public void Parse_ThrowsForUnregistered()
    {
        var r = new ExtensionRegistry();
        var elem = JsonDocument.Parse("{}").RootElement.Clone();
        Action act = () => r.Parse("arcpx.acme.unknown.v1", elem);
        act.Should().Throw<UnimplementedException>();
    }
}
