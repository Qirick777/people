using People.Sim;
using Xunit;

public class KoreanTests
{
    [Theory]
    [InlineData("밀러", "밀러를", "밀러로", "밀러가")]
    [InlineData("살인", "살인을", "살인으로", "살인이")]
    [InlineData("도둑질", "도둑질을", "도둑질로", "도둑질이")]
    [InlineData("'공개'", "'공개'를", "'공개'로", "'공개'가")]
    [InlineData("'일부 은폐'", "'일부 은폐'를", "'일부 은폐'로", "'일부 은폐'가")]
    [InlineData("3", "3을", "3으로", "3이")]
    public void ParticlesFollowTheLastSound(string noun, string obj, string ro, string subj)
    {
        Assert.Equal(obj, World.Obj(noun));
        Assert.Equal(ro, World.Ro(noun));
        Assert.Equal(subj, World.Subj(noun));
    }
}
