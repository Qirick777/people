using People.Sim;
using Xunit;

public class ReligionTests
{
    private static (World w, Group g, int leader) WithCult()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Brotherhood);
        foreach (int i in heads.Skip(1).Take(8)) w.JoinGroup(i, g);
        w.RecountOrgs();
        w.RecountDevotion();   // 종교가 정해진다
        return (w, g, heads[0]);
    }

    [Fact]
    public void WorldHasReligionsAndCultJoinsOne()
    {
        var (w, g, _) = WithCult();
        Assert.True(w.Religions.Count >= 3);
        Assert.True(g.ReligionId >= 0);
        Assert.True(w.IsFaith(g));
    }

    [Fact]
    public void FoundingStartsUnknownAndRanksOverride()
    {
        var (w, g, leader) = WithCult();
        var r = w.FoundReligion(g, "시험교", new[] { (Doctrine.Chastity, Strength.Strong), (Doctrine.Fertility, Strength.Mid), (Doctrine.Learning, Strength.Weak) })!;
        Assert.Equal(r.Id, g.ReligionId);
        for (int v = 0; v < r.Known.Count; v++) Assert.True(r.Known[v] <= 0.1f);
        int m = w.Members(g).First(x => x != leader);
        w.People.Piety[m] = 0.1f;   // 사제가 아닌 신도
        Assert.Equal(1.5f, w.DoctrineOn(m, Doctrine.Chastity), 3);
        Assert.True(w.SetRankStrength(g, (int)Rank.Believer, Doctrine.Chastity, Strength.Exempt));
        Assert.Equal(0f, w.DoctrineOn(m, Doctrine.Chastity), 3);
        Assert.Equal(1.5f, w.DoctrineOn(leader, Doctrine.Chastity), 3);   // 우두머리는 그대로
    }

    [Fact]
    public void OppositeDoctrinesCannotBothBeStrongOnOneRank()
    {
        var (w, g, _) = WithCult();
        w.FoundReligion(g, "", new[] { (Doctrine.Chastity, Strength.Weak), (Doctrine.Liberty, Strength.Weak), (Doctrine.Doom, Strength.Mid) });
        Assert.True(w.SetRankStrength(g, (int)Rank.Leader, Doctrine.Liberty, Strength.Strong));
        Assert.False(w.SetRankStrength(g, (int)Rank.Leader, Doctrine.Chastity, Strength.Strong));
        Assert.True(w.SetRankStrength(g, (int)Rank.Believer, Doctrine.Chastity, Strength.Strong));
    }

    [Fact]
    public void ChastityLowersAffairAcceptance()
    {
        var (w, g, leader) = WithCult();
        int m = w.Members(g).First(x => x != leader);
        w.People.Piety[m] = 0.1f;   // 사제가 아닌 신도
        var def = new InteractionDef { Id = InteractionId.Affair, Accept = (_, _) => 0.5f };
        var ctx = new InteractionContext { Actor = leader, Target = m, Third = -1 };
        w.FoundReligion(g, "", new[] { (Doctrine.Chastity, Strength.Strong) });
        Assert.Equal(0.2f, w.AcceptChance(def, ctx), 3);
        w.SetRankStrength(g, (int)Rank.Believer, Doctrine.Chastity, Strength.Exempt);
        Assert.Equal(0.5f, w.AcceptChance(def, ctx), 3);
    }

    [Fact]
    public void BelieversWithoutWorshipCoolDown()
    {
        var (w, g, leader) = WithCult();
        var p = w.People;
        foreach (int i in w.Members(g)) p.Piety[i] = 0.1f;   // 사제가 없다
        int m = w.Members(g).First(x => x != leader);
        float before = w.FaithOf(p.Uid[m], g);
        w.RecountDevotion();
        Assert.True(w.FaithOf(p.Uid[m], g) < before);
    }
}
