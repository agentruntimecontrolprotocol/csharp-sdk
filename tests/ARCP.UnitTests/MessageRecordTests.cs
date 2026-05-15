// SPDX-License-Identifier: Apache-2.0
using System;
using Arcp.Core.Agents;
using Arcp.Core.Errors;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using FluentAssertions;
using Xunit;

namespace Arcp.UnitTests;

public class MessageRecordTests
{
    [Theory]
    [InlineData(-1, null)]
    [InlineData(10, 5L)]
    public void Progress_body_rejects_invalid_values(long current, long? total)
    {
        var act = () => new ProgressBody { Current = current, Total = total }.Validate();
        act.Should().Throw<InvalidRequestException>();
    }

    [Theory]
    [InlineData("USD:5.00", "USD", "5.00")]
    [InlineData("credits:1000", "credits", "1000")]
    [InlineData("EUR:0.42", "EUR", "0.42")]
    public void BudgetAmount_parses_well_formed_amounts(string s, string currency, string amount)
    {
        BudgetAmount.Parse(s).Currency.Should().Be(currency);
        BudgetAmount.Parse(s).Amount.Should().Be(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("USD:-1")]
    [InlineData("USD:abc")]
    [InlineData("USD")]
    [InlineData(":1.00")]
    public void BudgetAmount_rejects_malformed(string s)
    {
        BudgetAmount.TryParse(s, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("echo", "echo", null)]
    [InlineData("code-refactor@2.0.0", "code-refactor", "2.0.0")]
    [InlineData("test-runner@1.0.0", "test-runner", "1.0.0")]
    public void AgentRef_parses(string s, string name, string? version)
    {
        var r = AgentRef.Parse(s);
        r.Name.Should().Be(name);
        r.Version.Should().Be(version);
        r.ToString().Should().Be(s);
    }

    [Theory]
    [InlineData("BAD UPPER@1")]
    [InlineData("@1.0.0")]
    [InlineData("name@")]
    public void AgentRef_rejects_malformed(string s)
    {
        AgentRef.TryParse(s, null, out _).Should().BeFalse();
    }
}
