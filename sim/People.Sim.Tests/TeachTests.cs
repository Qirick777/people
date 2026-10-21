using People.Sim;
using Xunit;

public class TeachTests
{
    private static (World w, Group g, List<int> folk) Group(GroupKind kind)
    {
        var w = new World(3, 400);
        w.SeedCouples(5);
        for (int d = 0; d < 60 * w.Config.DaysPerYear; d++) w.Step();
        var p = w.People;
        var adults = Enumerable.Range(0, p.HighWater).Where(i => p.IsAliveIndex(i) && (w.Day - p.BirthDay[i]) / w.Config.DaysPerYear is >= 18 and <= 45 && w.GroupsOf(i).Count() < World.GroupSlots).Take(6).ToList();
        var g = w.FoundGroup(adults[0], kind);
        foreach (int i in adults.Skip(1)) w.JoinGroup(i, g);
        return (w, g, adults);
    }

    [Fact]
    public void DoctrineRaisesRegardButHeirWithInheritTakesOverSmoothly()
    {
        var (w, g, folk) = Group(GroupKind.Brotherhood);
        var p = w.People;
        var before = folk.Skip(1).ToDictionary(i => i, i => Social.Opinion(p, i, g.LeaderUid));
        g.Lesson = Lesson.Doctrine; g.TeachShare = 0.3f; g.TeachManual = true;
        for (int d = 0; d < w.Config.DaysPerYear; d++) w.Step();
        // 남은 조직원의 평균이 오른다(한 사람은 그해 다른 일로 깎일 수 있다)
        var stay = folk.Skip(1).Where(i => p.IsAliveIndex(i) && w.InGroup(i, g)).ToList();
        if (stay.Count > 0) Assert.True(stay.Average(i => Social.Opinion(p, i, g.LeaderUid)) > stay.Average(i => before[i]));
        // 이음을 마친 후계자가 있으면 우두머리가 떠나도 그가 잇는다
        int heir = folk.Skip(1).First(i => w.InGroup(i, g));
        g.HeirUid = p.Uid[heir]; g.Inherit = 1f;
        int oldLeader = folk[0];
        long smooth = 0;
        w.LeaveGroup(oldLeader, g);
        for (int d = 0; d < w.Config.DaysPerYear; d++) { w.Step(); smooth += w.Stats.SuccessionSmooth; }
        if (g.Alive) Assert.Equal(p.Uid[heir], g.LeaderUid);
    }
}
