using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class TagIdsTests
{
    [Fact]
    public void Ids_are_fixed_values()
    {
        TagIds.ForTag("estudos").Should().Be(Guid.Parse("cb7b580b-eb62-5496-863c-7a1a3b6741e2"));
        TagIds.ForTag("estudos").Version.Should().Be(5);
        TagIds.ForLink(Guid.Parse("0193a000-0000-7000-8000-000000000001"), "estudos")
            .Should().Be(Guid.Parse("c9b2dfc8-51e8-5244-9cfd-390257534f54"));
        TagIds.ForTag("medicamentos").Should().Be(Guid.Parse("e6f93147-2cb6-5a0e-98be-9a29644d12ec"));
        TagIds.ForTag("tarefas").Should().Be(Guid.Parse("6a6bce84-3d58-5cb9-8e5e-09b43cba54a0"));
    }

    [Fact]
    public void Same_name_in_nfd_and_nfc_gives_same_id() =>
        TagIds.ForTag(TagName.Normalize("Sau\u0301de")).Should().Be(TagIds.ForTag(TagName.Normalize("sa\u00fade")));
}
