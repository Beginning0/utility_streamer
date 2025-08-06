using HtmlAgilityPack;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net;

// --- ESTRUCTURAS DE DATOS PARA NUESTRA BASE DE DATOS ---
public class MonsterData
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int BaseHP { get; set; }
    public string RoarAudioUrl { get; set; } = "";
    public List<StatDetail> LimpingThresholds { get; set; } = new();
    public List<StatDetail> CaptureThresholds { get; set; } = new();
    public Dictionary<string, float> EnragedStats { get; set; } = new();
    public List<HabitatInfo> Habitats { get; set; } = new();
    public List<WeaknessInfo> Weaknesses { get; set; } = new();
    public List<ItemDropInfo> ItemDrops { get; set; } = new();
    public List<AbnormalStatusInfo> AbnormalStatuses { get; set; } = new();
    public List<StatDetail> HPRanges { get; set; } = new(); // Para "Low", "High", "G"
    public List<StatDetail> StaggerLimits { get; set; } = new(); // Límites de Aturdimiento
    public List<ArmorInfo> Armors { get; set; } = new();         // ¡NUEVO! Para armaduras
    public List<QuestInfo> Quests { get; set; } = new();         // ¡NUEVO! Para misiones
    public string GameKey { get; set; } = "";
}

// ¡NUEVA ESTRUCTURA DE DATOS! Para guardar el resultado del descubrimiento

// --- AÑADE ESTAS NUEVAS CLASES ---

public class ArmorInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("type")] // ej: "Yelmo", "Coraza", "Brazales"
    public string Type { get; set; } = "";

    [JsonProperty("defense")]
    public int Defense { get; set; }

    [JsonProperty("resistances")]
    public Dictionary<string, int> Resistances { get; set; } = new(); // ej: {"fire": 3, "water": -1}

    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = new(); // ej: ["Ataque+2", "Afinidad+1"]
}

public class QuestInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("type")] // ej: "Aldea", "Gremio", "Evento"
    public string Type { get; set; } = "";

    [JsonProperty("rank")] // ej: "Rango Bajo", "Rango G", "6 Estrellas"
    public string Rank { get; set; } = "";

    [JsonProperty("objective")]
    public string Objective { get; set; } = "";
}
public class MonsterInfo
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public class StatDetail
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ItemData
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = ""; // ¡NUEVO!
    public List<string> Sources { get; set; } = new();
    public List<string> Usages { get; set; } = new();
}
public class AbnormalStatusInfo // ¡NUEVA CLASE!
{
    public string StatusName { get; set; } = "";
    public string Initial { get; set; } = "";
    public string Increase { get; set; } = "";
    public string Max { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Damage { get; set; } = "";
    public string Reduction { get; set; } = "";
}
public class HabitatInfo
{
    [JsonProperty("map")]
    public string Map { get; set; } = "";

    [JsonProperty("startArea")]
    public string StartArea { get; set; } = "";

    [JsonProperty("areas")]
    public string Areas { get; set; } = "";

    [JsonProperty("restArea")] // ¡La más importante para saber dónde duerme!
    public string RestArea { get; set; } = "";
}
public class WeaknessInfo
{
    public string BodyPart { get; set; } = "";
    public Dictionary<string, int> ElementalValues { get; set; } = new();
}

public class ItemDropInfo
{
    public string Rank { get; set; } = "";
    public string Source { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Percentage { get; set; } = "";
}



public class KnowledgeService
{
    private readonly string _cacheFilePath = "Biblioteca_cache.json"; 
    // private Dictionary<string, Dictionary<string, MonsterData>> _knowledgeCache = new();
    public Dictionary<string, Dictionary<string, MonsterData>> _knowledgeCache { get; private set; } = new();

    // El servicio ahora tiene una única instancia del agente web
    private readonly WebAgentService _webAgent;


    // Los diccionarios se declaran aquí
    private readonly Dictionary<string, IKnowledgeExtractor> _extractors;
    private readonly Dictionary<string, string> _gameBaseUrls;
    // Hacemos que sean legibles desde fuera de forma segura
    public IReadOnlyDictionary<string, IKnowledgeExtractor> Extractors => _extractors;
    public IReadOnlyDictionary<string, string> GameBaseUrls => _gameBaseUrls;

    // --- CONSTRUCTOR CORREGIDO Y SIMPLIFICADO ---
    public KnowledgeService(OllamaService ollamaService)
    {
        _webAgent = new WebAgentService(ollamaService);

        LoadCache();

        // ¡Aquí está la magia! Rellenamos los diccionarios manualmente.
        // Esto es explícito, fácil de entender y maneja las dependencias correctamente.
        _extractors = new Dictionary<string, IKnowledgeExtractor>
        {
            { "mh4u", new LegacyKiranicoExtractor() }, // Este no necesita el agente
            { "mh3u", new LegacyKiranicoExtractor() }, // Este tampoco
            { "mhgu", new MhguKiranicoExtractor(_webAgent) }  // ¡Y a este le pasamos el agente que necesita!
        };

        _gameBaseUrls = new Dictionary<string, string>
        {
            { "mh4u", "https://kiranico.com/en/mh4u" },
            { "mh3u", "https://kiranico.com/en/mh3u" },
            { "mhgu", "https://mhgu.kiranico.com" }
        };
    }

    private void LoadCache()
    {
        if (File.Exists(_cacheFilePath))
        {
            var json = File.ReadAllText(_cacheFilePath);
            _knowledgeCache = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, MonsterData>>>(json) ?? new();
        }
        Console.WriteLine($"[Knowledge]: Caché cargado con {_knowledgeCache.Values.Sum(dict => dict.Count)} monstruos.");
    }

    private void SaveCache()
    {
        var json = JsonConvert.SerializeObject(_knowledgeCache, Formatting.Indented);
        File.WriteAllText(_cacheFilePath, json);
    }

    public async Task<MonsterData?> GetOrLearnMonsterAsync(string game, string monsterName)
    {
        string gameKey = game.ToLower();
        string monsterKey = monsterName.ToLower();

        if (_knowledgeCache.TryGetValue(gameKey, out var monsters) && monsters.TryGetValue(monsterKey, out var cachedData))
        {
            // Verificamos si los datos esenciales están presentes. Si no, volvemos a aprender.
            if (!string.IsNullOrEmpty(cachedData.Description) && cachedData.Weaknesses.Any() && cachedData.ItemDrops.Any())
            {
                Console.WriteLine($"[Knowledge]: '{monsterName}' encontrado en caché con datos completos.");
                return cachedData;
            }
            Console.WriteLine($"[Knowledge]: Registro de '{monsterName}' incompleto. Aprendiendo de nuevo...");
        }

        // Si no está en caché o está incompleto, aprendemos desde la web.
        return await ScrapeAndLearnMonsterAsync(gameKey, monsterName);
    }


    public async Task<List<MonsterInfo>> DiscoverMonstersForGameAsync(string game)
    {
        if (!_extractors.TryGetValue(game, out var extractor) || !_gameBaseUrls.TryGetValue(game, out var baseUrl))
        {
            Console.WriteLine($"[Discovery Protocol]: No hay un extractor o URL base definido para '{game}'.");
            return new List<MonsterInfo>();
        }

        string monsterListUrl = $"{baseUrl}/monster";
        Console.WriteLine($"[Discovery Protocol]: Accediendo a {monsterListUrl} con Playwright...");

        try
        {
            // Usamos Playwright para obtener el HTML completo
            string? html = await GetHtmlWithPlaywright(monsterListUrl);
            if (string.IsNullOrEmpty(html))
            {
                Console.WriteLine($"[Discovery Protocol Error]: No se pudo obtener el contenido HTML de la página de la lista.");
                return new List<MonsterInfo>();
            }

            var monsters = extractor.DiscoverMonsters(html);
            Console.WriteLine($"[Discovery Protocol]: Descubiertos {monsters.Count} monstruos para {game.ToUpper()}.");
            return monsters;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery Protocol Error]: Fallo crítico. Razón: {ex.Message}");
            return new List<MonsterInfo>();
        }
    }
    private async Task<MonsterData?> ScrapeAndLearnMonsterAsync(string game, string monsterName)
    {
        // 1. Obtener el HTML de la página del monstruo (usando WebSearchAgent)
        Console.WriteLine($"[Knowledge Director]: Iniciando protocolo de aprendizaje para '{monsterName}' en '{game}'.");
        // Nota: Esta parte asume que tienes un agente que puede encontrar la URL correcta.
        // Para simplificar, construiremos una URL de Kiranico directamente.
        string gameId = game.StartsWith("mh") ? game.Substring(2) : game; // "mhworld" -> "world"
        string url = $"https://mhworld.kiranico.com/monsters/a8net/{monsterName.ToLower()}"; // Ejemplo para MHW
        if (game == "mhgu") url = $"https://mhgu.kiranico.com/monster/{monsterName.ToLower()}";

        string? html = await GetHtmlWithPlaywright(url);
        if (string.IsNullOrEmpty(html))
        {
            Console.WriteLine($"[Knowledge Director Error]: No se pudo obtener el HTML para '{monsterName}'.");
            return null;
        }

        var monsterData = new MonsterData { Name = monsterName };

        // 2. Realizar tareas de extracción específicas, una por una.
        Console.WriteLine("[Knowledge Director]: Desplegando agentes de extracción...");

        // TAREA 1: Extraer Debilidades
        var weaknessesTask = _webAgent.PerformExtractionTaskAsync(
            html,
            "Extract the monster's elemental and physical weaknesses for each body part.",
            "{ \"weaknesses\": [ { \"bodyPart\": \"Head\", \"elementalValues\": { \"cut\": 100, \"impact\": 100, \"shot\": 100, \"fire\": 3, \"water\": 0, \"thunder\": 1, \"ice\": 2, \"dragon\": 0 } } ] }"
        );

        // TAREA 2: Extraer Drops / Recompensas
        var dropsTask = _webAgent.PerformExtractionTaskAsync(
            html,
            "Extract the monster's item drops/rewards, including rank, source, item name, and percentage.",
            "{ \"itemDrops\": [ { \"rank\": \"High Rank\", \"source\": \"Body Carve\", \"itemName\": \"Rathalos Scale+\", \"percentage\": \"40%\" } ] }"
        );

        // TAREA 3: Extraer Descripción
        var descriptionTask = _webAgent.PerformExtractionTaskAsync(
            html,
            $"Extract a brief, one-paragraph description of the monster named '{monsterName}'.",
            "{ \"description\": \"A brief description of the monster...\" }"
        );

        // Ejecutamos todas las tareas en paralelo para máxima eficiencia
        await Task.WhenAll(weaknessesTask, dropsTask, descriptionTask);

        // 3. Ensamblar los resultados
        Console.WriteLine("[Knowledge Director]: Agentes han regresado. Ensamblando conocimiento...");

        var weaknessesResult = await weaknessesTask;
        if (weaknessesResult?["weaknesses"] is JArray weaknesses)
            monsterData.Weaknesses = weaknesses.ToObject<List<WeaknessInfo>>() ?? new List<WeaknessInfo>();

        var dropsResult = await dropsTask;
        if (dropsResult?["itemDrops"] is JArray drops)
            monsterData.ItemDrops = drops.ToObject<List<ItemDropInfo>>() ?? new List<ItemDropInfo>();

        var descriptionResult = await descriptionTask;
        monsterData.Description = descriptionResult?["description"]?.ToString() ?? "";


        // 4. Validar y Guardar en Caché
        if (string.IsNullOrEmpty(monsterData.Description) && !monsterData.Weaknesses.Any() && !monsterData.ItemDrops.Any())
        {
            Console.WriteLine($"[Knowledge Director Error]: No se pudo extraer información significativa para '{monsterName}'. No se guardará en caché.");
            return null;
        }

        CacheLearnedMonster(game, monsterData);
        return monsterData;
    }

    private async Task<string?> GetHtmlWithPlaywright(string url)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new PageGotoOptions { Timeout = 60000, WaitUntil = WaitUntilState.NetworkIdle });
            return await page.ContentAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Playwright Error]: No se pudo cargar la URL '{url}'. Razón: {ex.Message}");
            return null;
        }
    }
    private string ExtractRoarAudioUrlFromHtml(HtmlAgilityPack.HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//h1//audio/source");
        if (node != null)
        {
            string relativeUrl = node.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(relativeUrl))
                return "https://kiranico.com" + relativeUrl;
        }
        return "";
    }

    private MonsterData? MapKiranicoJsonToMonsterData(JToken? monsterToken, string monsterName)
    {
        if (monsterToken == null) return null;

        var monsterData = new MonsterData { Name = monsterName };

        // --- Patrón Robusto: Siempre usar TryParse desde un string ---
        int.TryParse(monsterToken["base_hp"]?.ToString(), out var baseHp);
        monsterData.BaseHP = baseHp;

        // --- Mapeo de Puntos de Vida (HP) por Rango ---
        float.TryParse(monsterToken["hp_mult_low"]?.ToString(), out var hpMultLow);
        if (hpMultLow > 0) monsterData.HPRanges.Add(new StatDetail { Label = "Low Rank", Value = $"{(int)(monsterData.BaseHP * hpMultLow)} HP" });

        float.TryParse(monsterToken["hp_mult_high"]?.ToString(), out var hpMultHigh);
        if (hpMultHigh > 0) monsterData.HPRanges.Add(new StatDetail { Label = "High Rank", Value = $"{(int)(monsterData.BaseHP * hpMultHigh)} HP" });

        float.TryParse(monsterToken["hp_mult_g"]?.ToString(), out var hpMultG);
        if (hpMultG > 0) monsterData.HPRanges.Add(new StatDetail { Label = "G Rank", Value = $"{(int)(monsterData.BaseHP * hpMultG)} HP" });

        // --- Mapeo de Umbrales de Cojeo (Limping) ---
        int.TryParse(monsterToken["limp_low"]?.ToString(), out var limpLow);
        if (limpLow > 0) monsterData.LimpingThresholds.Add(new StatDetail { Label = "Low Rank", Value = $"{limpLow}%" });

        int.TryParse(monsterToken["limp_high"]?.ToString(), out var limpHigh);
        if (limpHigh > 0) monsterData.LimpingThresholds.Add(new StatDetail { Label = "High Rank", Value = $"{limpHigh}%" });

        int.TryParse(monsterToken["limp_g"]?.ToString(), out var limpG);
        if (limpG > 0) monsterData.LimpingThresholds.Add(new StatDetail { Label = "G Rank", Value = $"{limpG}%" });

        // --- Mapeo de Umbrales de Captura ---
        int.TryParse(monsterToken["cap_low"]?.ToString(), out var capLow);
        if (capLow > 0) monsterData.CaptureThresholds.Add(new StatDetail { Label = "Low Rank", Value = $"{capLow}%" });

        int.TryParse(monsterToken["cap_high"]?.ToString(), out var capHigh);
        if (capHigh > 0) monsterData.CaptureThresholds.Add(new StatDetail { Label = "High Rank", Value = $"{capHigh}%" });

        int.TryParse(monsterToken["cap_g"]?.ToString(), out var capG);
        if (capG > 0) monsterData.CaptureThresholds.Add(new StatDetail { Label = "G Rank", Value = $"{capG}%" });

        // --- Mapeo de Límites de Aturdimiento (Stagger) ---
        foreach (var stagger in monsterToken["monsterstaggerlimits"] ?? Enumerable.Empty<JToken>())
        {
            var region = stagger["region"]?.ToString();
            if (!string.IsNullOrEmpty(region))
            {
                monsterData.StaggerLimits.Add(new StatDetail
                {
                    Label = region,
                    Value = stagger["value"]?.ToString() ?? "N/A"
                });
            }
        }

        // --- Mapeo de Estados Alterados (Abnormal Status) ---
        foreach (var status in monsterToken["weaponspecialattacks"] ?? Enumerable.Empty<JToken>())
        {
            monsterData.AbnormalStatuses.Add(new AbnormalStatusInfo
            {
                StatusName = status["local_name"]?.ToString() ?? "N/A",
                Initial = status["pivot"]?["initial"]?.ToString() ?? "0",
                Increase = status["pivot"]?["increase"]?.ToString() ?? "0",
                Max = status["pivot"]?["max"]?.ToString() ?? "0",
                Duration = status["pivot"]?["duration"]?.ToString() ?? "0",
                Damage = status["pivot"]?["damage"]?.ToString() ?? "0",
                Reduction = $"{status["pivot"]?["reduction_amount"]?.ToString() ?? "0"}/{status["pivot"]?["reduction_time"]?.ToString() ?? "0"}sec"
            });
        }

        // --- Mapeo de Enraged (Existente) ---
        float.TryParse(monsterToken["rage_duration"]?.ToString(), out var rageDuration);
        monsterData.EnragedStats["Duration"] = rageDuration;

        float.TryParse(monsterToken["rage_mod_attack"]?.ToString(), out var attackMod);
        monsterData.EnragedStats["AttackMod"] = attackMod;

        float.TryParse(monsterToken["rage_mod_defense"]?.ToString(), out var defenseMod);
        monsterData.EnragedStats["DefenseMod"] = defenseMod;

        float.TryParse(monsterToken["rage_mod_speed"]?.ToString(), out var speedMod);
        monsterData.EnragedStats["SpeedMod"] = speedMod;

        // --- Mapeo de Hábitats (Existente) ---
        foreach (var map in monsterToken["maps"] ?? Enumerable.Empty<JToken>())
        {
            monsterData.Habitats.Add(new HabitatInfo
            {
                Map = map["local_name"]?.ToString() ?? "N/A",
                StartArea = map["pivot"]?["start"]?.ToString() ?? "N/A",
                Areas = map["pivot"]?["areas"]?.ToString() ?? "N/A",
                RestArea = map["pivot"]?["rest"]?.ToString() ?? "N/A"
            });
        }

        // --- Mapeo de Debilidades (Existente) ---
        foreach (var part in monsterToken["monsterbodyparts"] ?? Enumerable.Empty<JToken>())
        {
            var weakness = new WeaknessInfo { BodyPart = part["local_name"]?.ToString() ?? "N/A" };
            weakness.ElementalValues["cut"] = part["pivot"]?["res_cut"]?.Value<int>() ?? 0;
            weakness.ElementalValues["impact"] = part["pivot"]?["res_impact"]?.Value<int>() ?? 0;
            weakness.ElementalValues["shot"] = part["pivot"]?["res_shot"]?.Value<int>() ?? 0;
            weakness.ElementalValues["fire"] = part["pivot"]?["res_fire"]?.Value<int>() ?? 0;
            weakness.ElementalValues["water"] = part["pivot"]?["res_water"]?.Value<int>() ?? 0;
            weakness.ElementalValues["ice"] = part["pivot"]?["res_ice"]?.Value<int>() ?? 0;
            weakness.ElementalValues["thunder"] = part["pivot"]?["res_thunder"]?.Value<int>() ?? 0;
            weakness.ElementalValues["dragon"] = part["pivot"]?["res_dragon"]?.Value<int>() ?? 0;
            monsterData.Weaknesses.Add(weakness);
        }

        // --- Mapeo de Item Drops (Existente) ---
        foreach (var item in monsterToken["items"] ?? Enumerable.Empty<JToken>())
        {
            monsterData.ItemDrops.Add(new ItemDropInfo
            {
                Rank = item["pivot"]?["rank"]?["local_name"]?.ToString() ?? "N/A",
                Source = item["pivot"]?["monsteritemmethod"]?["local_name"]?.ToString() ?? "N/A",
                ItemName = item["local_name"]?.ToString() ?? "N/A",
                Percentage = $"{item["pivot"]?["percentage"]?.ToString() ?? "0"}%"
            });
        }

        return monsterData;
    }

    public List<MonsterData>? GetKnownMonstersForGame(string game)
    {
        string gameKey = game.ToLower();
        if (_knowledgeCache.TryGetValue(gameKey, out var monsters))
        {
            return monsters.Values.ToList();
        }
        return null;
    }

    public void CacheLearnedMonster(string game, MonsterData monsterData)
    {
        string gameKey = game.ToLower();
        string monsterKey = monsterData.Name.ToLower();

        if (!_knowledgeCache.ContainsKey(gameKey))
        {
            _knowledgeCache[gameKey] = new Dictionary<string, MonsterData>();
        }

        Console.WriteLine($"[Knowledge]: Guardando en caché el conocimiento recién aprendido para '{monsterData.Name}' en {game.ToUpper()}.");
        // Aseguramos que el nombre esté bien capitalizado antes de guardar
        monsterData.Name = monsterKey.First().ToString().ToUpper() + monsterKey.Substring(1);
        _knowledgeCache[gameKey][monsterKey] = monsterData;
        SaveCache();
    }

    public void UpdateMonsterCache(string game, string monsterName, MonsterData newData)
    {
        string gameKey = game.ToLower();
        string monsterKey = monsterName.ToLower();

        if (!_knowledgeCache.ContainsKey(gameKey))
        {
            _knowledgeCache[gameKey] = new Dictionary<string, MonsterData>();
        }

        // Si ya existe una entrada, la actualizamos. Si no, la creamos.
        if (_knowledgeCache[gameKey].TryGetValue(monsterKey, out MonsterData? existingData))
        {
            Console.WriteLine($"[Knowledge]: Actualizando entrada existente para '{monsterName}'...");
            // Fusionamos la información. Damos prioridad a la nueva información si no es nula o vacía.
            if (!string.IsNullOrEmpty(newData.Description)) existingData.Description = newData.Description;
            if (newData.ItemDrops.Any()) existingData.ItemDrops = newData.ItemDrops;
            if (newData.Weaknesses.Any()) existingData.Weaknesses = newData.Weaknesses;
            if (newData.Habitats.Any()) existingData.Habitats = newData.Habitats;
            // Se pueden añadir más campos para fusionar aquí
        }
        else
        {
            Console.WriteLine($"[Knowledge]: Creando nueva entrada en caché para '{monsterName}'...");
            _knowledgeCache[gameKey][monsterKey] = newData;
        }

        SaveCache();
        Console.WriteLine($"[Knowledge]: ¡Caché para '{monsterName}' actualizado y guardado!");
    }

    public MonsterData? GetMonsterFromCache(string game, string monsterName)
    {
        string gameKey = game.ToLower();
        string monsterKey = monsterName.ToLower();

        if (_knowledgeCache.TryGetValue(gameKey, out var monsters) && monsters.TryGetValue(monsterKey, out var cachedData))
        {
            // Solo devolvemos los datos si consideramos que están completos.
            if (!string.IsNullOrEmpty(cachedData.Description) || cachedData.ItemDrops.Any() || cachedData.Weaknesses.Any())
            {
                Console.WriteLine($"[Knowledge]: Se encontró una entrada completa para '{monsterName}' en el caché.");
                return cachedData;
            }
        }
        return null;
    }

    
}