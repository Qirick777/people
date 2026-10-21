using People.Sim;
using Xunit;

public class FaithTests
{
    /// <summary>지배자 아래 가장들로 만든 신도회(설교).</summary>
    private static (World w, Group g, int leader) WithCult()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Brotherhood);
        foreach (int i in heads.Skip(1).Take(8)) w.JoinGroup(i, g);
        w.RecountOrgs();
        return (w, g, heads[0]);
    }

    [Fact]
    public void BelieversGrowIntoZealots()
    {
        var (w, g, leader) = WithCult();
        var p = w.People;
        Assert.True(w.IsFaith(g));
        int m = w.Members(g).First(x => x != leader);
        p.Piety[m] = 1f;
        Social.Add(p, m, g.LeaderUid, 80f);
        for (int y = 0; y < 40 && !w.IsZealot(p.Uid[m], g); y++) { w.BackdateFaith(p.Uid[m], 1); w.RecountDevotion(); }
        Assert.True(w.IsZealot(p.Uid[m], g));
        Assert.True(w.FaithOf(p.Uid[m], g) >= 0.9f);
    }

    [Fact]
    public void OfferingIsLimitedToOnceInThreeYears()
    {
        var (w, g, _) = WithCult();
        foreach (int h in w.Members(g).Select(i => w.People.Household[i]).Distinct()) w.Households.Coin[h] = 100f;
        float got = w.CallOffering(g);
        Assert.True(got > 0f);
        Assert.False(w.CanCallOffering(g));
        Assert.Equal(-1f, w.CallOffering(g));
    }

    [Fact]
    public void FifthTitheTakesAFifthFromZealots()
    {
        var (w, g, leader) = WithCult();
        var p = w.People;
        int m = w.Members(g).First(x => x != leader);
        int h = p.Household[m];
        w.Households.Food[h] = 0f; w.Households.Coin[h] = 0f;
        w.SetTithe(g, TitheRule.Fifth);
        Assert.Equal(0f, w.TitheFrom(g, m));   // 넉넉하지 않은 신도는 무거운 헌금을 내지 않는다
        w.Households.Food[h] = 2000f; w.Households.Coin[h] = 500f;
        w.MakeZealot(p.Uid[m], g);
        var inc = w.IncomeOf(h);
        float expect = 0.2f * MathF.Max(5f, inc.own + inc.tenant);
        Assert.Equal(expect, w.TitheFrom(g, m), 2);   // 광신도는 소출의 1/5을 곡식으로 낸다
    }
}
