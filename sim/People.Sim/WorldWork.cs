namespace People.Sim;

/// <summary>
/// 3단계: 일과 배움. 재능(선천)은 오를 수 있는 높이를, 연습(후천)은 실제 기술을 정한다.
/// 아이는 집안 어른이나 스승 곁에서 일하며 배운다(가업). 농사 밖의 일은 마을에 수요가 있을 때만 생긴다.
/// </summary>
public sealed partial class World
{
    // 마을별 일 상태. 해마다 다시 계산한다.
    private readonly List<float> _healerSkill = new(), _traderSkill = new();
    private readonly List<int> _healerHh = new(), _traderHh = new(), _carpenterHh = new();
    private readonly List<float> _carpenterSkill = new();
    private readonly List<int> _buildsThisYear = new(), _buildsLastYear = new();

    /// <summary>가구의 농사 솜씨(일손 가중 평균). 수확에 곱한다.</summary>
    private float[] _farmSkill = new float[256];

    private void EnsureVillageWork()
    {
        int n = VillageList.Count;
        while (_healerSkill.Count < n)
        {
            _healerSkill.Add(0); _traderSkill.Add(0); _carpenterSkill.Add(0);
            _healerHh.Add(-1); _traderHh.Add(-1); _carpenterHh.Add(-1);
            _buildsThisYear.Add(0); _buildsLastYear.Add(0);
        }
        if (_farmSkill.Length < Households.HighWater) Array.Resize(ref _farmSkill, Math.Max(Households.HighWater, _farmSkill.Length * 2));
    }

    public float FarmSkillOf(int h) => h < _farmSkill.Length ? _farmSkill[h] : 0.5f;

    /// <summary>시작 인구의 기술: 농사는 누구나 조금, 다른 기술은 드물게.</summary>
    private void SeedSkills(int i, int age)
    {
        var p = People;
        if (age < 8) return;
        float k = Math.Min(1f, (age - 8) / 20f);
        for (int s = 0; s < (int)Skill.Count; s++)
        {
            var sk = (Skill)s;
            float cap = Skills.Cap(Skills.Talent(p, i, sk));
            float chance = sk switch { Skill.Farming => 1f, Skill.Letters => 0.05f, _ => 0.08f };
            if (!_rng.Chance(chance)) continue;
            // 글은 아는 사람만 제대로 안다(가르칠 수 있을 만큼)
            float lo = sk == Skill.Letters ? 0.6f : 0.3f;
            p.SetSkill(i, sk, cap * MathF.Max(k, sk == Skill.Letters ? 0.8f : 0f) * (lo + 0.4f * _rng.NextFloat()));
        }
    }

    /// <summary>집 짓기·늘리기 값. 마을 목수가 있으면 목수 솜씨만큼 싸지고 값은 목수 집이 받는다.</summary>
    private float PayCarpenter(int payer, float baseCost)
    {
        var hh = Households;
        int v = hh.IsAlive(payer) ? hh.Village[payer] : -1;
        float cost = baseCost;
        if (v >= 0 && v < _carpenterHh.Count)
        {
            _buildsThisYear[v]++;
            int ch = _carpenterHh[v];
            if (ch >= 0 && hh.IsAlive(ch) && ch != payer)
            {
                cost = baseCost * (1.3f - 0.6f * _carpenterSkill[v]);
                float paid = MathF.Min(cost, hh.Food[payer]);
                hh.Food[ch] += paid;
                hh.Food[payer] -= paid;
                return paid;
            }
        }
        float own = MathF.Min(cost, hh.Food[payer]);
        hh.Food[payer] -= own;
        return own;
    }

    /// <summary>마을 약초꾼의 솜씨(없으면 0). 아기·산모 사망을 줄인다.</summary>
    private float HealerSkillAt(int h)
    {
        int v = Households.IsAlive(h) ? Households.Village[h] : -1;
        return v >= 0 && v < _healerSkill.Count ? _healerSkill[v] : 0f;
    }

    /// <summary>아이가 태어나면 약초꾼 집에 사례를 낸다.</summary>
    private void PayHealer(int h)
    {
        var hh = Households;
        int v = hh.IsAlive(h) ? hh.Village[h] : -1;
        if (v < 0 || v >= _healerHh.Count) return;
        int hl = _healerHh[v];
        if (hl < 0 || !hh.IsAlive(hl) || hl == h) return;
        float fee = MathF.Min(Config.HealerFee, hh.Food[h]);
        hh.Food[h] -= fee;
        hh.Food[hl] += fee;
    }

    /// <summary>
    /// 썩는 양. 마을 장사꾼이 남는 곡식을 제때 팔아 썩는 양을 줄이고, 아낀 양의 일부를 받는다.
    /// </summary>
    private void SpoilWithTrade()
    {
        var hh = Households;
        float rate = Config.SpoilagePerSeason;
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            int v = hh.Village[h];
            float t = v >= 0 && v < _traderSkill.Count ? _traderSkill[v] : 0f;
            float loss = hh.Food[h] * rate;
            if (t > 0f)
            {
                float saved = loss * Config.TradeSaving * t;
                loss -= saved;
                int th = _traderHh[v];
                if (th >= 0 && hh.IsAlive(th) && th != h)
                {
                    float fee = saved * Config.TradeFee;
                    hh.Food[th] += fee;
                    loss += fee;
                }
            }
            hh.Food[h] -= loss;
        }
    }

    /// <summary>16살이 될 때 어린 시절에 겪은 일을 태그로 남기고 성격 축을 옮긴다.</summary>
    private void ApplyExperience(int i)
    {
        var p = People;
        int uid = p.Uid[i];
        var tag = ExperienceTag.None;
        if (p.ChildhoodHungryDays[i] > 16 * Config.DaysPerYear / 10) tag |= ExperienceTag.HungryChildhood;
        int birth = p.BirthDay[i], twelve = birth + 12 * Config.DaysPerYear;
        if (uid >= 0)
        {
            int f = Lineage.Father(uid), m = Lineage.Mother(uid);
            bool lost = (f >= 0 && Lineage.Death(f) >= 0 && Lineage.Death(f) < twelve) || (m >= 0 && Lineage.Death(m) >= 0 && Lineage.Death(m) < twelve);
            if (lost) tag |= ExperienceTag.EarlyOrphan;
            if (m >= 0 && Lineage.Children(m) >= 9) tag |= ExperienceTag.BigFamily;
        }
        p.Experience[i] = (byte)tag;
        if ((tag & ExperienceTag.HungryChildhood) != 0) Nudge(i, Trait.Ambition, 0.15f);
        if ((tag & ExperienceTag.EarlyOrphan) != 0) { Nudge(i, Trait.Caution, -0.1f); Nudge(i, Trait.Ambition, 0.1f); }
        if ((tag & ExperienceTag.BigFamily) != 0) Nudge(i, Trait.Sociability, 0.1f);
    }

    private void Nudge(int i, Trait t, float d) => People.Set(i, t, Math.Clamp(People.Get(i, t) + d, 0f, 1f));

    /// <summary>
    /// 한 해의 일: 마을마다 일자리 수를 정해 사람을 앉히고, 아이에게 스승을 붙이고, 모두가 한 해 연습한다.
    /// </summary>
    private void YearOfWork()
    {
        EnsureVillageWork();
        var p = People;
        var hh = Households;
        var vl = VillageList;
        int nv = vl.Count;
        for (int v = 0; v < nv; v++) { _buildsLastYear[v] = _buildsThisYear[v]; _buildsThisYear[v] = 0; }

        // 1) 마을별 어른·아이 목록
        var adults = new List<int>[nv];
        var kids = new List<int>[nv];
        var holders = new List<int>[nv * (int)Job.Count];
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int h = p.Household[i];
            int v = hh.IsAlive(h) ? hh.Village[h] : -1;
            int age = AgeYears(i);
            var job = (Job)p.JobOf[i];
            if (v < 0 || age > 65) { p.JobOf[i] = 0; job = Job.Farmer; }
            if (v < 0) continue;
            if (age >= 18) (adults[v] ??= new()).Add(i);
            else if (age >= 8) (kids[v] ??= new()).Add(i);
            if (job != Job.Farmer) (holders[v * (int)Job.Count + (int)job] ??= new()).Add(i);
        }

        // 2) 일자리: 수요만큼 앉히고 남으면 솜씨 낮은 사람부터 내린다
        for (int v = 0; v < nv; v++)
        {
            if (!vl.Alive[v]) continue;
            int n = vl.Households[v];
            // 마을 수요로 정하는 일(목수·약초꾼·장사꾼). 군인·징수관은 지배자가 고용한다(WorldPower).
            for (int j = 1; j <= (int)Job.Trader; j++)
            {
                var job = (Job)j;
                int slots = job switch
                {
                    Job.Carpenter => n >= 8 ? Math.Clamp((int)MathF.Round(_buildsLastYear[v] / 4f), 1, Math.Max(1, n / 6)) : 0,
                    Job.Healer => n >= 10 ? 1 + n / 40 : 0,
                    Job.Trader => n >= 15 ? Math.Max(1, n / 30) : 0,
                    _ => 0,
                };
                var list = holders[v * (int)Job.Count + j] ??= new();
                var sk = Skills.SkillOf(job);
                list.Sort((a, b) => p.GetSkill(b, sk).CompareTo(p.GetSkill(a, sk)));
                while (list.Count > slots) { p.JobOf[list[^1]] = 0; list.RemoveAt(list.Count - 1); }
                var pool = adults[v];
                while (list.Count < slots && pool != null)
                {
                    int best = -1;
                    float bs = float.MinValue;
                    foreach (int a in pool)
                    {
                        if (p.JobOf[a] != 0 || AgeYears(a) > 60) continue;
                        float sc = p.GetSkill(a, sk) + 0.3f * Skills.Talent(p, a, sk) + 0.15f * p.Get(a, Trait.Ambition);
                        if (sc > bs) { bs = sc; best = a; }
                    }
                    if (best < 0) break;
                    p.JobOf[best] = (byte)job;
                    Lineage.SetJob(p.Uid[best], job);
                    list.Add(best);
                    Stats.NewJobs++;
                    int fu = Lineage.Father(p.Uid[best]), mu = Lineage.Mother(p.Uid[best]);
                    if (Lineage.JobOf(fu) == job || Lineage.JobOf(mu) == job) Stats.InheritedJobs++;
                    else if (!p.Master[best].IsNone || WasApprentice(best, job)) Stats.ApprenticedJobs++;
                }
            }
            // 마을이 쓰는 대표 솜씨(가장 잘하는 사람)
            _healerSkill[v] = 0; _traderSkill[v] = 0; _carpenterSkill[v] = 0;
            _healerHh[v] = _traderHh[v] = _carpenterHh[v] = -1;
            Best(holders[v * (int)Job.Count + (int)Job.Healer], Skill.Herbalism, out float hs, out int hhh);
            Best(holders[v * (int)Job.Count + (int)Job.Trader], Skill.Trade, out float ts, out int thh);
            Best(holders[v * (int)Job.Count + (int)Job.Carpenter], Skill.Carpentry, out float cs, out int chh);
            _healerSkill[v] = hs; _healerHh[v] = hhh;
            _traderSkill[v] = ts * (0.7f + 0.3f * MathF.Min(1f, LettersOfBest(holders[v * (int)Job.Count + (int)Job.Trader]) * 2f));
            _traderHh[v] = thh;
            _carpenterSkill[v] = cs; _carpenterHh[v] = chh;
        }

        // 3) 도제: 일을 가진 사람 중 집안에 배우는 아이가 없으면 마을에서 재능 있는 아이를 받는다
        for (int v = 0; v < nv; v++)
        {
            var ks = kids[v];
            if (ks == null) continue;
            foreach (int k in ks)
            {
                var m = p.Master[k];
                if (!m.IsNone && (!p.IsValid(m) || p.JobOf[m.Index] == 0 || AgeYears(k) >= 16)) p.Master[k] = PersonId.None;
            }
            for (int j = 1; j <= (int)Job.Collector; j++)   // 몸 파는 일은 도제를 두지 않는다
            {
                var list = holders[v * (int)Job.Count + j];
                if (list == null) continue;
                var sk = Skills.SkillOf((Job)j);
                foreach (int master in list)
                {
                    int mh = p.Household[master];
                    bool hasLearner = false;
                    foreach (int k in ks)
                        if ((p.Household[k] == mh && p.Master[k].IsNone) || p.Master[k].Index == master) { hasLearner = true; break; }
                    if (hasLearner) continue;
                    int best = -1;
                    float bt = 0.45f;   // 재능이 이보다 낮은 아이는 받지 않는다
                    foreach (int k in ks)
                    {
                        if (!p.Master[k].IsNone || AgeYears(k) < 10 || HouseholdHasJob(p.Household[k])) continue;
                        float t = Skills.Talent(p, k, sk);
                        if (t > bt) { bt = t; best = k; }
                    }
                    if (best >= 0)
                    {
                        p.Master[best] = p.IdAt(master);
                        _apprenticeOf.Add(((long)p.Uid[best] << 8) | (byte)j);
                        Stats.Apprentices++;
                    }
                }
            }
        }

        // 4) 한 해 연습
        _teachingLetters.Clear();
        Span<bool> practiced = stackalloc bool[(int)Skill.Count];
        for (int i = 0; i < p.HighWater; i++)
        {
            if (!p.IsAliveIndex(i)) continue;
            int age = AgeYears(i);
            if (age < 8) continue;
            int h = p.Household[i];
            if (!hh.IsAlive(h)) continue;
            practiced.Clear();
            var job = (Job)p.JobOf[i];
            if (age >= 16)
            {
                if (!p.Master[i].IsNone) p.Master[i] = PersonId.None;   // 16살이면 도제가 끝난다
                var sk = Skills.SkillOf(job);
                Learn(i, sk, -1, 1f);
                practiced[(int)sk] = true;
                if (job is Job.Trader or Job.Healer && p.GetSkill(i, Skill.Letters) > 0.1f) practiced[(int)Skill.Letters] = true;
            }
            else
            {
                // 아이: 스승 → 집안의 일 가진 어른 → 집안에서 농사 제일 잘하는 어른 순으로 곁에서 배운다
                int teacher = -1;
                Skill sk = Skill.Farming;
                if (!p.Master[i].IsNone && p.IsValid(p.Master[i])) { teacher = p.Master[i].Index; sk = Skills.SkillOf((Job)p.JobOf[teacher]); }
                else
                {
                    float best = -1;
                    for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
                    {
                        if (k == i || AgeYears(k) < 16) continue;
                        var kj = (Job)p.JobOf[k];
                        if (kj == Job.SexWorker) kj = Job.Farmer;
                        float sc = kj != Job.Farmer ? 2f + p.GetSkill(k, Skills.SkillOf(kj)) : p.GetSkill(k, Skill.Farming);
                        if (sc > best) { best = sc; teacher = k; sk = Skills.SkillOf(kj); }
                    }
                }
                Learn(i, sk, teacher, 1f);
                practiced[(int)sk] = true;
                // 글: 집안에 글 아는 어른이 있으면 틈틈이 배운다
                int lt = -1;
                float lv = 0.3f;
                for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
                    if (k != i && p.GetSkill(k, Skill.Letters) > lv) { lv = p.GetSkill(k, Skill.Letters); lt = k; }
                if (lt < 0 && teacher >= 0 && p.GetSkill(teacher, Skill.Letters) > 0.3f) lt = teacher;
                if (lt >= 0) { Learn(i, Skill.Letters, lt, 0.5f); practiced[(int)Skill.Letters] = true; _teachingLetters.Add(lt); }
            }
            for (int s = 0; s < (int)Skill.Count; s++)
            {
                if (practiced[s]) continue;
                // 글은 천천히 잊고, 아이에게 가르치는 어른은 잊지 않는다.
                if (s == (int)Skill.Letters && _teachingLetters.Contains(i)) continue;
                float rust = s == (int)Skill.Letters ? Config.SkillRust / 3f : Config.SkillRust;
                p.SetSkill(i, (Skill)s, p.GetSkill(i, (Skill)s) * (1f - rust));
            }
        }

        // 5) 가구 농사 솜씨
        if (_farmSkill.Length < hh.HighWater) Array.Resize(ref _farmSkill, hh.HighWater * 2);
        for (int h = 0; h < hh.HighWater; h++)
        {
            if (!hh.IsAlive(h)) continue;
            float sum = 0, w = 0;
            for (int k = hh.FirstMember[h]; k >= 0; k = p.HhNext[k])
            {
                float lab = LaborByAge(AgeYears(k));
                if (lab <= 0) continue;
                sum += lab * p.GetSkill(k, Skill.Farming);
                w += lab;
            }
            _farmSkill[h] = w > 0 ? sum / w : 0.3f;
        }
    }

    private void Best(List<int>? list, Skill s, out float skill, out int household)
    {
        skill = 0; household = -1;
        if (list == null) return;
        foreach (int i in list)
        {
            float v = People.GetSkill(i, s);
            if (v > skill) { skill = v; household = People.Household[i]; }
        }
    }

    private float LettersOfBest(List<int>? list)
    {
        if (list == null || list.Count == 0) return 0;
        return People.GetSkill(list[0], Skill.Letters);
    }

    // 도제였던 기록(16살에 스승이 풀리므로, 스승 일과 같은 일을 맡으면 도제 출신으로 센다)
    private readonly HashSet<long> _apprenticeOf = new();
    private readonly HashSet<int> _teachingLetters = new();
    private bool WasApprentice(int i, Job j) => _apprenticeOf.Contains(((long)People.Uid[i] << 8) | (byte)j);

    private bool HouseholdHasJob(int h)
    {
        var p = People;
        for (int k = Households.FirstMember[h]; k >= 0; k = p.HhNext[k]) if (p.JobOf[k] != 0) return true;
        return false;
    }

    /// <summary>
    /// 한 해 연습. 혼자서는 재능이 정한 높이까지 다가가고, 더 잘하는 스승 곁이면 스승 쪽으로 빨리 다가간다.
    /// 스승에게 배우면 재능 높이를 넘어 스승의 90%까지 오를 수 있다(가업의 힘).
    /// </summary>
    private void Learn(int i, Skill s, int teacher, float time)
    {
        var p = People;
        int age = AgeYears(i);
        float talent = Skills.Talent(p, i, s);
        float cap = Skills.Cap(talent);
        float v = p.GetSkill(i, s);
        float ageK = Skills.AgeLearn(age);
        float mult = ageK * (0.5f + talent) * (1f + 0.3f * p.GetSkill(i, Skill.Letters)) * (1.1f - 0.2f * p.Get(i, Trait.Caution)) * time;
        float ts = teacher >= 0 ? p.GetSkill(teacher, s) : -1f;
        p.SetSkill(i, s, Skills.LearnStep(v, cap, mult, ageK * time, ts, Config.LearnRate, Config.TeachRate));
    }
}
