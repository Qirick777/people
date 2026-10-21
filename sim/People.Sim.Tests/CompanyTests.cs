using People.Sim;
using Xunit;

public class CompanyTests
{
    private static (World w, Group g, int leader, int lord) WithCompany()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        var heads = Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]))
            .Select(h => TestWorlds.Free(w, hh.Head[h].Index)).ToList();
        var g = w.FoundGroup(heads[0], GroupKind.Company);
        foreach (int i in heads.Skip(1).Take(6)) w.JoinGroup(i, g);
        g.ShareRule = ShareRule.Treasury;
        w.RecountOrgs();
        return (w, g, heads[0], lord);
    }

    [Fact]
    public void ContractDeliversAndPays()
    {
        var (w, g, _, lord) = WithCompany();
        g.Store[(int)Good.Arms] = 10f;
        w.Households.Coin[lord] = 500f;
        var c = w.MakeContract(g, lord, Good.Arms, 2f, 8f, 3);
        Assert.NotNull(c);
        float before = g.Treasury, armsBefore = w.GoodOfHouse(lord, Good.Arms);
        w.RecountContracts();
        Assert.Equal(8f, g.Store[(int)Good.Arms], 3);
        Assert.Equal(armsBefore + 2f, w.GoodOfHouse(lord, Good.Arms), 3);
        Assert.True(g.Treasury > before);
    }

    [Fact]
    public void MissedDeliveriesBreakTheContract()
    {
        var (w, g, _, lord) = WithCompany();
        g.Store[(int)Good.Arms] = 0f;
        w.MakeContract(g, lord, Good.Arms, 2f, 8f, 5);
        float st = g.Standing;
        w.RecountContracts(); w.RecountContracts();
        Assert.DoesNotContain(w.Contracts, k => k.Seller == g.Id && k.Buyer == lord);
        Assert.True(g.Standing < st);
    }

    [Fact]
    public void LendingCreatesDebtToTheCompany()
    {
        var (w, g, leader, _) = WithCompany();
        var hh = w.Households;
        g.Treasury = 1000f;
        int v = hh.Village[w.People.Household[leader]];
        foreach (int h in Enumerable.Range(0, hh.HighWater).Where(h => hh.IsAlive(h) && hh.Village[h] == v)) { hh.Food[h] = 0f; hh.Coin[h] = 0f; }
        hh.Food[w.People.Household[leader]] = 500f;
        int n = w.LendNow(g, v);
        Assert.True(n > 0);
        Assert.True(g.Treasury < 1000f);
    }
}
