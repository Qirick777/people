using People.Sim;
using Xunit;

public class NurtureTests
{
    private static int Age(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    private static (World w, Group g, List<int> heads) WithNursery()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).Where(i => Age(w, i) >= 16).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        g.Tasks.Clear();
        g.Tasks.Add(new OrgTask { Verb = Verb.Nurture, Village = g.Village, Weight = 1f });
        return (w, g, heads);
    }

    [Fact]
    public void NurseryNameFollowsTheFaction()
    {
        var (w, g, _) = WithNursery();
        Assert.Equal("유년 병영", w.UnitLabel(g));
    }

    [Fact]
    public void WardsGrowUpLoyal()
    {
        var (w, g, heads) = WithNursery();
        var p = w.People;
        int kid = Enumerable.Range(0, p.HighWater).First(k => p.IsAliveIndex(k) && Age(w, k) is >= 2 and < 10 && p.Household[k] != p.Household[heads[0]]);
        Assert.True(w.Entrust(kid, g));
        Assert.True(w.IsWard(kid));
        Assert.Equal(p.Household[heads[0]], p.Household[kid]);
        int uid = p.Uid[kid];
        w.Households.Food[p.Household[heads[0]]] += 500f;
        for (int y = 0; y < 3; y++) w.RecountNurture();
        Assert.True(w.OrgLoyalty(g, uid) > 0.4f);
    }

    [Fact]
    public void LongHerbUseCanLeaveBarren()
    {
        var (w, _, heads) = WithNursery();
        var p = w.People;
        var women = Enumerable.Range(0, p.HighWater).Where(k => p.IsAliveIndex(k) && !p.Male[k] && Age(w, k) is >= 16 and <= 40).Take(200).ToList();
        foreach (int i in women) w.TakeHerbNow(i, 30);
        Assert.Contains(women, w.IsBarren);
        Assert.Equal(0f, w.ConceptionFactor(women.First(w.IsBarren), heads[0]));
    }

    [Fact]
    public void AbandonOnlyOwnYoungChild()
    {
        var (w, _, _) = WithNursery();
        var p = w.People;
        int kid = Enumerable.Range(0, p.HighWater).First(k => p.IsAliveIndex(k) && Age(w, k) < 6 && p.IsValid(p.Mother[k]) && p.IsAliveIndex(p.Mother[k].Index) && p.Household[p.Mother[k].Index] == p.Household[k]);
        int mom = p.Mother[kid].Index;
        var def = w.Def(InteractionId.Abandon);
        Assert.True(w.CanOffer(def, new InteractionContext { Actor = mom, Target = kid, Third = -1 }));
        int other = Enumerable.Range(0, p.HighWater).First(k => p.IsAliveIndex(k) && Age(w, k) >= 16 && p.Household[k] != p.Household[kid]);
        Assert.False(w.CanOffer(def, new InteractionContext { Actor = other, Target = kid, Third = -1 }));
    }
}
