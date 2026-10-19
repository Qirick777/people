using People.Sim;
using Xunit;

public class GroupTests
{
    [Fact]
    public void FoundJoinLeaveKeepCounts()
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 40 * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear >= 16).Take(3).ToList();
        var g = w.FoundGroup(adults[0], GroupKind.Watch);
        Assert.True(w.IsLeader(adults[0], g));
        Assert.True(w.JoinGroup(adults[1], g));
        Assert.True(w.JoinGroup(adults[1], g));          // 두 번 들어도 한 번
        Assert.Equal(2, g.Members);
        w.LeaveGroup(adults[1], g);
        Assert.Equal(1, g.Members);
        Assert.False(w.InGroup(adults[1], g));
    }
}
