namespace People.Sim;

/// <summary>자리 잡은 세계를 만들 때 고르는 것.</summary>
public sealed class GenOptions
{
    public int Rulers = 4;              // 군주(꼭대기 세력) 수
    public int VillagesPerRuler = 7;    // 군주 하나에 딸린 마을 수(어림)
}

/// <summary>
/// P8 자리 잡은 세계 생성기(plan_power 8장, spec 34절).
/// 1. 마을 자리: 시작 마을에서 한 걸음씩(70~140칸) 퍼져 마을을 세운다.
/// 2. 가문: 마을마다 가문 몇을 120년 전 시조 부부부터 다섯 대로 내린다. 아들과 딸은 마을 안 다른 가문과 혼인한다.
///    지금 나이에 따라 살아 있는지 정하고, 죽은 조상은 족보에만 남는다(촌수와 혈연 계산이 된다).
/// 3. 집: 산 부부마다 한 집. 홀어미는 혼인한 아들 집에, 혼인 안 한 자식은 부모 집에 산다.
/// 4. 땅: 지배자와 넉넉한 집이 많이, 가난한 집은 거의 못 잡는다(소작과 품팔이가 생긴다).
/// 5. 세력: 군주를 몇 정해 마을을 나누고, 마을마다 영주, 몇 마을은 대영주. 마을 집은 그 마을 영주를 섬긴다.
///    지배자는 군인과 징수관을 거느리고 성과 저택에 산다. 칭호는 물려받음, 상속은 맏이.
/// 생성 뒤 몇 해를 돌려 관계, 비밀, 일자리, 조직이 저절로 생기게 한다(부르는 쪽이 돌린다).
/// </summary>
public sealed partial class World
{
    private sealed class GenPerson
    {
        public int Uid, Birth, Death = -1, Clan, Gen, Father = -1, Mother = -1, Spouse = -1, LastChild = int.MinValue;
        public bool Male, Alive;
        public int Index = -1;   // 산 사람의 사람 번호
    }

    /// <summary>자리 잡은 세계를 만든다. Day 0 에서 한 번만 부른다.</summary>
    public void GenerateSettled(GenOptions o)
    {
        var hh = Households; var p = People;
        int dpy = Config.DaysPerYear;
        // ---------------------------------------------------------------- 1. 마을 자리
        var vills = new List<int> { FoundVillage(SpawnX, SpawnY, -1) };
        int want = Math.Max(2, o.Rulers * o.VillagesPerRuler);
        for (int guard = 0; vills.Count < want && guard < want * 40; guard++)
        {
            int from = vills[_rng.NextInt(vills.Count)];
            var vl = VillageList;
            float best = float.MinValue; (int x, int y) site = (-1, -1);
            for (int a = 0; a < 16; a++)
            {
                float r = 70f + 70f * _rng.NextFloat(), ang = _rng.NextFloat() * MathF.PI * 2f;
                int x = vl.CenterX[from] + (int)(MathF.Cos(ang) * r), y = vl.CenterY[from] + (int)(MathF.Sin(ang) * r);
                if (!Map.InBounds(x, y) || NearestVillageDist(x, y) < 64) continue;
                float sc = Map.VillageSiteScore(x, y, Config.FarmRadius);
                if (sc > best) { best = sc; site = (x, y); }
            }
            if (site.x < 0 || best == float.MinValue) continue;
            vills.Add(FoundVillage(site.x, site.y, from));
        }
        // ---------------------------------------------------------------- 2. 세력 나누기(마을 단위)
        var cap = new List<int> { vills[0] };
        while (cap.Count < Math.Min(o.Rulers, vills.Count))
            cap.Add(vills.Where(v => !cap.Contains(v)).OrderByDescending(v => cap.Min(c => VDist2(v, c))).First());
        var realmOf = vills.ToDictionary(v => v, v => cap.OrderBy(c => VDist2(v, c)).First());
        var seatOf = new Dictionary<int, int>();   // 마을 → 그 마을이 바로 섬기는 마을(군주 마을은 -1)
        foreach (int c in cap)
        {
            var mine = vills.Where(v => realmOf[v] == c && v != c).ToList();
            var greats = new List<int>();
            int g = mine.Count / 3;
            while (greats.Count < g)
                greats.Add(mine.Where(v => !greats.Contains(v)).OrderByDescending(v => greats.Append(c).Min(s => VDist2(v, s))).First());
            seatOf[c] = -1;
            foreach (int v in greats) seatOf[v] = c;
            foreach (int v in mine.Where(v => !greats.Contains(v))) seatOf[v] = greats.Append(c).OrderBy(s => VDist2(v, s)).First();
        }
        int RankOf(int v) => seatOf[v] < 0 ? 3 : vills.Any(u => seatOf.GetValueOrDefault(u, -2) == v) ? 2 : 1;   // 3 군주, 2 대영주, 1 영주
        // ---------------------------------------------------------------- 3. 마을마다 가문과 집
        var lordOfVillage = new Dictionary<int, int>();
        foreach (int v in vills)
        {
            int houses = Math.Clamp(FreeInVillage(v) / 9, 16, 45);
            int lord = PopulateVillage(v, houses, RankOf(v));
            if (lord >= 0) lordOfVillage[v] = lord;
        }
        // 남은 땅은 영주의 직영지다(모든 마을을 채운 뒤, 마을 집 수만큼 한도). 땅 없는 집은 영주에게 빌려 부친다
        _freeCache.Clear();
        RecountHouseholds();
        foreach (var (v, lord) in lordOfVillage)
        {
            float need = 0f; int have = 0;
            for (int h = 0; h < hh.HighWater; h++)
                if (hh.IsAlive(h) && hh.Village[h] == v) { need += hh.Labor[h] * Config.PlotsPerWorker * Config.LandReserve; have += hh.PlotCount[h]; }
            ClaimFields(lord, Math.Min(FreeInVillage(v), Math.Max(0, (int)(1.5f * need) - have)));
            _enclosed.Add(v);
        }
        // ---------------------------------------------------------------- 4. 섬김
        EnsurePowerArrays(); EnsureRealmArrays();
        foreach (int v in vills)
        {
            if (!lordOfVillage.TryGetValue(v, out int lord)) continue;
            for (int h = 0; h < hh.HighWater; h++)
                if (hh.IsAlive(h) && h != lord && hh.Village[h] == v) { _patron[h] = lord; _patronFounded[h] = hh.FoundedDay[lord]; Bind(h); }
            int seat = seatOf[v];
            if (seat >= 0 && lordOfVillage.TryGetValue(seat, out int liege)) { _patron[lord] = liege; _patronFounded[lord] = hh.FoundedDay[liege]; Bind(lord); }
            _legit[lord] = (byte)Legitimacy.Inherited;
            if (p.IsValid(hh.Head[lord])) Lineage.SetPolicy(Lineage.FamilyOf(p.Uid[hh.Head[lord].Index]), InheritPolicy.Primogeniture);
        }
        RecountClients();
        // ---------------------------------------------------------------- 5. 군인, 징수관, 성, 충성
        foreach (var (v, lord) in lordOfVillage)
        {
            int rank = RankOf(v);
            hh.Food[lord] += rank switch { 3 => 4000f, 2 => 1500f, _ => 500f };
            int soldiers = rank switch { 3 => 12, 2 => 6, _ => 3 };
            int collectors = rank >= 2 ? rank - 1 : 0;
            var pool = new List<int>();
            for (int i = 0; i < p.HighWater; i++)
            {
                if (!p.IsAliveIndex(i) || !p.Male[i] || AgeYears(i) < 18 || AgeYears(i) > 40 || p.JobOf[i] != 0) continue;
                int h = p.Household[i];
                if (h == lord || PatronOf(h) != lord || hh.Head[h].Index == i) continue;
                pool.Add(i);
            }
            pool = pool.OrderByDescending(i => FightPower(i)).ToList();
            foreach (int i in pool.Take(soldiers)) Employ(i, lord, Job.Soldier);
            foreach (int i in pool.Skip(soldiers).OrderByDescending(i => p.GetSkill(i, Skill.Letters)).Take(collectors)) Employ(i, lord, Job.Collector);
            // 거처: 군주는 성, 대영주는 망루 달린 저택, 영주는 저택
            int level = rank;
            if (_residence.Length < hh.HighWater) Array.Resize(ref _residence, Math.Max(hh.HighWater, _residence.Length * 2));
            _residence[lord] = (byte)level;
            hh.Capacity[lord] = Math.Max(hh.Capacity[lord], level switch { 1 => 12, 2 => 16, _ => 24 });
            ClaimFootprint(lord, ResidenceRadius(level));
            // 따르는 집 가장은 지배자를 좋게 본다
            int lh = HeadIdx(lord);
            if (lh < 0) continue;
            for (int h = 0; h < hh.HighWater; h++)
                if (hh.IsAlive(h) && PatronOf(h) == lord && HeadIdx(h) is int x && x >= 0) Social.Add(p, x, p.Uid[lh], 25f + 20f * _rng.NextFloat());
        }
        RecountClients();
        RecountTree();
        RecountHouseholds();
    }

    private float VDist2(int a, int b)
    {
        var vl = VillageList;
        float dx = vl.CenterX[a] - vl.CenterX[b], dy = vl.CenterY[a] - vl.CenterY[b];
        return dx * dx + dy * dy;
    }

    private void Employ(int i, int lord, Job j)
    {
        People.JobOf[i] = (byte)j;
        Lineage.SetJob(People.Uid[i], j);
        _employer[i] = lord;
        _employerVer++;
    }

    /// <summary>
    /// 마을 v 에 가문 몇을 다섯 대로 내리고 산 사람으로 집을 채운다. 지배자 집(가장 큰 가문의 맏집) 번호를 돌려준다.
    /// rank 가 클수록(군주 마을) 지배자 집이 넉넉하다.
    /// </summary>
    private int PopulateVillage(int v, int houses, int rank)
    {
        var p = People; var hh = Households;
        int dpy = Config.DaysPerYear, now = Day;
        int Years(int y) => y * dpy + _rng.NextInt(dpy);
        int clans = Math.Max(3, houses / 5);
        var all = new List<GenPerson>();
        var couples = new List<(GenPerson man, GenPerson woman)>();
        // 부모 모르는 사람(시조, 바깥 신부)의 성: 시조는 새 성(목록이 다 차면 다른 마을의 같은 성), 신부는 이미 있는 성
        int OldFamily() => Lineage.Families > 0 ? _rng.NextInt(Lineage.Families) : -1;
        GenPerson Add(bool male, int birth, GenPerson? f, GenPerson? m, int clan, int gen)
        {
            var x = new GenPerson { Male = male, Birth = birth, Clan = clan, Gen = gen, Father = f?.Uid ?? -1, Mother = m?.Uid ?? -1 };
            if (f == null && m == null)
            {
                bool founder = clan >= 0;
                bool fresh = founder && Lineage.Families < Names.SurnameCount;
                x.Uid = fresh || OldFamily() < 0 ? Lineage.Add(male, birth, -1, -1) : Lineage.AddInto(male, birth, OldFamily());
            }
            else x.Uid = Lineage.Add(male, birth, x.Father, x.Mother);
            if (m != null) m.LastChild = Math.Max(m.LastChild, birth);
            if (f != null) f.LastChild = Math.Max(f.LastChild, birth);
            all.Add(x);
            return x;
        }
        // 시조 부부(120년쯤 전)
        for (int c = 0; c < clans; c++)
        {
            int b = now - Years(110 + _rng.NextInt(15));
            var man = Add(true, b, null, null, c, 0);
            var wife = Add(false, b + Years(2 + _rng.NextInt(4)), null, null, -1, 0);
            Lineage.SetPolicy(Lineage.FamilyOf(man.Uid), _rng.NextBool() ? InheritPolicy.Partible : InheritPolicy.Primogeniture);
            man.Spouse = wife.Uid; wife.Spouse = man.Uid;
            couples.Add((man, wife));
        }
        // 대를 내린다: 부부마다 자란 자식 2~5(지금 이전에 태어난 아이만), 같은 대끼리 다른 가문과 혼인
        var gen = couples;
        for (int g = 1; g <= 5 && gen.Count > 0; g++)
        {
            var kids = new List<GenPerson>();
            foreach (var (man, woman) in gen)
            {
                int n = 2 + _rng.NextInt(4);
                int t = woman.Birth + Years(18 + _rng.NextInt(6));
                for (int k = 0; k < n; k++)
                {
                    if (t > now) break;
                    kids.Add(Add(_rng.NextBool(), t, man, woman, man.Clan, g));
                    t += Years(2) + _rng.NextInt(dpy * 2);
                }
            }
            // 혼인: 열여덟 넘은 아들은 다른 가문 딸과(나이 차 8년 안), 짝이 없으면 바깥 여자와
            var next = new List<(GenPerson, GenPerson)>();
            var girls = kids.Where(k => !k.Male && now - k.Birth >= 16 * dpy).OrderBy(_ => _rng.NextFloat()).ToList();
            foreach (var son in kids.Where(k => k.Male && now - k.Birth >= 18 * dpy).OrderBy(_ => _rng.NextFloat()))
            {
                if (!_rng.Chance(0.88f)) continue;
                var bride = girls.FirstOrDefault(w => w.Clan != son.Clan && Math.Abs(w.Birth - son.Birth - 3 * dpy) < 8 * dpy);
                if (bride != null) girls.Remove(bride);
                else bride = Add(false, son.Birth + Years(1 + _rng.NextInt(5)), null, null, -1, g);
                son.Spouse = bride.Uid; bride.Spouse = son.Uid;
                next.Add((son, bride));
            }
            gen = next;
        }
        // 지금 살아 있나: 나이가 많을수록 죽었다. 죽은 날은 마지막 아이가 태어난 뒤
        var byUid = all.ToDictionary(x => x.Uid);
        foreach (var x in all)
        {
            float age = (now - x.Birth) / (float)dpy;
            float alive = age < 45 ? 0.96f : age < 55 ? 0.85f : age < 65 ? 0.6f : age < 75 ? 0.3f : age < 85 ? 0.08f : 0f;
            x.Alive = _rng.Chance(alive);
            if (x.Alive) continue;
            int lo = Math.Max(x.Birth + 16 * dpy, x.LastChild + 1), hi = Math.Min(now - 1, x.Birth + 85 * dpy);
            if (age < 16) lo = x.Birth + 1;
            x.Death = hi > lo ? lo + _rng.NextInt(hi - lo) : Math.Max(x.Birth + 1, Math.Min(now - 1, lo));
            Lineage.MarkDeath(x.Uid, x.Death);
        }
        // 집을 정한다: 산 혼인한 남자(18 넘음)의 집, 홀어미는 혼인한 아들 집이나 제 집, 혼인 안 한 자식은 부모 집
        var home = new Dictionary<int, int>();   // 산 사람 uid → 집 열쇠(그 집 가장 uid)
        foreach (var x in all.Where(x => x.Alive && x.Male && x.Spouse >= 0 && now - x.Birth >= 18 * dpy)) home[x.Uid] = x.Uid;
        int KeyOf(GenPerson x)
        {
            if (home.TryGetValue(x.Uid, out int k)) return k;
            if (!x.Male && x.Spouse >= 0 && byUid.TryGetValue(x.Spouse, out var hus) && hus.Alive && home.ContainsKey(hus.Uid)) return hus.Uid;
            // 홀어미: 혼인한 아들 가운데 맏이의 집
            if (!x.Male && x.Spouse >= 0)
            {
                var son = all.Where(s => s.Alive && s.Male && s.Mother == x.Uid && home.ContainsKey(s.Uid)).OrderBy(s => s.Birth).FirstOrDefault();
                if (son != null) return son.Uid;
                return x.Uid;   // 제 집(가장)
            }
            // 혼인 안 한 사람: 아버지 집, 없으면 어머니 집, 없으면 맏형 집, 다 없으면 제 집
            if (x.Father >= 0 && byUid[x.Father].Alive && home.ContainsKey(x.Father)) return x.Father;
            if (x.Mother >= 0 && byUid[x.Mother].Alive) return KeyOf(byUid[x.Mother]);
            var bro = all.Where(s => s.Alive && s.Male && s.Father == x.Father && x.Father >= 0 && home.ContainsKey(s.Uid)).OrderBy(s => s.Birth).FirstOrDefault();
            if (bro != null) return bro.Uid;
            return x.Uid;
        }
        // 부모도 형도 없는 아이는 같은 가문의 어른 집(없으면 아무 집)에서 자란다
        var keyed = all.Where(x => x.Alive).ToDictionary(x => x.Uid, KeyOf);
        foreach (var x in all.Where(x => x.Alive && keyed[x.Uid] == x.Uid && now - x.Birth < 16 * dpy))
        {
            var heads = home.Keys.Where(k => byUid[k].Alive).ToList();
            if (heads.Count == 0) continue;
            int pick = heads.Where(k => byUid[k].Clan == x.Clan).DefaultIfEmpty(heads[_rng.NextInt(heads.Count)]).First();
            keyed[x.Uid] = pick;
        }
        var groups = all.Where(x => x.Alive).GroupBy(x => keyed[x.Uid]).ToList();
        // 가장 큰 가문의 맏집이 지배자 집이다
        var clanSize = groups.GroupBy(gr => byUid[gr.Key].Clan).ToDictionary(c => c.Key, c => c.Sum(gr => gr.Count()));
        int lordClan = clanSize.Where(c => c.Key >= 0).OrderByDescending(c => c.Value).Select(c => c.Key).DefaultIfEmpty(-1).First();
        var lordKey = groups.Where(gr => byUid[gr.Key].Clan == lordClan && byUid[gr.Key].Male).OrderBy(gr => byUid[gr.Key].Birth).Select(gr => gr.Key).DefaultIfEmpty(-1).First();
        int lordHouse = -1;
        var made = new List<int>();
        // 지배자 집을 먼저 세운다(성터를 먼저 잡게)
        foreach (var gr in groups.OrderByDescending(gr => gr.Key == lordKey))
        {
            var (x, y) = HouseSpotOnRoad(v);
            if (x < 0) continue;
            int size = gr.Count();
            int h = NewHousehold(x, y, 8f * size * Config.DaysPerSeason, Math.Max(Config.HutCapacity, size + 1), v);
            made.Add(h);
            if (gr.Key == lordKey) lordHouse = h;
            foreach (var m in gr.OrderBy(m => m.Birth))
            {
                var fid = m.Father >= 0 && byUid[m.Father].Index >= 0 ? p.IdAt(byUid[m.Father].Index) : PersonId.None;
                var mid = m.Mother >= 0 && byUid[m.Mother].Index >= 0 ? p.IdAt(byUid[m.Mother].Index) : PersonId.None;
                var id = p.Add(m.Male, m.Birth, fid, mid, h);
                Join(id.Index, h);
                p.Uid[id.Index] = m.Uid;
                _uidIndex[m.Uid] = id.Index;
                m.Index = id.Index;
                Genetics.Randomize(p.Genome(id.Index), m.Male, ref _rng);
                p.ComputePhenotype(id.Index);
                int age = (now - m.Birth) / dpy;
                SeedSkills(id.Index, age);
                p.Piety[id.Index] = 0.2f + 0.6f * _rng.NextFloat();
                if (age >= 16) p.GrownUp[id.Index] = true;
            }
            var head = byUid[gr.Key];
            hh.Head[h] = p.IdAt(head.Index >= 0 ? head.Index : gr.First().Index);
        }
        // 혼인과 식구 사이의 정
        foreach (var x in all.Where(x => x.Alive && x.Male && x.Spouse >= 0 && x.Index >= 0))
        {
            var w = byUid[x.Spouse];
            if (!w.Alive || w.Index < 0 || p.Household[w.Index] != p.Household[x.Index]) continue;
            Marry(x.Index, w.Index, 0.5f);
        }
        foreach (var x in all.Where(x => x.Alive && x.Index >= 0))
            foreach (int par in new[] { x.Father, x.Mother })
                if (par >= 0 && byUid[par].Alive && byUid[par].Index >= 0)
                {
                    Social.Add(p, x.Index, par, 30f + 20f * _rng.NextFloat());
                    Social.Add(p, byUid[par].Index, x.Uid, 40f + 20f * _rng.NextFloat());
                }
        // 땅: 지배자 집이 먼저 넉넉히, 그다음 넉넉한 집, 나머지, 가장 가난한 넷에 하나는 거의 못 잡는다
        RecountHouseholds();
        // 영주가 먼저 좋은 땅을 직영지로 잡는다(마을이 먹고살 땅의 30%). 나머지 집은 그다음, 모자라면 영주에게 빌린다
        if (lordHouse >= 0)
        {
            float need = made.Sum(h => hh.Labor[h] * Config.PlotsPerWorker * Config.LandReserve);
            ClaimFields(lordHouse, (int)(0.3f * need));
        }
        var order = made.Where(h => h != lordHouse).OrderBy(_ => _rng.NextFloat()).ToList();
        for (int k = 0; k < order.Count; k++)
        {
            int h = order[k];
            float lab = MathF.Max(1f, hh.Labor[h]);
            // 넉넉한 집은 먹고살 몫의 1.6배(남는 땅은 빌려준다), 보통 집은 제 몫만큼, 가난한 넷에 하나는 거의 없이(빌리거나 품을 판다)
            float f = k < order.Count / 6 ? 1.6f : k >= order.Count * 3 / 4 ? 0.15f : 1.0f;
            ClaimFields(h, (int)(lab * Config.PlotsPerWorker * Config.LandReserve * f));
            if (k < order.Count / 6) hh.Food[h] *= 2f;
        }
        // 남은 땅은 영주의 직영지다. 땅 없는 집은 영주에게 빌려 부친다
        return lordHouse;
    }
}
