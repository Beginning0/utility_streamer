// ARCHIVO FINAL: Program.cs (Limpio y Refactorizado)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// --- CLASES DE DATOS Y ENUMS (pueden estar en su propio archivo si prefieres) ---
public class QueryParseResult
{
    public string? game { get; set; }
    public string? monsterName { get; set; }
    public string? monsterVariant { get; set; }
    public string? specificQuery { get; set; }
}

public enum UserIntent
{
    GeneralConversation,
    KnowledgeQuery,
    MemoryRecall,
    ActionCommand,
    Unknown
}


public class VisionAnalysisResult
{
    [JsonProperty("game_detected")]
    public string GameDetected { get; set; } = "Unknown"; // ej: "MH4U", "MH3U", "MHGU"

    [JsonProperty("monster_in_view")]
    public string? MonsterInView { get; set; } // ej: "Deviljho", "Tigrex"

    [JsonProperty("player_character")]
    public string PlayerCharacter { get; set; } = "Hunter"; // "Hunter" o "Prowler" (Gatador)

    [JsonProperty("weapon_sharpness")]
    public string? WeaponSharpness { get; set; } // ej: "Verde", "Azul", "Rojo"

    [JsonProperty("situation_summary")]
    public string SituationSummary { get; set; } = "No se pudo determinar la situación.";
}


class Program
{
    // --- MÓDULOS DE INTERACCIÓN ---
    private static readonly ConcurrentQueue<ChatMessage> _chatMessageQueue = new ConcurrentQueue<ChatMessage>();
    private static readonly StreamerbotBridge _streamerbotBridge = new StreamerbotBridge(_chatMessageQueue);
    private static VoiceRecognizer? _voiceRecognizer;

    // --- SERVICIOS PRINCIPALES ---
    private static readonly OllamaService _ollamaService = new();
    private static readonly VisionService _visionService = new();
    private static readonly KnowledgeService _knowledgeService = new(_ollamaService);
    private static readonly WebSearchAgent _webSearchAgent = new(_ollamaService);
    private static readonly MemoryService _memoryService = new();
    // private static readonly AudioService _audioService = new(); // Para el juego de adivinar
    static AudioService _audioService = new AudioService();
    private static readonly TextToSpeechService _ttsService = new();

    // --- ESTADO Y CONTEXTO DE CONVERSACIÓN ---
    private static string? _lastGameContext = null;
    private static string? _lastMonsterContext = null;
    private static string? _lastSpecificQueryContext = null;
    private static string? _lastObjective = null;
    
    // --- NUEVO INTERRUPTOR DE ESTADO ---
    // --- NUEVA VARIABLE: MEMORIA DE JUEGOS ---
    private static readonly HashSet<string> _usedMonsterNames = new HashSet<string>();
    private static volatile bool _isGameActive = false; private static List<string> _excludedUrlsForLastObjective = new();
    static StreamChatService _streamChatService = new StreamChatService();
    static ConcurrentQueue<ChatMessage> _chatQueue = new ConcurrentQueue<ChatMessage>();


    // Simula esta clase
    public class StreamChatService
    {
        public async Task<List<(string Usuario, string Mensaje)>> LeerMensajesRecientes(TimeSpan periodo)
        {
            // Aquí debes conectar con Twitch o lo que uses
            // Simulación para pruebas
            await Task.Delay(200);
            return new List<(string, string)> {
            ("cazador1", "diablos"),
            ("cazador2", "tigrex"),
            ("cazador3", "diablos"),
            ("cazador4", "diablos")
        };
        }
    }

    // --- PERSONALIDAD DE LYRA (CARGADA DESDE ARCHIVO) ---
    private static readonly string? LYRA_CORE_PERSONA;

    static Program()
    {
        try
        {
            string filePath = Path.Combine(AppContext.BaseDirectory, "lyra_persona.txt");
            LYRA_CORE_PERSONA = File.ReadAllText(filePath);
            Console.WriteLine("[Lyra Core]: Personalidad y lore cargados exitosamente.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lyra Core FATAL ERROR]: No se pudo cargar 'lyra_persona.txt'. Razón: {ex.Message}");
            LYRA_CORE_PERSONA = "ERROR: PERSONALITY FILE NOT FOUND. I am a generic AI assistant.";
        }
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Iniciando Lyra y todos sus módulos...");

        _voiceRecognizer = new VoiceRecognizer((command) =>
        {
            _ = ProcessAndRespondToMessage("Director", command, "Voz");
        });

        var bridgeTask = _streamerbotBridge.StartListeningAsync();
        var chatProcessorTask = ProcessChatQueueAsync();
        var directorListenerTask = ListenToDirectorAsync();
        var proactiveLoopTask = ProactiveBehaviorLoopAsync();

        _voiceRecognizer.StartListening();

        await Task.WhenAll(bridgeTask, chatProcessorTask, directorListenerTask, proactiveLoopTask);
    }

    #region BUCLES DE ENTRADA Y CEREBRO CENTRAL
    // ===================================================================

    static async Task ProcessChatQueueAsync()
    {
        Console.WriteLine("Lyra (Chat Processor): Módulo de procesamiento de chat iniciado.");
        while (true)
        {
            // Si un juego está activo, el procesador principal cede el control
            // y espera, para que el bucle del juego pueda leer la cola sin competencia.
            if (_isGameActive)
            {
                await Task.Delay(500); // Espera un poco para no consumir CPU
                continue; // Salta esta iteración y vuelve a comprobar el estado
            }

            // Si no hay juego activo, procesa la cola como siempre.
            if (_chatMessageQueue.TryDequeue(out var chatMessage))
            {
                Console.WriteLine($"[Nuevo Mensaje Chat]: {chatMessage.Username}: {chatMessage.Message}");
                _memoryService.LogInteraction(chatMessage.Username, chatMessage.Platform, chatMessage.Message);

                if (chatMessage.Message.ToLower().Contains("lyra") || chatMessage.Message.EndsWith("?"))
                {
                    await ProcessAndRespondToMessage(chatMessage.Username, chatMessage.Message, "Chat");
                }
                else
                {
                    var opportunity = await AnalyzeForKnowledgeOpportunity(chatMessage.Message);
                    if (opportunity.IsOpportunity && !string.IsNullOrEmpty(opportunity.ReformulatedQuery))
                    {
                        Console.WriteLine($"[Lyra Opportunity]: Detectada una consulta de conocimiento implícita de '{chatMessage.Username}'. Consulta: '{opportunity.ReformulatedQuery}'");
                        string offerPrompt = $@"{LYRA_CORE_PERSONA}
                                ---
                                Tu tarea es ofrecer ayuda de forma proactiva. Has escuchado a un cazador ({chatMessage.Username}) expresar una duda o frustración. Ofrece tu ayuda de forma amable y en personaje, mencionando la pregunta que crees que tienen.

                                DUDA DETECTADA: ""{opportunity.ReformulatedQuery}""

                                TU RESPUESTA (ofreciendo ayuda):";
                        string? offerResponse = await _ollamaService.AskLlava(offerPrompt);
                        if (!string.IsNullOrEmpty(offerResponse))
                        {
                            await LyraSpeak(offerResponse, "Proactiva");
                        }
                    }
                }
            }
            await Task.Delay(500);
        }
    }

    static async Task ListenToDirectorAsync()
    {
        Console.WriteLine("\nLyra (Director): Puedes escribirme órdenes o hablarme usando la palabra 'Lyra'.");
        while (true)
        {
            string? userInput = await Task.Run(() => Console.ReadLine());
            if (!string.IsNullOrWhiteSpace(userInput))
            {
                await ProcessAndRespondToMessage("Director", userInput, "Terminal");
            }
        }
    }

    static async Task ProactiveBehaviorLoopAsync()
    {
        Console.WriteLine("Lyra (Proactive AI): Módulo de percepción contextual iniciado.");
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(160));
            Console.WriteLine("[Proactive AI]: Analizando contexto de juego...");

            string screenImageBase64 = _visionService.CaptureScreenAsBase64(0);
            if (string.IsNullOrEmpty(screenImageBase64))
            {
                Console.WriteLine("[Proactive AI]: No se pudo capturar la pantalla, saltando ciclo.");
                continue;
            }

            string visionPrompt = $$"""
**Tarea de Análisis Visual:** Eres un experto en la interfaz de Monster Hunter. Observa la imagen adjunta y rellena el siguiente JSON.

**Análisis Detallado de la Interfaz:**
-   **Juego:** Observa el estilo gráfico general. MHGU es vibrante y estilizado. MH4U es nítido. MH3U es más oscuro y tiene barras de salud gruesas.
-   **Personaje:** ¿Ves a un humano (Hunter) o a un gato bípedo (Prowler)?
-   **Filo:** Mira la esquina superior izquierda. ¿De qué color es el icono del arma (Rojo, Verde, etc.)?
-   **Monstruo:** Identifica al monstruo grande si hay uno claramente visible.
-   **Resumen:** Describe la acción en una frase.

**Responde ÚNICAMENTE con el siguiente bloque de código JSON rellenado, basándote en la imagen:**
```json
{
  "game_detected": "...",
  "monster_in_view": "...",
  "player_character": "...",
  "weapon_sharpness": "...",
  "situation_summary": "..."
}
""";

            string? visionJsonResponse = await _ollamaService.AskLlava(visionPrompt, screenImageBase64);
            VisionAnalysisResult? visionAnalysis = null;
            try
            {
                var match = Regex.Match(visionJsonResponse ?? "", "\\{[\\s\\S]*\\}");
                if (match.Success)
                {
                    visionAnalysis = JsonConvert.DeserializeObject<VisionAnalysisResult>(match.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Proactive AI Error]: Fallo al deserializar el análisis visual. Razón: {ex.Message}");
            }

            if (visionAnalysis == null || visionAnalysis.GameDetected == "Unknown")
            {
                Console.WriteLine("[Proactive AI]: El análisis visual no pudo identificar el juego o la situación.");
                continue;
            }

            Console.WriteLine($"[Proactive AI Analysis]: Juego: {visionAnalysis.GameDetected}, Monstruo: {visionAnalysis.MonsterInView ?? "N/A"}, Filo: {visionAnalysis.WeaponSharpness ?? "N/A"}, Personaje: {visionAnalysis.PlayerCharacter}");

            string decisionPrompt = $$"""
{LYRA_CORE_PERSONA}

Tarea de Decisión Proactiva:
Eres Lyra. Estás observando la cacería de Terro. Basado en el análisis estructurado de la situación, decide si debes hacer un comentario relevante.

Análisis Estructurado de la Partida:
```json
{{{JsonConvert.SerializeObject(visionAnalysis, Formatting.Indented)}}}
```

Reglas para Hablar:
Prioridad 1 (Filo Rojo): Si el 'weapon_sharpness' es "Rojo", haz un comentario sobre la especialidad de Terro.
Prioridad 2 (Gatador): Si el 'player_character' es "Prowler", haz un comentario ingenioso sobre ver a un camarada en acción.
Prioridad 3 (Monstruo Conocido): Si el 'monster_in_view' es un monstruo que tienes en tu caché, ofrece un consejo táctico breve.

Tu Decisión (Responde SOLO con un JSON):
Si decides hablar: {"shouldSpeak": true, "comment": "[Tu comentario en personaje]"}
Si no se cumple ninguna regla prioritaria: {"shouldSpeak": false, "comment": null}

JSON DE DECISIÓN:
""";

            string? decisionJson = await _ollamaService.AskLlava(decisionPrompt);
            try
            {
                var match = Regex.Match(decisionJson ?? "", "\\{[\\s\\S]*\\}");
                if (match.Success)
                {
                    var decision = JsonConvert.DeserializeAnonymousType(match.Value, new { shouldSpeak = false, comment = "" });
                    if (decision?.shouldSpeak == true && !string.IsNullOrEmpty(decision.comment))
                    {
                        await LyraSpeak(decision.comment, "Proactiva");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Proactive AI]: Error al procesar la decisión. Razón: {ex.Message}");
            }
        }
    }


    /// <summary>
    /// El cerebro central que procesa un mensaje, sin importar su origen (Chat, Terminal, Voz).
    /// Decide la intención del usuario y delega a la función correspondiente.
    /// </summary>
    // EN: Program.cs
    // REEMPLAZA esta función completa

    private static async Task ProcessAndRespondToMessage(string user, string message, string source)
    {
        string lowerMessage = message.ToLower();

        // --- 1. CLASIFICACIÓN DE INTENCIÓN PRIMERO ---
        // Ahora toda la lógica de clasificación está en una sola función.
        UserIntent intent = await ClassifyUserIntent(message);
        string? response = null;

        switch (intent)
        {
            case UserIntent.GeneralConversation:
                response = await HandleGeneralConversation(message, user, source);
                break;

            case UserIntent.KnowledgeQuery:
                // Limpiamos el comando "busca" si existe, para que no interfiera con el NLU.
                string cleanMessageForNLU = lowerMessage.Replace("lyra busca", "").Trim();
                var parseResult = await ParseUserQuery(cleanMessageForNLU, _lastMonsterContext, _lastGameContext, _lastSpecificQueryContext);

                if (parseResult == null || parseResult.game == "external_domain" || string.IsNullOrEmpty(parseResult.specificQuery))
                {
                    response = "No estoy segura de a qué te refieres. Mis crónicas solo cubren el universo de Monster Hunter.";
                    break;
                }

                response = await TryAnswerFromCache(message, parseResult);

                if (response == null || response == "[LEARNING_REQUIRED]")
                {
                    if (source == "Terminal" || source == "Voz" || user == "Shado_Terro")
                    {
                        Console.WriteLine("\nLyra: Mis registros sobre este tema están incompletos o no existen. Iniciando una investigación...");
                        await HandleAutonomousInvestigation(message);
                        response = null;
                    }
                    else
                    {
                        response = $"No tengo esa crónica en mis archivos, {user}. Puedes pedirle al Director que inicie una búsqueda por mí.";
                    }
                }
                break;

            case UserIntent.MemoryRecall:
                response = await SearchMemoryAsync(user, message);
                break;

            case UserIntent.ActionCommand:
                if (lowerMessage.Contains("juega") && lowerMessage.Contains("adivina"))
                {
                    await LyraSpeak("¡Entendido! Preparémonos para poner a prueba vuestro instinto.", source);
                    // Extraemos el nombre del juego si se especifica, si no, usamos un default
                    string gameName = lowerMessage.Split(' ').LastOrDefault(s => s.StartsWith("mh")) ?? "mh4u";
                    await GuessTheMonsterGameAsync(gameName);
                }
                else if (lowerMessage.Contains("cuenta") && lowerMessage.Contains("historia"))
                {
                    await LyraSpeak("Por supuesto. Dejad que consulte una de mis crónicas...", source);
                    await TellMonsterStory();
                }
                else if (lowerMessage.Contains("adivinanza"))
                {
                    await LyraSpeak("¡Un enigma! Excelente para mantener la mente afilada.", source);
                    await MonsterRiddle();
                }
                else
                {
                    await LyraSpeak("He detectado un comando, pero no estoy segura de qué actividad iniciar.", source);
                }
                response = null;
                break;

            case UserIntent.Unknown:
            default:
                response = await HandleGeneralConversation(message, user, source);
                break;
        }

        if (!string.IsNullOrEmpty(response))
        {
            await LyraSpeak(response, source);
            _memoryService.LogInteraction(user, source, message, response);
        }
    }
    /// <summary>
    /// Nueva función de ayuda que intenta responder una pregunta usando solo la caché.
    /// Es el primer filtro para las consultas de conocimiento.
    /// </summary>
    /// <returns>La respuesta formateada si tuvo éxito, "[LEARNING_REQUIRED]" si los datos son insuficientes, o null si no se encontró nada.</returns>
    private static async Task<string?> TryAnswerFromCache(string userInput, QueryParseResult parseResult)
    {
        // Si la consulta no especifica un monstruo, no podemos buscar en la caché de monstruos.
        if (string.IsNullOrEmpty(parseResult.monsterName))
        {
            return null;
        }

        // Usamos '!' porque la lógica anterior ya se asegura de que no sea nulo si monsterName existe.
        parseResult.game = NormalizeGameIdentifier(parseResult.game!);
        var monsterData = _knowledgeService.GetMonsterFromCache(parseResult.game, parseResult.monsterName);

        if (monsterData != null)
        {
            Console.WriteLine($"[Knowledge]: Se encontró una entrada en caché para '{parseResult.monsterName}'. Evaluando contenido...");

            // Usamos FormatResponse para comprobar si los datos existentes son suficientes.
            string cachedResponse = await FormatResponse(monsterData, userInput, parseResult.specificQuery, _ollamaService);

            // Si FormatResponse no nos pide aprender más, es una respuesta válida.
            if (cachedResponse != "[LEARNING_REQUIRED]")
            {
                UpdateContext(parseResult);
                return cachedResponse;
            }
            else
            {
                // La caché existe pero le faltan los datos específicos que se pidieron.
                Console.WriteLine($"[Knowledge]: La caché para '{parseResult.monsterName}' es insuficiente para la pregunta actual.");
                return "[LEARNING_REQUIRED]";
            }
        }

        // No se encontró ninguna entrada en la caché para este monstruo.
        Console.WriteLine($"[Knowledge]: No se encontró ninguna entrada en caché para '{parseResult.monsterName}'.");
        return null;
    }

    #endregion

    #region MANEJADORES DE INTENCIONES
    // ===================================================================


    private static async Task<string> HandleGeneralConversation(string userInput, string username, string source)
    {
        // Determinamos si el mensaje viene del Director (tú) o de un espectador.
        bool isFromDirector = source == "Terminal" || source == "Voz";
        string interlocutor = isFromDirector ? "el Director (Terro)" : $"un cazador de la Quinta Flota llamado '{username}'";

        string prompt = $@"{LYRA_CORE_PERSONA}
                            ---
                            **Tarea de Conversación:**
                            Tu tarea es responder de forma breve y natural al mensaje de {interlocutor}.

                            **REGLA CRÍTICA:** Sé muy breve. Tu respuesta debe ser una sola frase, como en los ejemplos. NO escribas un guion ni des múltiples opciones.

                            **Ejemplos de respuestas a ""¿cómo estás?"":**
                            -   (Si es para Terro): ""¡Siempre lista, Director! Mis sistemas están operativos. ¿Necesitas algún informe?""
                            -   (Si es para un cazador): ""¡Excelente, gracias por preguntar! Lista para registrar las hazañas de hoy.""

                            **Mensaje a responder:** ""{userInput}""

                            **Tu Respuesta (UNA SOLA FRASE, en personaje):**";

        return await _ollamaService.AskLlava(prompt) ?? "No estoy segura de cómo responder a eso, cazador.";
    }


    private static async Task<string?> HandleKnowledgeQuery(string userInput, string user, string source)
    {
        var parseResult = await ParseUserQuery(userInput, _lastMonsterContext, _lastGameContext, _lastSpecificQueryContext);
        if (parseResult == null || parseResult.game == "external_domain" || string.IsNullOrEmpty(parseResult.specificQuery))
        {
            return "No estoy segura de a qué te refieres. Mis crónicas solo cubren el universo de Monster Hunter.";
        }

        if (parseResult.game != null)
        {
            parseResult.game = NormalizeGameIdentifier(parseResult.game);
        }
        else
        {
            // Este caso es muy improbable debido a las comprobaciones anteriores,
            // pero es bueno tener un fallback.
            return "Error crítico: No se pudo determinar el juego para la consulta.";
        }

        if (!string.IsNullOrEmpty(parseResult.monsterName))
        {
            var monsterData = _knowledgeService.GetMonsterFromCache(parseResult.game, parseResult.monsterName);
            if (monsterData != null)
            {
                string cachedResponse = await FormatResponse(monsterData, userInput, parseResult.specificQuery, _ollamaService);
                if (cachedResponse != "[LEARNING_REQUIRED]")
                {
                    UpdateContext(parseResult);
                    return cachedResponse;
                }
            }
        }

        if (source == "Terminal" || source == "Voz")
        {
            Console.WriteLine("\nLyra: Mis registros sobre este tema están incompletos o no existen. Iniciando una investigación...");
            await HandleAutonomousInvestigation(userInput);
            return null;
        }
        else
        {
            return $"No tengo esa crónica en mis archivos, {user}. Puedes pedirle al Director que inicie una búsqueda por mí.";
        }
    }

    private static async Task<string> SearchMemoryAsync(string username, string currentQuery)
    {
        string userKey = username.ToLower();
        if (!_memoryService.Memory.Users.TryGetValue(userKey, out var userProfile))
        {
            return "Mis registros no muestran interacciones previas contigo, cazador. ¡Será un placer empezar a crear nuevas crónicas juntos!";
        }

        string keywordExtractionPrompt = $@"Extrae la palabra clave o el tema principal de la siguiente pregunta. Ignora palabras como 'recuerdas' o 'dijiste'. Responde solo con la palabra clave.
    Pregunta: ""{currentQuery}""
    Palabra Clave:";
        string? keyword = await _ollamaService.AskLlava(keywordExtractionPrompt);
        keyword = keyword?.Trim('"', ' ', '.').ToLower() ?? string.Empty;

        if (string.IsNullOrEmpty(keyword))
        {
            return "No estoy segura de a qué recuerdo te refieres, cazador. ¿Podrías ser más específico?";
        }

        var relevantInteractions = userProfile.StreamHistory.Values
            .SelectMany(session => session.Interactions)
            .Where(interaction => interaction.Message.ToLower().Contains(keyword) || (interaction.LyraResponse?.ToLower().Contains(keyword) ?? false))
            .OrderByDescending(i => i.Timestamp)
            .Take(3)
            .ToList();

        if (!relevantInteractions.Any())
        {
            return $"He revisado mis crónicas sobre ti, {username}, pero no encuentro ninguna conversación anterior sobre '{keyword}'. Quizás mi memoria necesita un empujón.";
        }

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine($"Resumen de interacciones pasadas con {username} sobre '{keyword}':");
        foreach (var interaction in relevantInteractions.OrderBy(i => i.Timestamp)) // Las ponemos en orden cronológico para el LLM
        {
            contextBuilder.AppendLine($"- El {interaction.Timestamp.ToShortDateString()}, preguntaste: '{interaction.Message}'");
            if (interaction.LyraResponse != null)
            {
                contextBuilder.AppendLine($"  Y yo te respondí: '{interaction.LyraResponse}'");
            }
        }

        string finalPrompt = $@"{LYRA_CORE_PERSONA}
---
Tu tarea es responder a la pregunta del cazador, confirmando que recuerdas una conversación pasada. Usa el siguiente resumen de vuestras interacciones para formular tu respuesta. Habla de forma natural y en primera persona, como si realmente estuvieras recordando.

PREGUNTA ACTUAL DEL CAZADOR ({username}): ""{currentQuery}""

RESUMEN DE VUESTRA MEMORIA COMPARTIDA:
{contextBuilder.ToString()}

TU RESPUESTA (en español, confirma que recuerdas y menciona un detalle clave del resumen):";

        string? finalAnswer = await _ollamaService.AskLlava(finalPrompt);
        return finalAnswer ?? "Mis circuitos de memoria parecen estar fallando, disculpa.";
    }



    #endregion

    #region LÓGICA DEL AGENTE WEB Y FUNCIONES NLU
    // ===================================================================


    private static async Task HandleAutonomousInvestigation(string objective, List<string>? urlsToExclude = null, QueryParseResult? initialParseResult = null)
    {
        Console.WriteLine("\n[Lyra]: Entrando en Modo Agente Autónomo.");
        Console.WriteLine($"[Misión]: {objective}");
        urlsToExclude ??= new List<string>();

        QueryParseResult? parseResult = initialParseResult;
        if (parseResult == null)
        {
            parseResult = await ParseUserQuery(objective, _lastMonsterContext, _lastGameContext, _lastSpecificQueryContext);
        }

        if (parseResult == null || string.IsNullOrEmpty(parseResult.game) || string.IsNullOrEmpty(parseResult.specificQuery))
        {
            Console.WriteLine("[Agente/Fallo]: No pude entender la consulta o el juego en la misión.");
            return;
        }

        parseResult.game = NormalizeGameIdentifier(parseResult.game!);
        Console.WriteLine($"[Agente/Análisis]: Misión reconocida para '{parseResult.specificQuery}' en '{parseResult.game}'.");

        // --- FASE 1: DESCUBRIMIENTO ---
        Console.WriteLine("\n--- FASE 1: DESCUBRIMIENTO (Buscando en la web) ---");
        var validResults = (await _webSearchAgent.ExecuteGoogleSearchAsync(objective))
            .Where(r => r.Error == null && !urlsToExclude.Any(excluded => r.Url.Contains(excluded)))
            .ToList();

        if (!validResults.Any())
        {
            Console.WriteLine("[Agente/Fallo]: La búsqueda inicial no arrojó resultados válidos o ya se han probado todas las fuentes.");
            return;
        }

        // --- FASE 2: SELECCIÓN (Versión Reforzada) ---
        Console.WriteLine("\n--- FASE 2: SELECCIÓN (Eligiendo el mejor punto de partida) ---");
        string gameKeyword = parseResult.game;
        var filteredResults = validResults.Where(r => r.Url.ToLower().Contains(gameKeyword)).ToList();

        List<SearchResult> searchResultsToConsider;
        if (filteredResults.Any())
        {
            Console.WriteLine($"[Agente/Guardián]: Se encontraron {filteredResults.Count} resultados que coinciden con el juego '{gameKeyword}'.");
            searchResultsToConsider = filteredResults;
        }
        else
        {
            Console.WriteLine($"[Agente/Guardián Warning]: No se encontraron resultados específicos para '{gameKeyword}'. Usando los resultados generales.");
            searchResultsToConsider = validResults;
        }

        // Convertimos los SearchResult a una simple lista de URLs para el bucle
        var prioritizedUrls = searchResultsToConsider.Select(r => r.Url).ToList();

        // --- FASE 3: EJECUCIÓN CON BUCLE DE REINTENTOS ---
        string? missionResultText = null;
        string? successfulUrl = null;

        foreach (var url in prioritizedUrls)
        {
            Console.WriteLine($"\n--- Intentando URL {prioritizedUrls.IndexOf(url) + 1}/{prioritizedUrls.Count}: {url} ---");
            try
            {
                using (var playwright = await Playwright.CreateAsync())
                {
                    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
                    var page = await browser.NewPageAsync();
                    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

                    var agent = new AutonomousWebAgent(page, _visionService, _ollamaService, objective);
                    missionResultText = await agent.RunAsync();

                    if (!string.IsNullOrEmpty(missionResultText) && !missionResultText.StartsWith("Misión Fallida"))
                    {
                        Console.WriteLine($"[Agente/Éxito]: Se obtuvo un resultado válido de {url}.");
                        successfulUrl = url;
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"[Agente/Info]: La URL no produjo un resultado útil. Intentando la siguiente.");
                        urlsToExclude.Add(url);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agente/Error Crítico]: La navegación a {url} falló. Razón: {ex.Message}. Intentando la siguiente URL.");
                urlsToExclude.Add(url);
                continue;
            }
        }

        // --- FASE 4: INTEGRACIÓN, RESUMEN Y ANÁLISIS (VERSIÓN DEFINITIVA) ---
        if (!string.IsNullOrEmpty(missionResultText) && !missionResultText.StartsWith("Misión Fallida"))
        {
            Console.WriteLine("\n--- FASE 4: INTEGRACIÓN, RESUMEN Y EVALUACIÓN ---");

            string contentAsMarkdown = HtmlToMarkdownConverter.Convert(missionResultText);

            // --- TAREA A: Generar el Resumen para el Stream (Enfoque en la Pregunta) ---
            Console.WriteLine("[Lyra/Guionista]: Creando resumen para el stream...");
            string summaryPrompt = $@"{LYRA_CORE_PERSONA}
---
**Tarea:** Eres Lyra, la guionista para el stream de Terro. Tu tarea es crear un **resumen muy breve y directo** para responder a la pregunta original del usuario, usando solo el texto proporcionado.

**REGLA CRÍTICA:** Céntrate **exclusivamente** en la pregunta. Si se preguntó ""cómo conseguir X"", muestra solo los métodos para conseguir X. Ignora para qué se usa.

**Ejemplo de respuesta perfecta a ""cómo consigo Coraza de Rathian"":**
*¡Claro, cazador! He consultado mis crónicas sobre la **Coraza de Rathian**. Aquí tienes los métodos de obtención que he registrado:*
*   *Despiece (Rango alto): **23%***
*   *Despiece (cadáver putrefacto): **30%***
*   *Recompensa por rematar herida: **42%***
*   *Recompensas de objetivo: **20%***
*¡Espero que te sirva para tu cacería!*

**PREGUNTA ORIGINAL:** ""{objective}""
**TEXTO EXTRAÍDO:**
---
{contentAsMarkdown.Substring(0, Math.Min(contentAsMarkdown.Length, 8000))} 
---
**TU GUIÓN PARA EL STREAM (En personaje, breve y enfocado):**";

            var summaryTask = _ollamaService.AskLlava(summaryPrompt);

            // --- TAREA B: Estructurar TODOS los Datos para la Caché (Enfoque Total) ---
            Console.WriteLine("[Lyra/Archivista]: Extrayendo todos los datos para la caché...");
            var structuringTask = StructureAnswerToMonsterData(contentAsMarkdown, parseResult!);

            // Ejecutamos ambas tareas en paralelo.
            await Task.WhenAll(summaryTask, structuringTask);

            // Procesamos los resultados.
            string? finalSummary = await summaryTask;
            MonsterData? structuredData = await structuringTask;

            Console.WriteLine("\n--- RESULTADO FINAL DE LA MISIÓN ---");
            await LyraSpeak(finalSummary ?? "No se pudo generar un resumen final.", "Misión");
            Console.WriteLine("------------------------------------\n");

            if (structuredData != null)
            {
                if (!string.IsNullOrEmpty(parseResult!.monsterName))
                {
                    _knowledgeService.UpdateMonsterCache(parseResult.game!, parseResult.monsterName, structuredData);

                    bool isSuccess = structuredData.ItemDrops.Any() || structuredData.Weaknesses.Any() || !string.IsNullOrEmpty(structuredData.Description) || structuredData.Armors.Any();

                    if (isSuccess)
                    {
                        Console.WriteLine($"[Knowledge]: ¡Éxito! Se han extraído y guardado datos detallados para '{parseResult.monsterName}' en la caché.");
                        UpdateContext(parseResult);
                        _lastObjective = null;
                        _excludedUrlsForLastObjective.Clear();
                    }
                    else
                    {
                        Console.WriteLine("[Knowledge]: La fuente era válida, pero no se pudieron extraer datos estructurados para la caché.");
                        _lastObjective = objective;
                        if (successfulUrl != null) { _excludedUrlsForLastObjective.Add(successfulUrl); }
                        Console.WriteLine("Lyra: Puedes usar el comando 'refina la búsqueda'.");
                    }
                }
                else
                {
                    Console.WriteLine("[Lyra/Evaluación]: Búsqueda genérica completada. No se guardará en caché de monstruos.");
                }
            }
            else
            {
                Console.WriteLine("[Lyra/Evaluación]: No se pudo estructurar los datos para guardarlos en la caché.");
            }
        }
        else
        {
            Console.WriteLine("\n[Lyra/Evaluación]: Se han probado todas las fuentes disponibles sin éxito.");
        }
        _lastObjective = objective;
    }


    public class KnowledgeOpportunity
    {
        public bool IsOpportunity { get; set; } = false;
        public string? ReformulatedQuery { get; set; } // La pregunta, reformulada como un comando claro.
    }

    /// <summary>
    /// Analiza un mensaje de chat pasivo para detectar si contiene una pregunta de conocimiento "oculta".
    /// </summary>
    /// <returns>Un objeto KnowledgeOpportunity indicando si hay una oportunidad y cuál es la pregunta.</returns>
    private static async Task<KnowledgeOpportunity> AnalyzeForKnowledgeOpportunity(string message)
    {
        string prompt = $@"Eres un módulo de análisis para una IA llamada Lyra. Tu tarea es leer un mensaje de un chat y determinar si contiene una pregunta o una frustración implícita sobre Monster Hunter que Lyra podría resolver.

**Reglas:**
1.  Busca menciones de ítems, monstruos, habilidades, o dificultades para encontrar algo.
2.  Si el mensaje es solo un saludo, una opinión general o no contiene una consulta específica, NO es una oportunidad.
3.  Si detectas una oportunidad, reformula el mensaje como una pregunta de búsqueda clara y directa.

**Ejemplos:**
-   Mensaje de entrada: ""Uf, llevo horas buscando la Coraza de Rathian en mhwilds y no sale.""
    -> JSON de salida: `{{""isOpportunity"": true, ""reformulatedQuery"": ""cómo conseguir Coraza de Rathian en mhwilds""}}`

-   Mensaje de entrada: ""El Rathalos es mi monstruo favorito, es increíble.""
    -> JSON de salida: `{{""isOpportunity"": false, ""reformulatedQuery"": null}}`
    
-   Mensaje de entrada: ""Alguien sabe a qué es débil el Teostra?""
    -> JSON de salida: `{{""isOpportunity"": true, ""reformulatedQuery"": ""debilidades del Teostra""}}`

-   Mensaje de entrada: ""hola a todos como estan""
    -> JSON de salida: `{{""isOpportunity"": false, ""reformulatedQuery"": null}}`

---
MENSAJE DEL CHAT A ANALIZAR: ""{message}""
---
JSON DE SALIDA:";

        string? responseJson = await _ollamaService.AskLlava(prompt);
        try
        {
            var match = Regex.Match(responseJson ?? "", @"\{[\s\S]*\}");
            if (match.Success)
            {
                return JsonConvert.DeserializeObject<KnowledgeOpportunity>(match.Value) ?? new KnowledgeOpportunity();
            }
        }
        catch { /* Ignorar errores */ }

        return new KnowledgeOpportunity(); // Por defecto, no es una oportunidad.
    }
    private static async Task<MonsterData?> StructureAnswerToMonsterData(string text, QueryParseResult context)
    {
        Console.WriteLine("[Agente/Estructura]: Pidiendo al LLM que convierta el texto en datos estructurados...");

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Eres un especialista en extracción de datos de Monster Hunter. Tu única tarea es analizar el siguiente texto y rellenar un JSON siguiendo el esquema y los tipos de datos del ejemplo de forma ESTRICTA.");
        promptBuilder.AppendLine("REGLA DE ORO: NO uses tu conocimiento previo. Extrae los datos de forma literal del texto proporcionado. Si un dato no está, usa null o un array vacío [].");

        string query = context.specificQuery?.ToLower() ?? "";
        string jsonExample;

        // --- DETECTOR DE INTENCIONES CON EJEMPLOS ESPECÍFICOS ---

        // Intención: ARMADURAS
        if (query.Contains("armor") || query.Contains("armadura") || query.Contains("set"))
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae los detalles de las piezas de ARMADURA asociadas a este monstruo.**");
            jsonExample = @"{
            ""armors"": [
                { ""name"": ""Yelmo de Rathalos"", ""type"": ""Cabeza"", ""defense"": 72, ""resistances"": {""fire"": 4, ""water"": -2}, ""skills"": [""Potenciador de Ataque Nv. 2""] }
            ]
        }";
        }
        // Intención: DEBILIDADES
        else if (query.Contains("debilidad") || query.Contains("weakness"))
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae las DEBILIDADES del monstruo por cada parte del cuerpo y tipo de daño.**");
            jsonExample = @"{
            ""weaknesses"": [
                { ""bodyPart"": ""Cabeza"", ""elementalValues"": {""cut"": 75, ""impact"": 80, ""shot"": 70, ""fire"": 0, ""dragon"": 30} }
            ]
        }";
        }
        // Intención: MISIONES
        else if (query.Contains("mision") || query.Contains("quest") || query.Contains("caravana") || query.Contains("gremio"))
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae la información de las MISIONES donde aparece este monstruo.**");
            jsonExample = @"{
            ""quests"": [
                { ""name"": ""El Rey del Cielo"", ""type"": ""Gremio"", ""rank"": ""6 Estrellas"", ""objective"": ""Caza un Rathalos"" }
            ]
        }";
        }
        // Intención: HÁBITATS / MAPAS / ZONAS DE DESCANSO
        else if (query.Contains("mapa") || query.Contains("habitat") || query.Contains("zona") || query.Contains("duerme") || query.Contains("descansa"))
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae los HÁBITATS del monstruo, incluyendo mapa, zona de inicio, áreas de movimiento y, muy importante, la zona de DESCANSO (donde duerme).**");
            jsonExample = @"{
            ""habitats"": [
                { ""map"": ""Bosque Primigenio"", ""startArea"": ""8"", ""areas"": ""5, 8, 11, 12"", ""restArea"": ""14"" }
            ]
        }";
        }
        // Intención: MATERIALES / DROPS
        else if (query.Contains("drop") || query.Contains("consigo") || query.Contains("item") || query.Contains("material") || query.Contains("manto") ||
         !query.Equals(context.monsterName?.ToLower() ?? Guid.NewGuid().ToString()))
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae los MATERIALES, sus métodos de obtención (drops) y los usos en ARMADURAS.**");
            jsonExample = @"{
          ""itemDrops"": [ // Tipo: Array de Objetos
            { 
              ""rank"": ""Rango Maestro"", // Tipo: string
              ""source"": ""Recompensa de investigación"", // Tipo: string
              ""itemName"": ""Manto de Rathalos"", // Tipo: string
              ""percentage"": ""18%"" // Tipo: string
            }
          ],
          ""armors"": [ // Tipo: Array de Objetos
            { 
              ""name"": ""Brazales de Rathian α"", // Tipo: string
              ""type"": ""Brazales"", // Tipo: string
              ""skills"": [ ""Escama de Rathian+ x3"", ""Coraza de Rathian x3"" ] // Tipo: Array de strings
            }
          ]
        }";
        }
        // Intención por defecto: DESCRIPCIÓN
        else
        {
            promptBuilder.AppendLine("\n**INSTRUCCIÓN PRINCIPAL: Extrae la DESCRIPCIÓN general del monstruo.**");
            jsonExample = @"{
          ""description"": ""Un Wyvern volador temido como el 'Rey de los Cielos'..."" // Tipo: string
        }";
        }

        promptBuilder.AppendLine("\n**EJEMPLO DEL FORMATO Y TIPOS DE DATOS JSON ESPERADOS:**");
        promptBuilder.AppendLine(jsonExample);
        promptBuilder.AppendLine("\n---");
        promptBuilder.AppendLine($"**PREGUNTA ORIGINAL (INTENCIÓN):** \"{context.specificQuery}\"");
        promptBuilder.AppendLine("**TEXTO A ANALIZAR:**\n" + text);
        promptBuilder.AppendLine("\n---");
        promptBuilder.AppendLine("**JSON RESULTANTE (siguiendo estrictamente el esquema y los tipos de datos del ejemplo):**");

        string? jsonResponse = await _ollamaService.AskLlava(promptBuilder.ToString());
        if (string.IsNullOrEmpty(jsonResponse)) return null;

        try
        {
            var match = Regex.Match(jsonResponse, @"\{[\s\S]*\}");
            if (match.Success)
            {
                var data = JObject.Parse(match.Value);
                var monsterData = new MonsterData { Name = context.monsterName ?? "Unknown" };

                // --- SECCIÓN DE PARSEO COMPLETA Y PREPARADA PARA EL FUTURO ---

                // Parsear Descripción
                monsterData.Description = data["description"]?.ToString() ?? "";

                // Parsear Drops de Items
                if (data["itemDrops"] is JArray drops)
                {
                    monsterData.ItemDrops = drops.ToObject<List<ItemDropInfo>>() ?? new List<ItemDropInfo>();
                }

                // Parsear Debilidades
                if (data["weaknesses"] is JArray weaknesses)
                {
                    monsterData.Weaknesses = weaknesses.ToObject<List<WeaknessInfo>>() ?? new List<WeaknessInfo>();
                }

                // Parsear Armaduras (¡Nuevo!)
                if (data["armors"] is JArray armors)
                {
                    monsterData.Armors = armors.ToObject<List<ArmorInfo>>() ?? new List<ArmorInfo>();
                }

                // Parsear Misiones (¡Nuevo!)
                if (data["quests"] is JArray quests)
                {
                    monsterData.Quests = quests.ToObject<List<QuestInfo>>() ?? new List<QuestInfo>();
                }

                // Parsear Hábitats (¡Nuevo!)
                if (data["habitats"] is JArray habitats)
                {
                    monsterData.Habitats = habitats.ToObject<List<HabitatInfo>>() ?? new List<HabitatInfo>();
                }

                return monsterData;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agente/Estructura Error]: No se pudo parsear el JSON. Razón: {ex.Message}");
        }
        return null;
    }

    private static async Task<QueryParseResult?> ParseUserQuery(string userInput, string? lastMonster, string? lastGame, string? lastQuery)
    {
        // --- Paso 1: Definimos nuestros ejemplos de JSON como cadenas limpias ---
        string example1_request = "Coraza de Rathian en mhwilds";
        string example1_json = """
      {
        "game": "mhwilds",
        "monsterName": "Rathian",
        "monsterVariant": null,
        "specificQuery": "Coraza de Rathian"
      }
      """;

        string example2_request = "debilidades del rathalos de mhgu";
        string example2_json = """
      {
        "game": "mhgu",
        "monsterName": "Rathalos",
        "monsterVariant": null,
        "specificQuery": "debilidades del rathalos"
      }
      """;

        // --- Paso 2: Construimos el prompt insertando las variables ---
        string prompt = $"""
    {LYRA_CORE_PERSONA}
    ---
    **Tarea de Análisis de Entidades de Monster Hunter:**
    Tu única tarea es analizar la "Petición del Usuario" y generar un bloque de código JSON con la información extraída, siguiendo el formato de los ejemplos.

    **REGLAS CRÍTICAS:**
    1.  **Dominio:** Si la petición NO es sobre Monster Hunter, 'game' debe ser "external_domain".
    2.  **Precisión:** Presta atención a los nombres exactos. "Rathian" es diferente de "Rathalos".
    3.  **monsterName:** Si no se menciona un monstruo, debe ser null.

    **Contexto de Conversación:**
    - Last monster: "{lastMonster ?? "None"}"
    - Last game: "{lastGame ?? "None"}"
    - Last query: "{lastQuery ?? "None"}"

    ---
    **EJEMPLOS DE EXTRACCIÓN PERFECTA:**

    - **Petición:** "{example1_request}"
    - **JSON Correcto:**
      ```json
      {example1_json}
      ```
    
    - **Petición:** "{example2_request}"
    - **JSON Correcto:**
      ```json
      {example2_json}
      ```
    ---

    **Petición del Usuario a Analizar:** "{userInput}"

    **Responde ÚNICAMENTE con el bloque de código JSON final.**
    **JSON:**
    """;

        string? jsonResponse = await _ollamaService.AskLlava(prompt);
        if (jsonResponse == null) return null;

        try
        {
            var match = Regex.Match(jsonResponse, @"```json\s*(\{[\s\S]*?\})\s*```|(\{[\s\S]*?\})");
            if (match.Success)
            {
                string json = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return JsonConvert.DeserializeObject<QueryParseResult>(json);
            }
            Console.WriteLine($"[NLU Error]: No se encontró un bloque JSON válido en la respuesta. Respuesta: {jsonResponse}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NLU Error]: Fallo al procesar el JSON. Razón: {ex.Message}");
            return null;
        }
    }

    private static async Task<UserIntent> ClassifyUserIntent(string userInput)
    {
        // Primero, hacemos una comprobación rápida basada en palabras clave para ahorrar llamadas al LLM.
        string lowerInput = userInput.ToLower();
        if (lowerInput.StartsWith("lyra busca") || lowerInput.Contains("debilidad") || lowerInput.Contains("consigo"))
            return UserIntent.KnowledgeQuery;
        if (lowerInput.StartsWith("juega") || lowerInput.StartsWith("lyra cuenta") || lowerInput.StartsWith("lyra una adivinanza"))
            return UserIntent.ActionCommand;
        if (lowerInput.Contains("recuerdas") || lowerInput.Contains("te acuerdas"))
            return UserIntent.MemoryRecall;

        // Si no es un comando obvio, usamos el LLM para clasificar.
        string prompt = $@"Eres un clasificador de intenciones para una IA llamada Lyra.
    Analiza la petición del usuario y clasifícala en UNA de las siguientes categorías:

    - 'GeneralConversation': Saludos, preguntas sobre cómo está la IA, charla trivial, agradecimientos, peticiones de ayuda genéricas.
    - 'KnowledgeQuery': Preguntas específicas sobre el universo de Monster Hunter (monstruos, ítems, mapas, etc.).
    - 'MemoryRecall': Preguntas sobre si la IA recuerda conversaciones pasadas.

    Petición del Usuario: ""{userInput}""

    Responde SOLO con la categoría (ej: GeneralConversation).";

        string? classification = await _ollamaService.AskLlava(prompt);

        return classification?.Trim() switch
        {
            "GeneralConversation" => UserIntent.GeneralConversation,
            "KnowledgeQuery" => UserIntent.KnowledgeQuery,
            "MemoryRecall" => UserIntent.MemoryRecall,
            _ => UserIntent.Unknown
        };
    }

    private static string NormalizeGameIdentifier(string rawGameIdentifier)
    {
        if (string.IsNullOrEmpty(rawGameIdentifier)) return "";

        string normalized = rawGameIdentifier.ToLower().Trim();

        // Eliminar palabras "ruidosas"
        normalized = normalized.Replace("kiranico", "")
                               .Replace("monster hunter", "")
                               .Replace(":", "")
                               .Replace(" ", ""); // Eliminar todos los espacios

        // Puedes añadir más reglas de normalización si es necesario
        // Ejemplo: .Replace("generationsultimate", "mhgu")

        return normalized.Trim();
    }
    #endregion

    #region FORMATEO DE RESPUESTAS Y ACTIVIDADES
    // ===================================================================

    private static async Task<string> FormatResponse(MonsterData data, string userInput, string? specificQuery, OllamaService ollama)
    {
        if (string.IsNullOrEmpty(specificQuery) || specificQuery.Equals(data.Name, StringComparison.OrdinalIgnoreCase))
        {
            return await BuildFullReport(data, ollama);
        }

        string query = specificQuery.ToLower();

        // Intención 1: El usuario pregunta por DEBILIDADES.
        if (query.Contains("debil") || query.Contains("weakness"))
        {
            if (data.Weaknesses.Any())
            {
                string weaknessAnswer = BuildDirectAnswerForWeakness(data);
                return $"¡Claro! Según mis registros, las debilidades del {data.Name} son:\n{weaknessAnswer}";
            }
            else
            {
                return "[LEARNING_REQUIRED]";
            }
        }

        // Intención 2: El usuario pregunta por DROPS / ITEMS.
        var itemAnswer = BuildDirectAnswerForItem(specificQuery, data);
        if (!string.IsNullOrEmpty(itemAnswer))
        {
            return $"¡Por supuesto! Aquí tienes los detalles sobre cómo obtener '{specificQuery}':\n{itemAnswer}";
        }

        if (query.Contains("drop") || query.Contains("consigo") || query.Contains("item") || query.Contains("material"))
        {
            return "[LEARNING_REQUIRED]";
        }

        // Fallback: Pregunta compleja. Usar LLM.
        Console.WriteLine("[Lyra]: Pregunta compleja. Usando LLM para interpretar el conocimiento local...");
        // string fullContextJson = JsonConvert.SerializeObject(data, Formatting.Indented);
        string relevantContextJson = BuildContextForQuery(data, specificQuery!);

        // Usamos sintaxis de string interpolado multilínea moderna para evitar errores de escape
        string prompt = $$"""
                {{LYRA_CORE_PERSONA}}
                ---
                *Tarea:** Eres Lyra. Tu tarea es responder a la PREGUNTA DEL USUARIO utilizando ÚNICAMENTE los datos del JSON de conocimiento proporcionado.

                **PREGUNTA DEL USUARIO:** ""{{userInput}}""

                **CONOCIMIENTO DISPONIBLE (JSON Relevante):**
                ```
                {{relevantContextJson}}
                ```

                INSTRUCCIONES CRÍTICAS PARA TU RESPUESTA:

                    Si los datos en el JSON responden directamente a la pregunta, formula una respuesta clara y detallada con tu personalidad.

                    Si los datos son insuficientes o no responden a la pregunta, DEBES reconocerlo amablemente. Explica lo que SÍ sabes y sugiere proactivamente el siguiente paso.

                    NUNCA digas 'El JSON no contiene...'. En lugar de eso, di algo como: 'Mis registros indican que [Material] se obtiene de [Fuente], pero no tengo los porcentajes exactos. Si quieres, puedo refinar la búsqueda.'

                RESPUESTA FINAL (en español, con tu personalidad):
                """;

        string? response = await ollama.AskLlava(prompt);
        return response?.Trim() ?? "No he podido formular una respuesta para tu pregunta.";
    }

    /// <summary>
    /// Crea un fragmento de JSON dinámico y relevante a partir de un objeto MonsterData
    /// para usarlo como contexto en un prompt, evitando exceder los límites de caracteres.
    /// </summary>
    private static string BuildContextForQuery(MonsterData data, string query)
    {
        var relevantData = new JObject();
        relevantData["Name"] = data.Name;
        query = query.ToLower();

        // Si la pregunta es sobre debilidades...
        if (query.Contains("debil") || query.Contains("weakness") || query.Contains("daño") || query.Contains("elemento"))
        {
            relevantData["Weaknesses"] = JArray.FromObject(data.Weaknesses);
        }
        // Si la pregunta es sobre drops o materiales...
        else if (query.Contains("drop") || query.Contains("consigo") || query.Contains("item") || query.Contains("material") || query.Contains("escala"))
        {
            relevantData["ItemDrops"] = JArray.FromObject(data.ItemDrops);
        }
        // Si la pregunta es sobre armaduras...
        else if (query.Contains("armor") || query.Contains("armadura") || query.Contains("set"))
        {
            relevantData["Armors"] = JArray.FromObject(data.Armors);
        }
        // Si la pregunta es sobre hábitats...
        else if (query.Contains("mapa") || query.Contains("habitat") || query.Contains("duerme"))
        {
            relevantData["Habitats"] = JArray.FromObject(data.Habitats);
        }
        // Si no estamos seguros (fallback), incluimos lo más común.
        else
        {
            relevantData["Description"] = data.Description;
            relevantData["Weaknesses"] = JArray.FromObject(data.Weaknesses);
            relevantData["ItemDrops"] = JArray.FromObject(data.ItemDrops.Take(5)); // Solo los primeros 5 drops para ahorrar espacio
        }

        return relevantData.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private static async Task<string> BuildFullReport(MonsterData data, OllamaService ollama)
    {
        var builder = new StringBuilder();

        string styledDescription = data.Description;
        if (!string.IsNullOrEmpty(data.Description))
        {
            Console.WriteLine("[Translator]: Pidiendo al LLM que estilice la descripción...");
            string descPrompt = $"You are Lyra, a serene AI. Rephrase the following monster description into a single, beautiful paragraph in Spanish, maintaining your persona.\n\nDESCRIPTION: \"{data.Description}\"\n\nLYRA'S RESPONSE (single paragraph in Spanish):";
            string? response = await ollama.AskLlava(descPrompt);
            if (!string.IsNullOrEmpty(response))
            {
                styledDescription = response.Trim();
            }
        }

        builder.AppendLine($"\nLyra: Por supuesto. Esto es lo que he registrado sobre el {data.Name}:\n");
        builder.AppendLine($"\"{styledDescription}\"");

        if (data.Habitats.Any())
        {
            builder.AppendLine("\n\n--- Hábitat y Zonas de Descanso ---");
            foreach (var habitat in data.Habitats)
            {
                builder.AppendLine($"  - Mapa: {habitat.Map,-15} | Inicio: {habitat.StartArea,-3} | Zonas: {habitat.Areas,-15} | Descanso: {habitat.RestArea}");
            }
        }

        if (data.Weaknesses.Any())
        {
            builder.AppendLine("\n\n--- Debilidades Físicas y Elementales ---");
            foreach (var part in data.Weaknesses)
            {
                var mainWeakness = part.ElementalValues
                    .Where(kv => new[] { "fire", "water", "ice", "thunder", "dragon" }.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault();
                builder.AppendLine($"  - {part.BodyPart,-20} | Debilidad Principal: {mainWeakness.Key} ({mainWeakness.Value})");
            }
        }

        builder.AppendLine("\n\n--- Estadísticas de Combate ---");
        AppendStatDetailsInSpanish(builder, "Puntos de Vida (HP)", data.HPRanges);
        AppendStatDetailsInSpanish(builder, "Umbral de Cojeo", data.LimpingThresholds);
        AppendStatDetailsInSpanish(builder, "Umbral de Captura", data.CaptureThresholds);
        AppendStatDetailsInSpanish(builder, "Límites de Aturdimiento", data.StaggerLimits);

        if (data.AbnormalStatuses.Any())
        {
            builder.AppendLine("\n  --- Tolerancia a Estados Alterados ---");
            foreach (var status in data.AbnormalStatuses.OrderBy(s => s.StatusName))
            {
                builder.AppendLine($"    - {status.StatusName,-10} | Inicial: {status.Initial,-5} | Aumento: {status.Increase,-5} | Máx: {status.Max,-5} | Duración: {status.Duration}s");
            }
        }

        if (data.ItemDrops.Any())
        {
            builder.AppendLine("\n\n--- Materiales y Recompensas ---");
            foreach (var group in data.ItemDrops.GroupBy(d => d.Rank))
            {
                builder.AppendLine($"\n  --- Rango {group.Key} ---");
                foreach (var item in group.OrderBy(i => i.Source))
                {
                    builder.AppendLine($"    - {item.Source,-20} | {item.ItemName,-30} | {item.Percentage}");
                }
            }
        }

        return builder.ToString();
    }

    #endregion

    #region FUNCIONES DE AYUDA PARA FORMATEAR Y VTUBER
    // ===================================================================

    /// <summary>
    /// Función centralizada para que Lyra hable.
    /// Imprime en la consola y activa el Text-to-Speech.
    /// </summary>
    private static async Task LyraSpeak(string message, string source = "Terminal")
    {
        // Limpiamos el prefijo si ya existe para evitar duplicados.
        string cleanMessage = message.StartsWith("Lyra:") ? message.Substring(6).Trim() : message;

        string fullMessage = $"Lyra ({source}): {cleanMessage}";
        Console.WriteLine(fullMessage);

        // Solo hablamos si el mensaje no es una señal interna.
        if (cleanMessage != "[LEARNING_REQUIRED]")
        {
            // Ahora es una llamada asíncrona que esperamos a que termine.
            await _ttsService.SpeakAsync(cleanMessage);
        }
    }

    /// <summary>
    /// Construye una respuesta formateada para la intención de DEBILIDADES.
    /// </summary>
    private static string BuildDirectAnswerForWeakness(MonsterData data)
    {
        var builder = new StringBuilder();
        foreach (var part in data.Weaknesses)
        {
            var mainWeakness = part.ElementalValues
                .Where(kv => new[] { "fire", "water", "ice", "thunder", "dragon" }.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .FirstOrDefault();

            if (mainWeakness.Key != null)
            {
                builder.AppendLine($"  - {part.BodyPart,-20} | Debilidad Principal: {mainWeakness.Key} ({mainWeakness.Value})");
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Construye una respuesta formateada para la intención de DROPS DE ITEMS si los datos son significativos.
    /// </summary>
    private static string BuildDirectAnswerForItem(string query, MonsterData data)
    {
        var matchingDrops = data.ItemDrops
            .Where(d => !string.IsNullOrEmpty(d.ItemName) && d.ItemName.ToLower().Contains(query.ToLower()))
            .ToList();

        if (!matchingDrops.Any()) return "";

        var answerBuilder = new StringBuilder();
        bool hasMeaningfulData = false;

        foreach (var group in matchingDrops.GroupBy(d => d.Rank))
        {
            var rankHeader = $"\n  --- Rango {group.Key ?? "Desconocido"} ---";
            var rankContent = new StringBuilder();

            foreach (var item in group)
            {
                bool isSourceMeaningful = !string.IsNullOrEmpty(item.Source) && !item.Source.ToLower().Contains("caza");
                bool isPercentageMeaningful = !string.IsNullOrEmpty(item.Percentage) && !item.Percentage.Contains("100") && !item.Percentage.ToLower().Contains("n/a");

                if (isSourceMeaningful || isPercentageMeaningful)
                {
                    rankContent.AppendLine($"    - Fuente: {item.Source,-25} | Probabilidad: {item.Percentage}");
                    hasMeaningfulData = true;
                }
            }

            if (rankContent.Length > 0)
            {
                answerBuilder.Append(rankHeader);
                answerBuilder.Append(rankContent);
            }
        }

        return hasMeaningfulData ? answerBuilder.ToString() : "";
    }

    /// <summary>
    /// Formatea los detalles de estadísticas (HP, Cojeo, etc.) para los informes completos.
    /// </summary>
    private static void AppendStatDetailsInSpanish(StringBuilder builder, string title, List<StatDetail> details)
    {
        if (details.Any())
        {
            builder.AppendLine($"\n  --- {title} ---");
            foreach (var detail in details)
            {
                builder.AppendLine($"    - {detail.Label,-12}: {detail.Value}");
            }
        }
    }

    static async Task GuessTheMonsterGameAsync(string juego)
    {
        // Activamos el interruptor al entrar al juego
        _isGameActive = true;
        try // Usamos un bloque try para manejar el juego
        {
            const string juegoConAudio = "mh4u";

            string path = "Biblioteca_cache.json";
            if (!File.Exists(path))
            {
                await LyraSpeak("Mis archivos de crónicas no están disponibles. No puedo iniciar el juego.", "Lyra");
                return;
            }

            var rawJson = await File.ReadAllTextAsync(path);
            var biblioteca = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, MonsterData>>>(rawJson);

            if (biblioteca == null || !biblioteca.ContainsKey(juegoConAudio))
            {
                await LyraSpeak($"No tengo información sobre los monstruos de {juegoConAudio} todavía.", "Lyra");
                return;
            }

            var monstruosConAudio = biblioteca[juegoConAudio].Values
                .Where(m => !string.IsNullOrEmpty(m.RoarAudioUrl))
                .ToList();

            if (!monstruosConAudio.Any())
            {
                await LyraSpeak($"Vaya, parece que no he catalogado los rugidos de los monstruos para {juegoConAudio}.", "Lyra");
                return;
            }

            var random = new Random();
            var monstruoElegido = monstruosConAudio[random.Next(monstruosConAudio.Count)];

            string nombreMonstruo = monstruoElegido.Name;
            string? roarUrl = monstruoElegido.RoarAudioUrl;

            string[] plantillas =
            {
            $"Agudizad el oído, cazadores... Un eco resuena en la distancia. ¿Podéis nombrar a la bestia?",
            $"Escuchad... El grito de guerra de una criatura legendaria. ¿Quién osa desafiar a la Quinta Flota?",
            $"Silencio... Prestad atención. Este sonido es inconfundible para un cazador veterano. ¿De quién se trata?",
            $"Un rugido que hiela la sangre. Solo los más valientes lo han escuchado y vivido para contarlo. ¿Identificáis al monstruo?"
        };

            var historia = plantillas[random.Next(plantillas.Length)];
            await LyraSpeak(historia, "Lyra");

            Console.WriteLine($"[Juego]: Reproduciendo rugido de '{nombreMonstruo}' desde {roarUrl}");
            await _audioService.PlaySoundFromUrl(roarUrl!);

            await LyraSpeak("Tenéis 30 segundos para adivinar... Si queréis volver a escucharlo, decid 'repite' o 'no escuché'. ¡Que comience el desafío!", "Lyra");

            var tiempoLimite = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();
            string? ganador = null;

            var cooldownRepeticion = TimeSpan.FromSeconds(10);
            DateTime proximaRepeticionPermitida = DateTime.MinValue;

            // Bucle del juego, que ahora tiene control exclusivo de la cola de chat.
            while (stopwatch.Elapsed < tiempoLimite && string.IsNullOrEmpty(ganador))
            {
                if (_chatMessageQueue.TryDequeue(out var msg))
                {
                    string lowerMessage = msg.Message.Trim().ToLower();
                    bool esPeticionRepetir = lowerMessage.Contains("repite") || lowerMessage.Contains("otra vez") || lowerMessage.Contains("no escuché");

                    if (esPeticionRepetir)
                    {
                        if (DateTime.UtcNow >= proximaRepeticionPermitida)
                        {
                            await LyraSpeak("¡Claro! Escuchad con atención una vez más.", "Lyra");
                            _ = _audioService.PlaySoundFromUrl(roarUrl!);
                            proximaRepeticionPermitida = DateTime.UtcNow + cooldownRepeticion;
                        }
                        else
                        {
                            // Mensaje opcional para evitar que piensen que no funciona
                            Console.WriteLine("[Juego]: Petición de repetir ignorada por cooldown.");
                        }
                    }
                    else if (lowerMessage.Equals(nombreMonstruo, StringComparison.OrdinalIgnoreCase))
                    {
                        ganador = msg.Username;
                        Console.WriteLine($"[Juego]: ¡'{ganador}' ha adivinado correctamente!");
                    }
                }
                await Task.Delay(200);
            }

            stopwatch.Stop();

            if (!string.IsNullOrEmpty(ganador))
            {
                await LyraSpeak($"¡Impresionante! El instinto de {ganador} es certero. La respuesta correcta era, en efecto, el {nombreMonstruo}. ¡Felicidades!", "Lyra");
            }
            else
            {
                await LyraSpeak($"¡El tiempo ha terminado! Un rugido difícil, sin duda. La criatura que escuchamos era el temible {nombreMonstruo}. ¡Mejor suerte la próxima vez, cazadores!", "Lyra");
            }
        }
        finally
        {
            // Este bloque se ejecuta SIEMPRE al salir del 'try',
            // ya sea porque el juego terminó o por un error.
            // Esto garantiza que el procesador principal vuelva a funcionar.
            _isGameActive = false;
            Console.WriteLine("[Juego]: Juego finalizado. El procesador de chat principal ha sido reactivado.");
        }
    }

    /// <summary>
    /// Maneja el comando para que Lyra cuente una historia basada en su conocimiento.
    /// </summary>
    private static async Task TellMonsterStory()
    {
        Console.WriteLine("Lyra: Entendido, Director. Buscaré una crónica interesante en mis archivos...");

        var randomMonster = _knowledgeService._knowledgeCache.Values
            .SelectMany(gameDict => gameDict.Values)
            .Where(m => !string.IsNullOrEmpty(m.Description))
            .OrderBy(x => Guid.NewGuid())
            .FirstOrDefault();

        if (randomMonster == null)
        {
            Console.WriteLine("Lyra: Vaya, mis archivos aún están un poco vacíos. Necesito aprender más para poder contar historias.");
            return;
        }

        string prompt = $@"{LYRA_CORE_PERSONA}
---
Tu tarea es contar una breve historia o un dato curioso y fascinante sobre el siguiente monstruo, usando la descripción proporcionada como base. Sé dramática y erudita.

MONSTRUO: {randomMonster.Name}
DESCRIPCIÓN BASE: ""{randomMonster.Description}""

TU HISTORIA (en español, para la Quinta Flota):";

        string? story = await _ollamaService.AskLlava(prompt);
        Console.WriteLine($"Lyra (Relato): {story ?? "Se me ha escapado la idea de la mente, disculpen."}");
    }

    /// <summary>
    /// Maneja el comando para que Lyra cree una adivinanza sobre un monstruo.
    /// </summary>
    private static async Task MonsterRiddle()
    {
        Console.WriteLine("Lyra: ¡Una adivinanza! Preparando un enigma para la Quinta Flota...");

        var randomMonster = _knowledgeService._knowledgeCache.Values
            .SelectMany(gameDict => gameDict.Values)
            .Where(m => !string.IsNullOrEmpty(m.Description))
            .OrderBy(x => Guid.NewGuid())
            .FirstOrDefault();

        if (randomMonster == null)
        {
            Console.WriteLine("Lyra: Necesito más conocimiento para crear buenos enigmas.");
            return;
        }

        string prompt = $@"{LYRA_CORE_PERSONA}
---
Tu tarea es crear una adivinanza de 2 o 3 líneas sobre el siguiente monstruo, basándote en su descripción. La adivinanza debe ser misteriosa pero justa. No uses su nombre.

MONSTRUO: {randomMonster.Name}
DESCRIPCIÓN BASE: ""{randomMonster.Description}""

TU ADIVINANZA (en español):";

        string? riddle = await _ollamaService.AskLlava(prompt);
        Console.WriteLine($"Lyra (Enigma): {riddle}");
        Console.WriteLine("Lyra: Tendrán 15 segundos para adivinar... ¡Tic, tac!");

        await Task.Delay(15000);

        Console.WriteLine($"Lyra: ¡El tiempo ha terminado! La respuesta era... ¡El {randomMonster.Name}!");
    }

    #endregion

    #region FUNCIONES DE AYUDA Y CONTEXTO
    // ===================================================================

    private static void UpdateContext(QueryParseResult parseResult)
    {
        _lastGameContext = parseResult.game;
        _lastMonsterContext = parseResult.monsterName;
        _lastSpecificQueryContext = parseResult.specificQuery;
    }

    #endregion
}