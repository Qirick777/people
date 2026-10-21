using People.Sim;
using Xunit;

public class AuthorityTests
{
    [Fact]
    public void HiddenOrgIsUnseenOutsideAndSplitMakesWorkersAndMembers()
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 60 * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16 && w.GroupsOf(i).Count() < World.GroupSlots).ToList();
        var g = w.FoundGroup(adults[0], GroupKind.Bandits);
        // 도적 떼는 일부 은폐로 선다(아주 신중한 NPC 두목은 완전 은폐)
        Assert.Equal(p.Get(adults[0], Trait.Caution) > 0.7f ? Visibility.Hidden : Visibility.Covert, g.Visibility);
        w.SetVisibility(g, Visibility.Covert);
        foreach (int i in adults.Skip(1).Take(10)) w.JoinGroup(i, g);
        w.RecountOrgs();
        Assert.True(g.Authority > 0f);
        int outsider = adults.Skip(11).First(i => !w.InGroup(i, g));
        // 우두머리는 거의 다 알고(±20%), 바깥은 모른다(새어 나간 것이 없으니)
        Assert.InRange(w.PerceivedAuthority(adults[0], g), 0.79f * g.Authority, 1.21f * g.Authority);
        Assert.Equal(0f, w.PerceivedAuthority(outsider, g));
        w.SetVisibility(g, Visibility.Public);
        Assert.True(w.PerceivedAuthority(outsider, g) > 0f);
        // 갈래가 생기면 갈래 사람은 일꾼, 맨 위에 남은 사람은 소속원
        Assert.Equal(OrgRole.Worker, w.RoleIn(adults[1], g));
        var c = w.SplitUnit(g)!;
        int worker = adults.Skip(1).Take(10).First(i => w.InGroup(i, c));
        int member = adults.Skip(1).Take(10).First(i => w.InGroup(i, g) && !w.InGroup(i, c));
        Assert.Equal(OrgRole.Worker, w.RoleIn(worker, g));
        Assert.Equal(OrgRole.Member, w.RoleIn(member, g));
        Assert.Equal(OrgRole.Leader, w.RoleIn(adults[0], g));
    }
}
