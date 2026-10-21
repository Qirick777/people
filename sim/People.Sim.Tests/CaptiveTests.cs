using People.Sim;
using Xunit;

public class CaptiveTests
{
    private static (World w, Group g, int leader, List<int> others) WithBand()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Bandits);
        foreach (int i in heads.Skip(1).Take(4)) w.JoinGroup(i, g);
        w.SetUnitWeights(g, new[] { (Verb.Raid, 1f) });
        w.RecountOrgs();
        var others = heads.Skip(6).ToList();
        return (w, g, heads[0], others);
    }

    private static int AdultWithKin(World w, List<int> heads)
    {
        var p = w.People; var hh = w.Households;
        foreach (int h in heads)
            foreach (int m in Enumerable.Range(0, p.HighWater).Where(k => p.IsAliveIndex(k) && p.Household[k] == p.Household[h] && k != h))
                if ((w.Day - p.BirthDay[m]) / w.Config.DaysPerYear >= 16) return m;
        return -1;
    }

    private static int Child(World w)
    {
        var p = w.People;
        return Enumerable.Range(0, p.HighWater).First(k => p.IsAliveIndex(k) && (w.Day - p.BirthDay[k]) / w.Config.DaysPerYear is >= 3 and < 16 && w.Households.IsAlive(p.Household[k]));
    }

    [Fact]
    public void CaptiveStatusFollowsCaptor()
    {
        var (w, g, _, others) = WithBand();
        int v = others[0];
        Assert.True(w.Capture(g, v));
        Assert.True(w.IsCaptive(v));
        Assert.Equal(CaptiveKind.Abducted, w.CaptiveOf(v)!.Kind);
        w.Release(v);
        Assert.False(w.IsCaptive(v));
    }

    [Fact]
    public void IntimateActsAreClosedToCaptives()
    {
        var (w, g, leader, others) = WithBand();
        int v = others[0];
        w.Capture(g, v);
        var c = new InteractionContext { Actor = leader, Target = v, Third = -1 };
        Assert.False(w.CanOffer(w.Def(InteractionId.Propose), c));
        Assert.False(w.CanOffer(w.Def(InteractionId.Affair), c));
        var back = new InteractionContext { Actor = v, Target = leader, Third = -1 };
        Assert.False(w.CanOffer(w.Def(InteractionId.Affair), back));
    }

    [Fact]
    public void LeverageRaisesOrdinaryDemandsOnly()
    {
        var (w, g, leader, others) = WithBand();
        int kin = AdultWithKin(w, others);
        Assert.True(kin >= 0);
        int head = w.Households.Head[w.People.Household[kin]].Index;
        var plain = new InteractionDef { Accept = (_, _) => 0.3f };
        var intimate = new InteractionDef { Accept = (_, _) => 0.3f, Intimate = true };
        var c = new InteractionContext { Actor = leader, Target = head, Third = -1 };
        float before = w.AcceptChance(plain, c), beforeI = w.AcceptChance(intimate, c);
        Assert.True(w.Capture(g, kin));
        Assert.True(w.Leverage(leader, head) > 0f);
        Assert.True(w.AcceptChance(plain, c) - before > w.AcceptChance(intimate, c) - beforeI + 0.05f);
    }

    [Fact]
    public void ChildrenAreOnlyHeldReleasedOrRansomed()
    {
        var (w, g, leader, _) = WithBand();
        int kid = Child(w);
        Assert.True(w.Capture(g, kid));
        Assert.False(w.Execute(kid));
        Assert.False(w.Place(kid, g));
        Assert.Equal(0f, w.Leverage(leader, w.Households.Head[w.People.Household[kid]].Index));
        Assert.True(w.IsCaptive(kid));
    }

    [Fact]
    public void CaptivesAreNotPlacedInBrothelWork()
    {
        var (w, g, _, others) = WithBand();
        int v = others[0];
        w.Capture(g, v);
        var unit = w.SplitUnit(g) ?? g;
        w.SetUnitWeights(unit, new[] { (Verb.Brothel, 1f) });
        Assert.False(w.CanPlace(v, unit, w.RoleDefOf(RoleKind.Member)));
    }

    [Fact]
    public void NpcCaptorsLetGoInTime()
    {
        var (w, g, _, others) = WithBand();
        int v = others[0];
        w.Capture(g, v);
        w.BackdateCaptive(v, 3 * w.Config.DaysPerYear);
        for (int k = 0; k < 3 && w.IsCaptive(v); k++) w.RecountCaptives();
        Assert.False(w.IsCaptive(v));
    }
}
