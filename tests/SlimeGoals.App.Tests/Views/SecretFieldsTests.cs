using System.Xml.Linq;

namespace SlimeGoals.App.Tests.Views;

/// <summary>
/// MASVS-STORAGE-2: o teclado do sistema não aprende nem sugere o que se digita nos campos secretos da conta. As opções
/// chegam ao teclado do Android e ao método de entrada do Linux (IBus, Fcitx).
/// </summary>
public sealed class SecretFieldsTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>Senhas, códigos de recuperação, de confirmação e de convite.</summary>
    private static readonly HashSet<string> SecretBindings =
        ["Password", "PasswordConfirmation", "CurrentPassword", "RecoveryCodeInput", "ConfirmationCode", "InviteCode"];

    [Fact]
    public void Every_secret_field_of_the_account_view_hides_from_the_keyboard()
    {
        var fields = TextBoxes().Where(f => SecretBindings.Contains(f.Binding)).ToList();

        fields.Should().HaveCount(13, "seis senhas, três confirmações, a senha atual e três códigos");
        fields.Where(f => !f.Classes.Contains("secret"))
            .Select(f => $"linha {f.Line} ({f.Binding})")
            .Should().BeEmpty("campo secreto leva Classes=\"secret\"");
    }

    [Fact]
    public void Secret_style_turns_off_keyboard_learning_and_suggestions()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "SlimeGoals.App", "Styles", "Controls.axaml");
        var style = XDocument.Load(path).Descendants(Avalonia + "Style")
            .Single(s => (string?)s.Attribute("Selector") == "TextBox.secret");

        style.Elements(Avalonia + "Setter")
            .ToDictionary(s => (string)s.Attribute("Property")!, s => (string)s.Attribute("Value")!)
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["TextInputOptions.ContentType"] = "Password",
                ["TextInputOptions.IsSensitive"] = "True",
                ["TextInputOptions.ShowSuggestions"] = "False",
            });
    }

    [Fact]
    public void Every_masked_field_of_the_account_view_is_a_known_secret()
    {
        TextBoxes().Where(f => f.Masked && !SecretBindings.Contains(f.Binding))
            .Select(f => $"linha {f.Line} ({f.Binding})")
            .Should().BeEmpty("um campo novo com PasswordChar entra em SecretBindings");
    }

    private sealed record Field(string Binding, bool Masked, string[] Classes, int Line);

    private static IEnumerable<Field> TextBoxes()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "SlimeGoals.App", "Views", "AccountView.axaml");
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        return document.Descendants(Avalonia + "TextBox").Select(box => new Field(
            BindingOf((string?)box.Attribute("Text")),
            box.Attribute("PasswordChar") is not null,
            ((string?)box.Attribute("Classes") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries),
            ((System.Xml.IXmlLineInfo)box).LineNumber));
    }

    private static string BindingOf(string? text) =>
        text is { } t && t.StartsWith("{Binding ", StringComparison.Ordinal) ? t["{Binding ".Length..].TrimEnd('}') : "";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SlimeGoals.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Não achei a raiz do repositório.");
    }
}
