using People.Sim;
using Xunit;

public class ForceTests
{
    private static (World w, Group g, int lord) WithForce()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Watch);
        foreach (int i in heads.Skip(1).Take(8)) w.JoinGroup(i, g);
        w.RecountOrgs();
        return (w, g, lord);
    }

    private static int OtherHouse(World w, Group g, int lord)
    {
        var hh = w.Households;
        int home = w.LandHomeOf(g);
        return Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && h != home && h != lord && hh.Village[h] >= 0 && w.PatronOf(h) != home);
    }

    [Fact]
    public void LabelFollowsAttackTarget()
    {
        var (w, g, _) = WithForce();
        w.SetUnitWeights(g, new[] { (Verb.Chase, 1f) });
        Assert.Equal("치안대", w.UnitLabel(g));
        w.SetUnitWeights(g, new[] { (Verb.Raid, 1f) });
        Assert.Equal("도적 떼", w.UnitLabel(g));
        Assert.Equal("공격: 민간인", World.VerbName(Verb.Raid));
    }

    [Fact]
    public void HostilesComeFromWarAndDesignation()
    {
        var (w, g, lord) = WithForce();
        int other = OtherHouse(w, g, lord);
        Assert.False(w.CanUseVerb(g, Verb.Attack, out _));
        w.SetHostile(g, other, true);
        Assert.Equal("지정", w.HostilesOf(g)[other]);
        Assert.True(w.CanUseVerb(g, Verb.Attack, out _));
        w.SetHostile(g, other, false);
        w.MarkWarNow(w.LandHomeOf(g), other);
        Assert.Equal("전쟁 상대", w.HostilesOf(g)[other]);
    }

    [Fact]
    public void AttackHitsOnlyHostilesAndSparesStanding()
    {
        var (w, g, lord) = WithForce();
        var hh = w.Households;
        int other = OtherHouse(w, g, lord);
        hh.Food[other] += 500f;
        w.SetHostile(g, other, true);
        g.Tasks.Clear();
        g.Tasks.Add(new OrgTask { Verb = Verb.Attack, Village = hh.Village[other], Weight = 1f });
        float st = g.Standing;
        for (int k = 0; k < 20; k++) w.AttackNow();
        Assert.True(w.Stats.Attacks > 0);
        Assert.True(g.Standing >= st - 1e-4f);
    }

    [Fact]
    public void RaidWeighsMoreThanChase()
    {
        var (w, g, _) = WithForce();
        w.SetUnitWeights(g, new[] { (Verb.Chase, 1f) });
        float chase = w.KindWeight(g);
        w.SetUnitWeights(g, new[] { (Verb.Raid, 1f) });
        Assert.True(w.KindWeight(g) > chase);
    }
}
