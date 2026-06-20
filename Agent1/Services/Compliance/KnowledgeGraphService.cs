using System.Text;
using Agent1.Config;
using Agent1.Models;

namespace Agent1.Services
{
    // ═══════════════════════════════════════
    // 图数据模型
    // ═══════════════════════════════════════

    public enum EntityType { Chemical, HazardCategory, Regulation, Incident, StorageRule }

    public enum RelationType { ClassifiedAs, References, IncompatibleWith, RequiresDistance, InvolvedIn, RequiresPpe }

    public record GraphEntity(string Id, EntityType Type, Dictionary<string, string> Props)
    {
        public string Label => Props.GetValueOrDefault("name", Id);
    }

    public record GraphRelation(string FromId, string ToId, RelationType Type, string? Detail = null);

    /// <summary>
    /// [P3] 化工安全知识图谱 — 轻量级内存图。
    /// 实体: 化学品/法规/危险类别/事故案例/储存规则
    /// 关系: 分类/参照/禁忌配伍/安全间距/涉及事故/PPE要求
    /// 支持 BFS/DFS 多跳遍历、法规冲突检测、DOT 导出。
    /// </summary>
    public class KnowledgeGraphService
    {
        private readonly Dictionary<string, GraphEntity> _entities = new();
        private readonly List<GraphRelation> _relations = new();
        private readonly IKnowledgeBaseService _kbService;

        public int EntityCount => _entities.Count;
        public int RelationCount => _relations.Count;

        public KnowledgeGraphService(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
        }

        // ═══════════════════════════════════════
        // 构建
        // ═══════════════════════════════════════

        public void BuildFromSubstanceDatabase()
        {
            var substances = ChemicalSubstanceDatabase.GetAll();
            foreach (var sub in substances)
            {
                var chemId = $"chem:{sub.Name}";
                AddEntity(new GraphEntity(chemId, EntityType.Chemical, new()
                {
                    ["name"] = sub.Name,
                    ["cas"] = sub.CasNumber,
                    ["un"] = sub.UnNumber,
                    ["formula"] = sub.Formula ?? "",
                    ["flashPoint"] = sub.FlashPointC?.ToString() ?? "N/A"
                }));

                foreach (var hc in sub.HazardCategories)
                {
                    var catId = $"cat:{hc.Category}";
                    AddEntity(new GraphEntity(catId, EntityType.HazardCategory, new()
                    {
                        ["name"] = hc.Category,
                        ["gbStandard"] = hc.GbStandard
                    }));
                    AddRelation(new GraphRelation(chemId, catId, RelationType.ClassifiedAs));

                    if (!string.IsNullOrWhiteSpace(hc.GbStandard))
                    {
                        var regId = $"reg:{hc.GbStandard}";
                        AddEntity(new GraphEntity(regId, EntityType.Regulation, new()
                        {
                            ["name"] = hc.GbStandard
                        }));
                        AddRelation(new GraphRelation(catId, regId, RelationType.References));
                    }
                }

                // 储存不兼容关系
                if (sub.IncompatibleWith?.Count > 0)
                {
                    foreach (var inc in sub.IncompatibleWith)
                    {
                        var incId = $"cat:{inc}";
                        AddEntity(new GraphEntity(incId, EntityType.HazardCategory, new()
                        {
                            ["name"] = inc
                        }));
                        AddRelation(new GraphRelation(chemId, incId, RelationType.IncompatibleWith));
                    }
                }
            }

            // 内置事故案例
            BuildIncidentGraph();

            Console.WriteLine($"   📊 知识图谱构建完成: {_entities.Count} 实体, {_relations.Count} 关系");
        }

        private void BuildIncidentGraph()
        {
            var incidents = new[]
            {
                ("2015天津港爆炸", "硝酸铵", "氧化性固体", "165人死亡, 798人受伤"),
                ("2019江苏响水爆炸", "苯", "易燃液体", "78人死亡, 617人受伤"),
                ("2020黎巴嫩贝鲁特", "硝酸铵", "氧化性固体", "220人死亡, 7000+受伤"),
            };

            foreach (var (name, chemical, category, desc) in incidents)
            {
                var incId = $"incident:{name}";
                AddEntity(new GraphEntity(incId, EntityType.Incident, new()
                {
                    ["name"] = name,
                    ["description"] = desc
                }));

                var chemId = $"chem:{chemical}";
                if (_entities.ContainsKey(chemId))
                    AddRelation(new GraphRelation(incId, chemId, RelationType.InvolvedIn, desc));
            }
        }

        // ═══════════════════════════════════════
        // 查询
        // ═══════════════════════════════════════

        public List<GraphEntity> Traverse(string startName, int maxHops = 2)
        {
            var startId = FindEntityId(startName);
            if (startId == null) return new List<GraphEntity>();

            var visited = new HashSet<string> { startId };
            var queue = new Queue<(string id, int depth)>();
            queue.Enqueue((startId, 0));
            var result = new List<GraphEntity> { _entities[startId] };

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                if (depth >= maxHops) continue;

                var neighbors = _relations
                    .Where(r => r.FromId == current || r.ToId == current)
                    .Select(r => r.FromId == current ? r.ToId : r.FromId)
                    .Distinct();

                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor) && _entities.ContainsKey(neighbor))
                    {
                        result.Add(_entities[neighbor]);
                        queue.Enqueue((neighbor, depth + 1));
                    }
                }
            }

            return result;
        }

        public List<GraphRelation> FindRelatedRegulations(string chemicalName)
        {
            var chemId = FindEntityId(chemicalName);
            if (chemId == null) return new List<GraphRelation>();

            var visited = new HashSet<string> { chemId };
            var queue = new Queue<string>(new[] { chemId });
            var regRelations = new List<GraphRelation>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var r in _relations.Where(r => r.FromId == current || r.ToId == current))
                {
                    var other = r.FromId == current ? r.ToId : r.FromId;
                    if (visited.Add(other))
                    {
                        if (_entities[other].Type == EntityType.Regulation)
                            regRelations.Add(r);
                        queue.Enqueue(other);
                    }
                }
            }
            return regRelations;
        }

        public List<string> FindConflictRegulations(string chemicalName)
        {
            // 查所有关联法规，检测是否有同一场景不同要求
            var regRelations = FindRelatedRegulations(chemicalName);
            var conflicts = new List<string>();

            // 简化冲突检测：同场景 (如储存) 有多个 GB 引用
            var storageRegs = regRelations
                .Where(r => r.Detail?.Contains("储存") == true || r.Type == RelationType.References)
                .GroupBy(r => r.ToId)
                .Where(g => g.Count() > 1);

            foreach (var group in storageRegs)
                conflicts.Add($"法规 {group.Key} 与 {string.Join(", ", group.Skip(1).Select(r => r.ToId))} 可能对同一场景有冲突要求");

            return conflicts;
        }

        public List<GraphRelation> FindIncidentsByChemical(string chemicalName)
        {
            var chemId = FindEntityId(chemicalName);
            if (chemId == null) return new List<GraphRelation>();

            return _relations
                .Where(r => r.ToId == chemId && r.Type == RelationType.InvolvedIn)
                .ToList();
        }

        // ═══════════════════════════════════════
        // RAG 增强查询
        // ═══════════════════════════════════════

        public async Task<string> QueryAsync(string query)
        {
            // 1. 图遍历
            var entities = Traverse(query, maxHops: 3);
            var regs = FindRelatedRegulations(query);
            var incidents = FindIncidentsByChemical(query);
            var conflicts = FindConflictRegulations(query);

            // 2. RAG 补充
            string ragSupplement = "";
            try
            {
                var chunks = await _kbService.RetrieveChemicalRegulationAsync(query, topK: 2);
                if (chunks.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("\n【RAG 知识库补充】");
                    foreach (var c in chunks.Take(2))
                    {
                        var content = c.Content ?? "";
                        if (content.Length > 250) content = content[..250] + "...";
                        sb.AppendLine($"  · {content}");
                    }
                    ragSupplement = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ RAG补充检索失败: {ex.Message}");
            }

            // 3. 格式化输出
            var result = new StringBuilder();
            result.AppendLine($"\n══════════ 知识图谱查询: {query} ══════════");
            result.AppendLine($"  实体: {entities.Count} | 关系: {regs.Count} | 事故: {incidents.Count}");

            result.AppendLine("\n━━━ 关联实体 (3跳遍历) ━━━");
            foreach (var e in entities.Take(15))
            {
                var extra = e.Type == EntityType.Chemical
                    ? $"CAS:{e.Props.GetValueOrDefault("cas","?")}"
                    : e.Type == EntityType.Regulation ? e.Props.GetValueOrDefault("name","")
                    : "";
                result.AppendLine($"  [{e.Type}] {e.Label} {extra}");
            }

            result.AppendLine("\n━━━ 引用法规 ━━━");
            foreach (var r in regs.Take(10))
            {
                var reg = _entities.GetValueOrDefault(r.ToId);
                result.AppendLine($"  {reg?.Label ?? r.ToId} ({r.Type})");
            }

            if (incidents.Count > 0)
            {
                result.AppendLine("\n━━━ 历史事故 ━━━");
                foreach (var inc in incidents)
                {
                    var entity = _entities.GetValueOrDefault(inc.FromId);
                    result.AppendLine($"  ⚠️ {entity?.Label ?? inc.FromId}: {inc.Detail}");
                }
            }

            if (conflicts.Count > 0)
            {
                result.AppendLine("\n━━━ 法规冲突检测 ━━━");
                foreach (var c in conflicts)
                    result.AppendLine($"  ⚠️ {c}");
            }

            result.Append(ragSupplement);
            result.AppendLine("\n═══════════════════════════════════");

            return result.ToString();
        }

        // ═══════════════════════════════════════
        // 导出
        // ═══════════════════════════════════════

        public string ExportDOT()
        {
            var sb = new StringBuilder();
            sb.AppendLine("digraph ChemicalSafetyKG {");
            sb.AppendLine("  rankdir=LR; node [shape=box, style=filled];");

            foreach (var e in _entities.Values)
            {
                var color = e.Type switch
                {
                    EntityType.Chemical => "lightyellow",
                    EntityType.HazardCategory => "lightcoral",
                    EntityType.Regulation => "lightblue",
                    EntityType.Incident => "lightsalmon",
                    _ => "white"
                };
                sb.AppendLine($"  \"{e.Id}\" [label=\"{e.Label}\", fillcolor={color}];");
            }

            foreach (var r in _relations)
            {
                var style = r.Type == RelationType.IncompatibleWith ? "color=red, style=bold" : "";
                sb.AppendLine($"  \"{r.FromId}\" -> \"{r.ToId}\" [label=\"{r.Type}\" {style}];");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ═══════════════════════════════════════
        // 内部辅助
        // ═══════════════════════════════════════

        private void AddEntity(GraphEntity entity)
        {
            _entities.TryAdd(entity.Id, entity);
        }

        private void AddRelation(GraphRelation relation)
        {
            _relations.Add(relation);
        }

        private string? FindEntityId(string name)
        {
            return _entities.Keys.FirstOrDefault(k =>
                _entities[k].Label.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                k.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 图构建工厂 — 从 ChemicalSubstanceDatabase 自动构建知识图谱
    /// </summary>
    public static class KnowledgeGraphFactory
    {
        private static KnowledgeGraphService? _instance;

        public static KnowledgeGraphService GetOrBuild(IKnowledgeBaseService kb)
        {
            if (_instance != null) return _instance;

            var graph = new KnowledgeGraphService(kb);
            graph.BuildFromSubstanceDatabase();
            _instance = graph;
            return _instance;
        }

        public static void Reset() => _instance = null;
    }
}
