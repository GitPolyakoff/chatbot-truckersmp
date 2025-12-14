using GroqApiLibrary;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WindowsInput;
using WindowsInput.Native;

namespace ChatBot
{
    class Program
    {
        private static GroqApiClient _gptClient;
        private static List<JObject> _gptHistory = new List<JObject>();
        private const string TRUCKERSMP_API_BASE = "https://api.truckersmp.com/v2";
        private static string ChatLogPath = "";
        private static string[] windowTitles = new[] { "Euro Truck Simulator 2 Multiplayer", "American Truck Simulator" };
        private static readonly InputSimulator sim = new InputSimulator();
        private static int tailSleepMs = 50;
        private static BlockingCollection<string> sendQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private const int MESSAGES_PER_MIN = 60;
        private static Queue<DateTime> sentTimestamps = new Queue<DateTime>();
        private static Regex chatLineRegex = new Regex(@"\[(?<channel>.+?)\]\s+\[(?<time>\d{2}:\d{2}:\d{2})\]\s+(?<nick>.+?)\s+\((?<id>\d+)\):\s+(?<message>.+)", RegexOptions.Compiled);
        private static string GroqApiKey = "";
        private static string BannedWordsPath = "";
        private static readonly string ConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "bot_config.txt");

        private static string DetectChatLogPath()
        {
            Console.WriteLine("🔍 Searching for chat log file automatically...");
            
            var possiblePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ETS2MP", "logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ATS2MP", "logs"),
                @"C:\Users\" + Environment.UserName + @"\Documents\ETS2MP\logs",
                @"C:\Users\" + Environment.UserName + @"\Documents\ATS2MP\logs",
            };

            foreach (var logDir in possiblePaths)
            {
                if (Directory.Exists(logDir))
                {                    
                    var today = DateTime.Now;
                    var fileName = $"chat_{today:yyyy_MM_dd}_log.txt";
                    var filePath = Path.Combine(logDir, fileName);
                    
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"✅ Chat log file found: {filePath}");
                        return filePath;
                    }

                    try
                    {
                        var chatFiles = Directory.GetFiles(logDir, "chat_*_log.txt")
                            .OrderByDescending(f => File.GetLastWriteTime(f))
                            .ToArray();

                        if (chatFiles.Length > 0)
                        {
                            var latestFile = chatFiles[0];
                            Console.WriteLine($"📄 Latest chat log file found: {latestFile}");
                            return latestFile;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Error reading log directory: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("❌ Chat log file not found automatically.");
            return null;
        }

        private static string GetChatLogPathFromUser()
        {
            Console.WriteLine("\n📝 Chat log file not found!");
            Console.WriteLine("Please enter the full path to your chat log file:");
            Console.WriteLine("Example: C:\\Users\\USER\\Documents\\ETS2MP\\logs\\chat_2025_01_15_log.txt");
            Console.Write("File path: ");
            
            string userPath = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(userPath))
            {
                Console.WriteLine("❌ Invalid file path!");
                return null;
            }

            if (!File.Exists(userPath))
            {
                Console.WriteLine($"❌ File not found: {userPath}");
                return null;
            }

            Console.WriteLine($"✅ Chat log file verified: {userPath}");
            return userPath;
        }

        private static async Task<bool> CheckApiConnectivity()
        {
            Console.WriteLine("🔍 Checking API connectivity...");
            
            bool groqConnected = false;
            
            try
            {
                // Test Groq API
                string testPrompt = "Hello";
                string response = await GenerateGptResponse(testPrompt);
                
                if (!string.IsNullOrEmpty(response) && response != "(no response)")
                {
                    Console.WriteLine("✅ Groq API: Connected");
                    groqConnected = true;
                }
                else
                {
                    Console.WriteLine("❌ Groq API: Failed to get response");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Groq API: Error - {ex.Message}");
            }

            Console.WriteLine("--------------------------------");
            return groqConnected;
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("🤖 Polyakoff ChatBot Starting...");
            Console.WriteLine("--------------------------------");

            LoadOrRequestConfig();
            _gptClient = new GroqApiClient(GroqApiKey);

            bool apiConnected = CheckApiConnectivity().GetAwaiter().GetResult();
            if (!apiConnected)
            {
                Console.WriteLine("⚠️ Warning: All API connections failed. Some features may not work.");
            }

            ChatLogPath = DetectChatLogPath();
            
            if (string.IsNullOrEmpty(ChatLogPath))
            {
                while (string.IsNullOrEmpty(ChatLogPath))
                {
                    ChatLogPath = GetChatLogPathFromUser();
                    if (string.IsNullOrEmpty(ChatLogPath))
                    {
                        Console.WriteLine("⚠️ Press Enter to try again...");
                        Console.ReadLine();
                    }
                }
            }

            Console.WriteLine("--------------------------------");
            Console.WriteLine("✅ ChatBot Started!");
            Console.WriteLine("📊 Monitoring chat...");

            Task.Factory.StartNew(() => SendWorker(), TaskCreationOptions.LongRunning);

            try
            {
                TailFileLoop(ChatLogPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error: " + ex);
            }
        }

        private static void TailFileLoop(string filePath)
        {
            while (!File.Exists(filePath))
            {
                Console.WriteLine($"Log file not found: {filePath}. Waiting 5 seconds...");
                Thread.Sleep(5000);
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            sr.BaseStream.Seek(0, SeekOrigin.End);
            sr.DiscardBufferedData();

            while (true)
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                    HandleLogLine(line);
                Thread.Sleep(tailSleepMs);
            }
        }

        private static void HandleLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var m = chatLineRegex.Match(line);
            if (!m.Success) return;

            string nick = m.Groups["nick"].Value;
            string id = m.Groups["id"].Value;
            string message = m.Groups["message"].Value.Trim();

            if (message.StartsWith("!"))
                _ = Task.Run(() => HandleCommand(nick, id, message));

        }

        private static async Task HandleCommand(string nick, string id, string message)
        {
            var parts = message.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "!help":
                    EnqueueBotMessage($"@{id} 🤖 Hello! I am PolyakoffBot v3. Commands: !help, !gpt <question>, !serverinfo, !players, !version, !socials, !events.");
                    break;

                case "!serverinfo":
                    var servers = GetServersAsync().GetAwaiter().GetResult();
                    if (servers != null && servers.Any())
                    {
                        var s = servers.First();
                        EnqueueBotMessage($"@{id} 🖥️ Server: {s.Name} | {s.Ip}:{s.Port} | Players: {s.Players}/{s.MaxPlayers} | Queue: {s.Queue}");
                    }
                    else
                    {
                        EnqueueBotMessage($"@{id} ❌ Could not fetch server info.");
                    }
                    break;

                case "!players":
                    var srv = GetServersAsync().GetAwaiter().GetResult();
                    if (srv != null)
                    {
                        var totalPlayers = srv.Sum(x => x.Players);
                        EnqueueBotMessage($"@{id} 👥 Total players online (all servers): {totalPlayers}");
                    }
                    else EnqueueBotMessage($"@{id} ❌ Could not fetch players.");
                    break;

                case "!version":
                    var ver = GetGameVersionAsync().GetAwaiter().GetResult();
                    if (ver != null)
                    {
                        EnqueueBotMessage($"@{id} 📦 Supported ETS2: {ver.SupportedGameVersion} | ATS: {ver.SupportedAtsGameVersion}");
                    }
                    else EnqueueBotMessage($"@{id} ❌ Could not fetch game versions.");
                    break;

                case "!socials":
                    EnqueueBotMessage($"@{id} 🔗 Our discord nickname: polyakoff & lrnsxgod | Github: github.com/GitPolyakoff & github.com/lrnsxdev");
                    break;

                case "!events":
                    var events = GetEventsAsync().GetAwaiter().GetResult();
                    if (events != null && events.Any())
                    {
                        var e = events.Take(2).ToList();
                        EnqueueBotMessage($"@{id} 📅 Events now/soon: {string.Join(" | ", e.Select(x => x.Name + " at " + x.StartDate.ToString("yyyy-MM-dd")))}");
                    }
                    else EnqueueBotMessage($"@{id} ℹ️ No events found or could not fetch events.");
                    break;

                case "!gpt":
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        EnqueueBotMessage($"@{id} 🤖 Usage: !gpt <your message>");
                    }
                    else
                    {

                        string[] bannedWords = File.Exists(BannedWordsPath)
                            ? File.ReadAllLines(BannedWordsPath)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Select(line => line.Trim().ToLower())
                                .ToArray()
                            : Array.Empty<string>();

                        if (IsBannedContent(arg, bannedWords))
                        {
                            Console.WriteLine($"⚠️ Banned word detected in user command from @{id}.");
                            EnqueueBotMessage($"@{id} 🤖 I will not answer this question.");
                            break;
                        }

                        string reply = await GenerateGptResponse(arg);
                        EnqueueBotMessage($"@{id} 🤖 GPT: {reply}");
                    }
                    break;

                default:
                    EnqueueBotMessage($"@{id} ❓ Unknown command. Type !help for details.");
                    break;
            }
        }


        private static void EnqueueBotMessage(string message)
        {
            lock (sentTimestamps)
            {
                while (sentTimestamps.Count > 0 && (DateTime.UtcNow - sentTimestamps.Peek()).TotalSeconds > 60)
                    sentTimestamps.Dequeue();

                if (sentTimestamps.Count >= MESSAGES_PER_MIN)
                    return;

                sentTimestamps.Enqueue(DateTime.UtcNow);
            }

            sendQueue.Add(message);
            Console.WriteLine("Enqueued bot message: " + message);
        }

        private static void SendWorker()
        {
            foreach (var msg in sendQueue.GetConsumingEnumerable())
            {
                SendChatMessageToGame(msg);
                Thread.Sleep(500);
            }
        }

        private static async Task<List<ServerDto>> GetServersAsync()
        {
            try
            {
                var client = new RestClient(TRUCKERSMP_API_BASE);
                var request = new RestRequest("/servers", Method.Get);
                var resp = await client.ExecuteAsync(request);
                if (resp.IsSuccessful)
                {
                    var obj = JsonConvert.DeserializeObject<ApiServersResponse>(resp.Content);
                    return obj?.Response;
                }
            }
            catch (Exception ex) { Console.WriteLine("GetServers error: " + ex.Message); }
            return null;
        }

        private static async Task<GameVersionDto> GetGameVersionAsync()
        {
            try
            {
                var client = new RestClient(TRUCKERSMP_API_BASE);
                var request = new RestRequest("/version", Method.Get);
                var resp = await client.ExecuteAsync(request);
                if (resp.IsSuccessful)
                {
                    var obj = JsonConvert.DeserializeObject<GameVersionResponse>(resp.Content);
                    return obj;
                }
            }
            catch (Exception ex) { Console.WriteLine("GetGameVersion error: " + ex.Message); }
            return null;
        }

        private static async Task<List<EventDto>> GetEventsAsync()
        {
            try
            {
                var client = new RestClient(TRUCKERSMP_API_BASE);
                var request = new RestRequest("/events", Method.Get);
                var resp = await client.ExecuteAsync(request);
                if (resp.IsSuccessful)
                {
                    var obj = JsonConvert.DeserializeObject<EventsResponse>(resp.Content);
                    var list = new List<EventDto>();
                    if (obj?.Response?.Now != null) list.AddRange(obj.Response.Now);
                    if (obj?.Response?.Today != null) list.AddRange(obj.Response.Today);
                    if (obj?.Response?.Upcoming != null) list.AddRange(obj.Response.Upcoming);
                    return list;
                }
            }
            catch (Exception ex) { Console.WriteLine("GetEvents error: " + ex.Message); }
            return null;
        }


        private static async Task<T?> SafeApiCallAsync<T>(RestClient client, RestRequest request)
        {
            try
            {
                var resp = await client.ExecuteAsync(request);
                if (resp == null || !resp.IsSuccessful) return default;
                return JsonConvert.DeserializeObject<T>(resp.Content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Exception during API call: " + ex.Message);
                return default;
            }
        }

        private static void LoadOrRequestConfig()
        {
            if (File.Exists(ConfigFilePath))
            {
                var lines = File.ReadAllLines(ConfigFilePath);

                foreach (var line in lines)
                {
                    if (line.StartsWith("GROQ_API_KEY=", StringComparison.OrdinalIgnoreCase))
                        GroqApiKey = line.Substring("GROQ_API_KEY=".Length).Trim();

                    if (line.StartsWith("BANNED_WORDS_PATH=", StringComparison.OrdinalIgnoreCase))
                        BannedWordsPath = line.Substring("BANNED_WORDS_PATH=".Length).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(GroqApiKey))
            {
                Console.Write("🔑 Enter GROQ API key (create it at https://console.groq.com/keys): ");
                GroqApiKey = Console.ReadLine()?.Trim();
            }

            while (string.IsNullOrWhiteSpace(BannedWordsPath) || !File.Exists(BannedWordsPath))
            {
                Console.Write("📄 Enter FULL path to banned_words.txt (example: C:\\Users\\YourName\\Desktop\\banned_words.txt): ");
                BannedWordsPath = Console.ReadLine()?.Trim();

                if (!File.Exists(BannedWordsPath))
                    Console.WriteLine("❌ File not found. Try again.");
            }

            File.WriteAllLines(ConfigFilePath, new[]
            {
                $"GROQ_API_KEY={GroqApiKey}",
                $"BANNED_WORDS_PATH={BannedWordsPath}"
            });

            Console.WriteLine($"✅ Config loaded (saved to {ConfigFilePath})");
        }


        private static async Task<string> GenerateGptResponse(string userInput)
        {
            string hiddenInstruction = " (Respond in the same language as the question, very concise: 1 short sentence only." +
                " NO profanity, swearing, or inappropriate language;" +
                " NO insults, threats, or discrimination;" +
                " NO political discussions or extreme views;" +
                " NO references to drugs, illegal substances, or sexual content;" +
                " NO N-Words;" +
                " NO personal information sharing;" +
                " Keep responses friendly, helpful, and appropriate for all ages;" +
                " If asked about inappropriate topics, politely decline and suggest appropriate alternatives.)";

            _gptHistory.Add(new JObject
            {
                ["role"] = "user",
                ["content"] = userInput + hiddenInstruction
            });

            int maxMessagesSize = 10;
            if (_gptHistory.Count > maxMessagesSize)
                _gptHistory.RemoveRange(0, _gptHistory.Count - maxMessagesSize);

            JObject request = new JObject
            {
                ["model"] = "llama-3.1-8b-instant",
                ["messages"] = new JArray(_gptHistory)
            };

            var response = await _gptClient.CreateChatCompletionAsync(request);
            string? aiResponse = response?["choices"]?[0]?["message"]?["content"]?.ToString();

            string[] bannedWords = File.Exists(BannedWordsPath)
                ? File.ReadAllLines(BannedWordsPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim().ToLower())
                    .ToArray()
                : Array.Empty<string>();


            if (!string.IsNullOrEmpty(aiResponse) && IsBannedContent(aiResponse, bannedWords))
            {
                Console.WriteLine("⚠️ Banned word detected in GPT response.");
                aiResponse = "I will not answer this question.";
            }

            if (!string.IsNullOrEmpty(aiResponse))
            {
                _gptHistory.Add(new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = aiResponse
                });
            }

            return aiResponse ?? "(no response)";
        }

        private static bool IsBannedContent(string text, string[] bannedWords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = Regex.Replace(text.ToLower(), @"[!@#$%^&*()\[\]{}|\\;:'"",.<>?/0-9_\-\s]+", "", RegexOptions.Compiled);

            foreach (string banned in bannedWords)
            {
                if (string.IsNullOrWhiteSpace(banned)) continue;

                string pattern = string.Join(@"[\W_0-9]*", banned.Select(c => Regex.Escape(c.ToString())));
                if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool SendChatMessageToGame(string message)
        {
            try
            {
                IntPtr hWnd = IntPtr.Zero;
                foreach (var title in windowTitles)
                {
                    hWnd = FindWindowByCaption(IntPtr.Zero, title);
                    if (hWnd != IntPtr.Zero) break;
                }

                if (hWnd == IntPtr.Zero)
                {
                    var processes = new[] { "eurotrucks2", "amtrucks" }
                        .SelectMany(name => System.Diagnostics.Process.GetProcessesByName(name))
                        .Where(p => p.MainWindowHandle != IntPtr.Zero);

                    var proc = processes.FirstOrDefault();
                    if (proc != null) hWnd = proc.MainWindowHandle;
                }

                if (hWnd == IntPtr.Zero) return false;

                SetForegroundWindow(hWnd);
                Thread.Sleep(500);

                SetClipboardText(message);
                Thread.Sleep(600);

                sim.Keyboard.KeyPress(VirtualKeyCode.VK_Y);
                Thread.Sleep(500);

                sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);
                Thread.Sleep(300);

                sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                Thread.Sleep(300);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetClipboardText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero)) return;
            try
            {
                EmptyClipboard();
                //выделяем глобальную память под текст
                IntPtr hGlobal = Marshal.StringToHGlobalUni(text);
                //передаём os -> теперь Windows владелец!
                SetClipboardData(13, hGlobal);
                //НЕ освобождаем hGlobal, иначе краш!
            }
            finally
            {
                CloseClipboard();
            }
        }

        private static IntPtr FindWindowByCaption(IntPtr zeroOnly, string lpWindowName) => FindWindow(null, lpWindowName);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        public class ServerDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("ip")] public string Ip { get; set; }
            [JsonProperty("port")] public int Port { get; set; }
            [JsonProperty("online")] public bool Online { get; set; }
            [JsonProperty("players")] public int Players { get; set; }
            [JsonProperty("maxplayers")] public int MaxPlayers { get; set; }
            [JsonProperty("queue")] public int Queue { get; set; }
        }

        public class ApiServersResponse
        {
            [JsonProperty("response")] public List<ServerDto> Response { get; set; }
        }

        public class GameVersionDto
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("numeric")] public string Numeric { get; set; }
            [JsonProperty("supported_game_version")] public string SupportedGameVersion { get; set; }
            [JsonProperty("supported_ats_game_version")] public string SupportedAtsGameVersion { get; set; }
        }

        public class GameVersionResponse : GameVersionDto
        {
        }

        public class PlayerResponse
        {
            [JsonProperty("response")] public PlayerInfo Response { get; set; }
        }

        public class PlayerInfo
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("bans_count")] public int? BansCount { get; set; }
        }

        public class EventsResponse
        {
            [JsonProperty("response")] public EventsList Response { get; set; }
        }

        public class EventsList
        {
            [JsonProperty("now")] public List<EventDto> Now { get; set; }
            [JsonProperty("today")] public List<EventDto> Today { get; set; }
            [JsonProperty("upcoming")] public List<EventDto> Upcoming { get; set; }
        }

        public class EventDto
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("start_at")] public DateTime StartDate { get; set; }
        }
    }
}