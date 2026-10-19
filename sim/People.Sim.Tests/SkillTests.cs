using People.Sim;
using Xunit;

public class SkillTests
{
    [Fact]
    public void SelfPracticeApproachesTalentCapButNotBeyond()
    {
        float v = 0f, cap = Skills.Cap(0.5f);
        for (int y = 0; y < 60; y++) v = Skills.LearnStep(v, cap, 1f, 1f, -1f, 0.1f, 0.25f);
        Assert.InRange(v, cap - 0.02f, cap + 0.0001f);
    }

    [Fact]
    public void MasterLiftsLowTalentApprenticeAboveOwnCap()
    {
        // 재능 낮은 아이도 솜씨 좋은 스승 곁에서는 혼자 오를 높이를 넘는다(가업의 힘). 스승을 넘지는 못한다.
        float cap = Skills.Cap(0.2f), master = 0.9f;
        float alone = 0f, taught = 0f;
        for (int y = 0; y < 30; y++)
        {
            alone = Skills.LearnStep(alone, cap, 1f, 1f, -1f, 0.1f, 0.25f);
            taught = Skills.LearnStep(taught, cap, 1f, 1f, master, 0.1f, 0.25f);
        }
        Assert.True(taught > alone + 0.2f, $"혼자 {alone:F2}, 스승 곁 {taught:F2}");
        Assert.True(taught <= 0.9f * master + 1e-4f);
    }

    [Fact]
    public void TalentedLearnerSurpassesTeacherAlone()
    {
        // 재능이 높으면 스승보다 높이 오를 수 있다(스승 수준까지는 빨리, 그 뒤로는 혼자).
        float cap = Skills.Cap(0.95f), v = 0f;
        for (int y = 0; y < 40; y++) v = Skills.LearnStep(v, cap, 1.4f, 1f, 0.6f, 0.1f, 0.25f);
        Assert.True(v > 0.75f, $"{v:F2}");
    }
}
