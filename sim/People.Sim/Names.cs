namespace People.Sim;

/// <summary>
/// 이름. 성은 가문(부계)에서 내려오고, 이름은 사람마다 고유 번호로 정해진다(같은 시드면 같은 이름).
/// 화면과 기록에만 쓰고 시뮬레이션 계산에는 쓰지 않는다.
/// </summary>
public static class Names
{
    private static readonly string[] Male =
    {
        "알드릭", "버트럼", "세드릭", "에드먼드", "고드윈", "해럴드", "오즈릭", "롤랜드", "토비아스", "윌프레드",
        "휴", "월터", "길버트", "로버트", "사이먼", "토머스", "제프리", "리처드", "헨리", "랄프",
        "마틴", "피터", "애덤", "존", "윌리엄", "로저", "볼드윈", "콘라드", "램버트", "오즈윈",
        "아서", "베네딕트", "가레스", "레지널드", "앨런", "니콜라스", "스티븐", "필립", "레오", "에드윈",
    };

    private static readonly string[] Female =
    {
        "앨리스", "베아트리스", "세실리", "에디스", "엠마", "길리언", "헬레나", "이저벨", "조앤", "마틸다",
        "마저리", "모드", "아그네스", "엘레노어", "로즈", "시빌", "에이미", "클라라", "이다", "루시",
        "마리온", "넬", "오필리아", "페트로넬라", "로잘린드", "윈프리드", "에이다", "브리짓", "엘로이즈", "이브",
        "앤", "캐서린", "마거릿", "줄리아나", "레티스", "올리브", "펠리시아", "그레이스", "한나", "멜리사",
    };

    private static readonly string[] Surname =
    {
        "밀러", "스미스", "쿠퍼", "플레처", "헤이워드", "손더스", "애시포드", "브룩", "칼튼", "던모어",
        "펠로우즈", "그레이브스", "할로", "아이언사이드", "랭글리", "모턴", "노스우드", "페이지", "레드필드", "스톤",
        "손턴", "웨스트브룩", "윈터", "얼든", "블랙우드", "크로프트", "페어웰", "하우", "킹슬리", "로우",
        "애벗", "베일리", "콜드웰", "더들리", "엘우드", "포레스터", "개스킨", "홀브룩", "잉글비", "젠킨스",
    };

    private static uint Hash(int v)
    {
        uint x = (uint)v * 0x9E3779B1u;
        x ^= x >> 15; x *= 0x85EBCA77u; x ^= x >> 13;
        return x;
    }

    public static string Given(int uid, bool male)
    {
        var list = male ? Male : Female;
        return list[Hash(uid) % (uint)list.Length];
    }

    public static string Family(int family) => family < 0 ? "" : Surname[(uint)family % (uint)Surname.Length] + (family >= Surname.Length ? $" {family / Surname.Length + 1}" : "");

    public static string Full(Lineage l, int uid) => uid < 0 ? "" : $"{Given(uid, l.Male(uid))} {Family(l.FamilyOf(uid))}";
}
