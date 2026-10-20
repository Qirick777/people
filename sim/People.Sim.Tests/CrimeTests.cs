using People.Sim;
using Xunit;

public class CrimeTests
{
    private static World Grown(ulong seed, int years)
    {
        var w = new World(seed, 400);
        w.SeedCouples(5);
        for (int d = 0; d < years * w.Config.DaysPerYear; d++) w.Step();
        return w;
    }

    private static int Years(World w, int i) => (w.Day - w.People.BirthDay[i]) / w.Config.DaysPerYear;

    [Fact]
    public void TheftMovesFoodAndLeavesARecord()
    {
        var w = Grown(3, 60);
        var p = w.People; var hh = w.Households;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && Years(w, i) >= 16 && hh.IsAlive(p.Household[i])).ToList();
        int thief = adults[0];
        int victim = adults.Where(i => p.Household[i] != p.Household[thief]).OrderByDescending(i => hh.Food[p.Household[i]]).First();
        hh.Food[p.Household[victim]] += 500f;
        float before = hh.Food[p.Household[thief]];
        int n = w.Crimes.Count;
        // 성공할 때까지(확률이 있다)
        for (int k = 0; k < 40 && hh.Food[p.Household[thief]] <= before; k++)
            w.Interact(InteractionId.Steal, new InteractionContext { Actor = thief, Target = victim, Third = -1 });
        Assert.True(hh.Food[p.Household[thief]] > before);
        Assert.True(w.Crimes.Count > n);
        Assert.Contains(w.Crimes, c => c.Type == CrimeType.Theft && c.Culprit == p.Uid[thief] && !c.Attempt);
    }

    [Fact]
    public void PunishingRaisesAndCoveringLowersVictimsView()
    {
        var (w, lord) = TestWorlds.WithLord(3);
        var p = w.People; var hh = w.Households;
        int vh = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && w.PatronOf(h) == lord && p.IsValid(hh.Head[h]));
        int victim = hh.Head[vh].Index, lu = p.Uid[hh.Head[lord].Index];
        int culprit = Enumerable.Range(0, p.HighWater).First(i => p.IsAliveIndex(i) && Years(w, i) >= 16 && p.Household[i] != vh && p.Household[i] != lord);
        Crime Make() => new Crime { Type = CrimeType.Theft, Culprit = p.Uid[culprit], Victim = p.Uid[victim], VictimHouse = vh, Village = hh.Village[vh], Lord = lord, VictimKnows = true, Day = w.Day };
        float o0 = Social.Opinion(p, victim, lu);
        w.Judge(Make(), Verdict.Fine);
        float o1 = Social.Opinion(p, victim, lu);
        Assert.True(o1 > o0);
        w.Judge(Make(), Verdict.Covered);
        Assert.True(Social.Opinion(p, victim, lu) < o1);
    }
}
