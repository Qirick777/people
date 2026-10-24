using People.Sim;
using Xunit;

public class WarTests
{
    [Fact]
    public void WarIsFoughtAndEndsOneOfThreeWays()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        // 지배자 나무 밖의 아무 집을 상대로 삼는다
        int t = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && h != lord && w.PatronOf(h) < 0 && w.TopOf(h) != w.TopOf(lord) && p.IsValid(hh.Head[h]));
        int w0 = w.Stats.WarWins, tr0 = w.Stats.WarTruces;
        w.StartWarNow(lord, t);
        Assert.True(w.InConflict(lord) && w.InConflict(t));
        Assert.False(w.CanWarNow(lord, t));   // 싸우는 중에는 다시 걸지 못한다
        for (int k = 0; k < 30 && w.Conflicts.Any(c => !c.Over); k++) w.TickConflicts();
        Assert.All(w.Conflicts, c => Assert.True(c.Over));
        if (w.Stats.WarWins > w0 && hh.IsAlive(t)) Assert.Equal(lord, w.PatronOf(t));
        if (w.Stats.WarTruces > tr0) Assert.False(w.CanWarNow(lord, t));   // 휴전 다섯 해
    }

    [Fact]
    public void BattlefieldLosesHarvestAndGetsDangerous()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        int v = w.Households.Village[lord];
        float d0 = w.DangerAt(v);
        for (int k = 0; k < 20; k++) w.RavageNow(v, 0.1f);
        Assert.Equal(0.6f, w.RavagedAt(v), 3);   // 많아야 0.6
        Assert.True(w.DangerAt(v) > d0);
        // 수확 때 깎고 지운다
        for (int d = 0; d < w.Config.DaysPerYear + 1 && w.RavagedAt(v) > 0f; d++) w.Step();
        Assert.Equal(0f, w.RavagedAt(v));
        Assert.True(w.Stats.RavagedLoss > 0f || w.Stats.Harvest == 0f);
    }

    [Fact]
    public void PlayerChoosesStanceAndCanGiveUp()
    {
        var (w, lord) = TestWorlds.WithLord(12);
        var hh = w.Households; var p = w.People;
        int t = Enumerable.Range(0, hh.HighWater).First(h => hh.IsAlive(h) && h != lord && w.PatronOf(h) < 0 && w.TopOf(h) != w.TopOf(lord) && p.IsValid(hh.Head[h]));
        w.PlayerHousehold = lord;
        w.StartWarNow(lord, t);
        var c = w.PlayerConflict;
        Assert.NotNull(c);
        Assert.Same(c!.A, w.PlayerSide(c));
        Assert.False(w.SetStance(c, Stance.Hold));    // 쳐들어가는 쪽은 버틸 수 없다
        Assert.True(w.SetStance(c, Stance.Siege));
        w.TakeFork();
        w.TickConflicts();
        if (!c.Over) Assert.True(w.TakeFork(), "플레이어가 낀 분쟁은 진행마다 멈춘다");
        if (!c.Over) { Assert.True(w.GiveUp(c)); Assert.True(c.Over); Assert.Equal("격퇴", c.Outcome); }
        Assert.Null(w.PlayerConflict);
    }
}
