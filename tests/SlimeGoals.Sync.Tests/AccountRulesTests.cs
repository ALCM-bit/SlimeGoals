using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync.Tests;

public class AccountRulesTests
{
    [Theory]
    [InlineData("ana@exemplo.com", "ana@exemplo.com")]
    [InlineData("  Ana.Silva+metas@Exemplo.COM.br ", "ana.silva+metas@exemplo.com.br")]
    [InlineData("a@b.co", "a@b.co")]
    [InlineData("\"nome estranho\"@exemplo.com", null)] // com espaço: fora do que o app aceita
    public void Email_is_trimmed_and_lowercased(string typed, string? expected) =>
        AccountRules.NormalizeEmail(typed).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana")]
    [InlineData("ana@")]
    [InlineData("@exemplo.com")]
    [InlineData("ana@exemplo")]
    [InlineData("ana@.com")]
    [InlineData("ana@exemplo.")]
    [InlineData("ana@exemplo..com")]
    [InlineData("ana@@exemplo.com")]
    [InlineData("ana@exem plo.com")]
    [InlineData("ana\u0000@exemplo.com")]
    [InlineData("ana\r\nBcc: x@y.com@exemplo.com")]
    public void Malformed_email_is_refused(string? typed) =>
        AccountRules.NormalizeEmail(typed).Should().BeNull();

    [Fact]
    public void Email_longer_than_the_limit_is_refused()
    {
        var domain = "@exemplo.com";
        var atLimit = new string('a', AccountRules.MaxEmailLength - domain.Length) + domain;

        AccountRules.NormalizeEmail(atLimit).Should().Be(atLimit);
        AccountRules.NormalizeEmail("a" + atLimit).Should().BeNull();
    }

    [Theory]
    [InlineData("notebook", "notebook")]
    [InlineData("  Celular da Ana  ", "Celular da Ana")]
    public void Device_name_is_trimmed(string typed, string expected) =>
        AccountRules.NormalizeDeviceName(typed).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pc\u0000")]
    [InlineData("pc\r\nlinha")]
    [InlineData("pc\tcom tab")]
    public void Empty_or_control_device_name_is_refused(string? typed) =>
        AccountRules.NormalizeDeviceName(typed).Should().BeNull();

    [Fact]
    public void Device_name_longer_than_the_limit_is_refused()
    {
        AccountRules.NormalizeDeviceName(new string('a', AccountRules.MaxDeviceNameLength)).Should().HaveLength(AccountRules.MaxDeviceNameLength);
        AccountRules.NormalizeDeviceName(new string('a', AccountRules.MaxDeviceNameLength + 1)).Should().BeNull();
    }
}
